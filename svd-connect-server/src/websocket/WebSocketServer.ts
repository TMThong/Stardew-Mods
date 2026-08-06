import { createServer as createHttpServer, type IncomingMessage, type Server, type ServerResponse } from "node:http";
import { createServer as createHttpsServer } from "node:https";
import type { AddressInfo } from "node:net";
import { WebSocket, WebSocketServer as WsServer, type RawData } from "ws";

import type { AppConfig } from "../config.js";
import { CloseCodes, ErrorCodes } from "../protocol/errors.js";
import { RoomManager } from "../rooms/RoomManager.js";
import { PasswordService } from "../security/PasswordService.js";
import { ConnectionCounter, FailedAttemptTracker, SlidingWindowRateLimiter } from "../security/RateLimiter.js";
import type { Logger } from "../utilities/logger.js";
import { Metrics } from "../utilities/metrics.js";
import { ConnectionContext } from "./ConnectionContext.js";
import { MessageRouter } from "./MessageRouter.js";

export interface SignalingServerOptions {
  config: AppConfig;
  logger: Logger;
  /** Override for tests (cheaper scrypt parameters). */
  passwordService?: PasswordService;
}

/**
 * Owns the HTTP + WebSocket listener and every background timer.
 *
 * Responsibilities kept here (and nowhere else):
 *  - accepting/rejecting upgrades (origin allowlist, per-IP connection cap, TLS policy),
 *  - heartbeat ping/pong and dead connection reaping,
 *  - periodic room cleanup,
 *  - `/health` and `/metrics`,
 *  - graceful shutdown.
 */
export class SignalingServer {
  private readonly config: AppConfig;
  private readonly logger: Logger;
  private readonly metrics = new Metrics();
  private readonly roomManager: RoomManager;
  private readonly router: MessageRouter;
  private readonly connectionCounter: ConnectionCounter;
  private readonly roomCreationLimiter: SlidingWindowRateLimiter;
  private readonly failedJoinTracker: FailedAttemptTracker;

  private readonly httpServer: Server;
  private readonly wss: WsServer;
  private readonly connections = new Set<ConnectionContext>();

  private heartbeatTimer: NodeJS.Timeout | null = null;
  private cleanupTimer: NodeJS.Timeout | null = null;
  private started = false;
  private stopping = false;

  public constructor(options: SignalingServerOptions) {
    this.config = options.config;
    this.logger = options.logger.child({ component: "SignalingServer" });

    const passwordService = options.passwordService ?? new PasswordService();
    this.roomManager = new RoomManager({
      passwordService,
      logger: options.logger,
      maxPlayersPerRoom: this.config.maxPlayersPerRoom,
      roomTtlMs: this.config.roomTtlMs
    });

    this.connectionCounter = new ConnectionCounter(this.config.maxConnectionsPerIp);
    this.roomCreationLimiter = new SlidingWindowRateLimiter(
      this.config.maxRoomsPerIpWindow,
      this.config.roomCreationWindowMs
    );
    this.failedJoinTracker = new FailedAttemptTracker(
      this.config.maxFailedJoinAttempts,
      this.config.failedJoinLockoutMs
    );

    this.router = new MessageRouter({
      config: this.config,
      logger: options.logger,
      metrics: this.metrics,
      roomManager: this.roomManager,
      roomCreationLimiter: this.roomCreationLimiter,
      failedJoinTracker: this.failedJoinTracker
    });

    this.httpServer =
      this.config.tls === null
        ? createHttpServer((request, response) => this.handleHttpRequest(request, response))
        : createHttpsServer(
            { cert: this.config.tls.cert, key: this.config.tls.key },
            (request, response) => this.handleHttpRequest(request, response)
          );

    this.wss = new WsServer({
      server: this.httpServer,
      maxPayload: this.config.maxMessageSizeBytes,
      clientTracking: false,
      verifyClient: (info, done) => this.verifyClient(info, done)
    });

    this.wss.on("connection", (socket, request) => this.handleConnection(socket, request));
    this.wss.on("error", (error) => this.logger.error("websocket server error", { error: error.message }));
  }

  // ---------------------------------------------------------------- lifecycle

  public async start(): Promise<void> {
    if (this.started) return;
    this.assertTransportPolicy();

    await new Promise<void>((resolve, reject) => {
      const onError = (error: Error): void => reject(error);
      this.httpServer.once("error", onError);
      this.httpServer.listen(this.config.port, this.config.host, () => {
        this.httpServer.off("error", onError);
        resolve();
      });
    });

    this.started = true;

    this.heartbeatTimer = setInterval(() => this.runHeartbeat(), this.config.heartbeatIntervalMs);
    this.heartbeatTimer.unref();
    this.cleanupTimer = setInterval(() => this.runCleanup(), this.config.cleanupIntervalMs);
    this.cleanupTimer.unref();

    this.logger.info("signaling server listening", {
      host: this.config.host,
      port: this.port,
      tls: this.config.tls !== null,
      nodeEnv: this.config.nodeEnv,
      maxPlayersPerRoom: this.config.maxPlayersPerRoom,
      roomTtlMinutes: Math.round(this.config.roomTtlMs / 60_000)
    });
  }

  public async stop(): Promise<void> {
    if (!this.started || this.stopping) return;
    this.stopping = true;
    this.logger.info("shutting down");

    if (this.heartbeatTimer !== null) clearInterval(this.heartbeatTimer);
    if (this.cleanupTimer !== null) clearInterval(this.cleanupTimer);
    this.heartbeatTimer = null;
    this.cleanupTimer = null;

    this.router.shutdownRooms();

    for (const ctx of [...this.connections]) {
      ctx.sendError(ErrorCodes.SERVER_SHUTTING_DOWN);
      ctx.close(CloseCodes.GOING_AWAY, "server shutting down");
    }

    await new Promise<void>((resolve) => {
      this.wss.close(() => resolve());
    });

    // Anything still hanging around after wss.close() gets cut off.
    for (const ctx of [...this.connections]) ctx.socket.terminate();
    this.connections.clear();

    await new Promise<void>((resolve) => {
      this.httpServer.close(() => resolve());
    });

    this.started = false;
    this.stopping = false;
    this.logger.info("shutdown complete");
  }

  public get port(): number {
    const address = this.httpServer.address();
    if (address === null || typeof address === "string") return this.config.port;
    return (address as AddressInfo).port;
  }

  public get websocketUrl(): string {
    const scheme = this.config.tls === null ? "ws" : "wss";
    return `${scheme}://127.0.0.1:${this.port}`;
  }

  public get connectionCount(): number {
    return this.connections.size;
  }

  public get roomCount(): number {
    return this.roomManager.roomCount;
  }

  /**
   * Refuses to boot a production instance that would accept plaintext traffic.
   * TLS may be terminated here (TLS_CERT_PATH/TLS_KEY_PATH) or at a trusted
   * reverse proxy (TRUST_PROXY=true).
   */
  private assertTransportPolicy(): void {
    if (!this.config.isProduction || !this.config.requireSecureTransport) return;
    if (this.config.tls !== null || this.config.trustProxy) return;
    throw new Error(
      "Refusing to start: NODE_ENV=production requires TLS (TLS_CERT_PATH/TLS_KEY_PATH) or TRUST_PROXY=true " +
        "behind an HTTPS reverse proxy. Set REQUIRE_SECURE_TRANSPORT=false to override."
    );
  }

  // ---------------------------------------------------------------- http

  private handleHttpRequest(request: IncomingMessage, response: ServerResponse): void {
    const url = request.url ?? "/";
    const path = url.split("?")[0] ?? "/";

    if (request.method !== "GET") {
      response.writeHead(405, { "content-type": "text/plain", allow: "GET" });
      response.end("Method Not Allowed");
      return;
    }

    if (path === "/health" || path === "/healthz") {
      const stats = this.roomManager.stats();
      const body = JSON.stringify({
        status: this.stopping ? "shutting_down" : "ok",
        uptimeSeconds: Math.floor(process.uptime()),
        rooms: stats.rooms,
        peers: stats.peers,
        connections: this.connections.size
      });
      response.writeHead(this.stopping ? 503 : 200, { "content-type": "application/json" });
      response.end(body);
      return;
    }

    if (path === "/metrics") {
      if (!this.config.metricsEnabled) {
        response.writeHead(404, { "content-type": "text/plain" });
        response.end("Not Found");
        return;
      }
      const stats = this.roomManager.stats();
      response.writeHead(200, { "content-type": "text/plain; version=0.0.4" });
      response.end(
        this.metrics.toPrometheus({
          rooms: stats.rooms,
          peers: stats.peers,
          connections: this.connections.size
        })
      );
      return;
    }

    response.writeHead(404, { "content-type": "text/plain" });
    response.end("Not Found");
  }

  // ---------------------------------------------------------------- websocket

  private verifyClient(
    info: { origin: string; secure: boolean; req: IncomingMessage },
    done: (result: boolean, code?: number, message?: string) => void
  ): void {
    if (this.stopping) {
      done(false, 503, "Server shutting down");
      return;
    }

    // Native clients (the SMAPI mod) do not send an Origin header; only browser
    // origins are filtered, and the allowlist is never the sole authentication.
    const origin = info.origin?.trim().toLowerCase();
    if (this.config.allowedOrigins.length > 0 && origin !== undefined && origin !== "") {
      if (!this.config.allowedOrigins.includes(origin)) {
        this.logger.warn("rejected upgrade: origin not allowed", { origin });
        done(false, 403, "Origin not allowed");
        return;
      }
    }

    if (this.config.isProduction && this.config.requireSecureTransport && this.config.tls === null) {
      const forwardedProto = this.headerValue(info.req, "x-forwarded-proto")?.split(",")[0]?.trim().toLowerCase();
      if (this.config.trustProxy && forwardedProto !== undefined && !["https", "wss"].includes(forwardedProto)) {
        this.logger.warn("rejected upgrade: insecure transport", { forwardedProto });
        done(false, 403, "Secure transport required");
        return;
      }
    }

    done(true);
  }

  private handleConnection(socket: WebSocket, request: IncomingMessage): void {
    const remoteAddress = this.resolveRemoteAddress(request);
    const ctx = new ConnectionContext({ socket, remoteAddress });

    this.metrics.increment("connections_total");
    const { count, overLimit } = this.connectionCounter.increment(remoteAddress);

    if (overLimit) {
      this.metrics.increment("connections_rejected_total");
      this.logger.warn("rejected connection: too many concurrent connections", { remoteAddress, count });
      ctx.sendError(ErrorCodes.TOO_MANY_CONNECTIONS);
      ctx.close(CloseCodes.POLICY_VIOLATION, "too many connections");
      this.connectionCounter.decrement(remoteAddress);
      return;
    }

    this.connections.add(ctx);
    this.logger.debug("connection opened", { ...ctx.logContext(), connectionsFromIp: count });

    socket.on("message", (data: RawData, isBinary: boolean) => {
      void this.router.handleRaw(ctx, data, isBinary).catch((error: unknown) => {
        this.logger.error("message handler crashed", {
          ...ctx.logContext(),
          error: error instanceof Error ? error.message : String(error)
        });
      });
    });

    socket.on("pong", () => {
      ctx.isAlive = true;
      ctx.lastHeartbeatAt = Date.now();
      const peer = ctx.peerId === null ? undefined : this.roomManager.getPeer(ctx.peerId);
      if (peer !== undefined) peer.lastHeartbeatAt = ctx.lastHeartbeatAt;
    });

    socket.on("error", (error: Error) => {
      this.logger.debug("socket error", { ...ctx.logContext(), error: error.message });
    });

    socket.on("close", (code: number) => {
      this.connections.delete(ctx);
      this.connectionCounter.decrement(remoteAddress);
      this.router.handleDisconnect(ctx);
      this.logger.debug("connection closed", { ...ctx.logContext(), code });
    });
  }

  private runHeartbeat(): void {
    const now = Date.now();
    for (const ctx of [...this.connections]) {
      if (!ctx.isAlive) {
        this.metrics.increment("heartbeat_timeouts_total");
        this.logger.info("terminating unresponsive connection", {
          ...ctx.logContext(),
          idleMs: now - ctx.lastHeartbeatAt
        });
        this.router.handleDisconnect(ctx, "timeout");
        this.connections.delete(ctx);
        this.connectionCounter.decrement(ctx.remoteAddress);
        ctx.socket.terminate();
        continue;
      }

      ctx.isAlive = false;
      if (ctx.socket.readyState === WebSocket.OPEN) ctx.socket.ping();
    }
  }

  private runCleanup(): void {
    const now = Date.now();
    const expired = this.router.sweepExpiredRooms(now);
    this.roomCreationLimiter.sweep(now);
    this.failedJoinTracker.sweep(now);
    if (expired > 0) this.logger.info("cleanup removed expired rooms", { rooms: expired });
  }

  private resolveRemoteAddress(request: IncomingMessage): string {
    if (this.config.trustProxy) {
      const forwarded = this.headerValue(request, "x-forwarded-for");
      const first = forwarded?.split(",")[0]?.trim();
      if (first !== undefined && first !== "") return first;
    }
    return request.socket.remoteAddress ?? "unknown";
  }

  private headerValue(request: IncomingMessage, name: string): string | undefined {
    const value = request.headers[name];
    if (Array.isArray(value)) return value[0];
    return value;
  }
}
