import { WebSocket, type RawData } from "ws";

import type { AppConfig } from "../config.js";
import { CloseCodes, ErrorCodes, isProtocolError, ProtocolError } from "../protocol/errors.js";
import {
  ClientMessageTypes,
  ServerMessageTypes,
  pongMessage,
  type PeerLeftReason,
  type RoomClosedReason
} from "../protocol/messages.js";
import {
  createClientMessageSchema,
  peekRequestId,
  type ClientMessage,
  type ClientMessageSchema,
  type CreateRoomMessage,
  type JoinRoomMessage,
  type SignalMessage
} from "../protocol/schemas.js";
import type { PeerConnection, PeerRole } from "../rooms/Room.js";
import type { RoomManager } from "../rooms/RoomManager.js";
import type { FailedAttemptTracker, SlidingWindowRateLimiter } from "../security/RateLimiter.js";
import { describePayload, type Logger } from "../utilities/logger.js";
import type { Metrics } from "../utilities/metrics.js";
import { ConnectionContext, sendMessage } from "./ConnectionContext.js";

export interface MessageRouterOptions {
  config: AppConfig;
  logger: Logger;
  metrics: Metrics;
  roomManager: RoomManager;
  roomCreationLimiter: SlidingWindowRateLimiter;
  failedJoinTracker: FailedAttemptTracker;
}

/**
 * Validates, authorises and dispatches every client message.
 *
 * Trust rules enforced here:
 *  - a connection is bound to exactly one room, assigned by the server;
 *  - `fromPeerId` must equal the peer id the server issued to this socket;
 *  - `roomId` must equal the room this socket belongs to;
 *  - relays only ever go to a single peer inside the same room.
 */
export class MessageRouter {
  private readonly config: AppConfig;
  private readonly logger: Logger;
  private readonly metrics: Metrics;
  private readonly roomManager: RoomManager;
  private readonly roomCreationLimiter: SlidingWindowRateLimiter;
  private readonly failedJoinTracker: FailedAttemptTracker;
  private readonly schema: ClientMessageSchema;
  /** peerId -> live connection, so a closed room can detach every member. */
  private readonly contextsByPeerId = new Map<string, ConnectionContext>();

  public constructor(options: MessageRouterOptions) {
    this.config = options.config;
    this.logger = options.logger.child({ component: "MessageRouter" });
    this.metrics = options.metrics;
    this.roomManager = options.roomManager;
    this.roomCreationLimiter = options.roomCreationLimiter;
    this.failedJoinTracker = options.failedJoinTracker;
    this.schema = createClientMessageSchema({
      maxSdpLength: options.config.maxSdpLength,
      maxIceCandidateLength: options.config.maxIceCandidateLength
    });
  }

  // ---------------------------------------------------------------- entry points

  public async handleRaw(ctx: ConnectionContext, data: RawData, isBinary: boolean): Promise<void> {
    this.metrics.increment("messages_received_total");

    if (isBinary) {
      this.rejectInvalid(ctx, ErrorCodes.INVALID_MESSAGE, "Binary frames are not supported.", undefined);
      return;
    }

    const text = Array.isArray(data) ? Buffer.concat(data).toString("utf8") : data.toString();
    if (Buffer.byteLength(text, "utf8") > this.config.maxMessageSizeBytes) {
      ctx.sendError(ErrorCodes.MESSAGE_TOO_LARGE);
      ctx.close(CloseCodes.MESSAGE_TOO_BIG, "message too large");
      return;
    }

    let payload: unknown;
    try {
      payload = JSON.parse(text);
    } catch {
      this.rejectInvalid(ctx, ErrorCodes.INVALID_MESSAGE, "Payload is not valid JSON.", undefined);
      return;
    }

    const parsed = this.schema.safeParse(payload);
    if (!parsed.success) {
      const requestId = peekRequestId(payload);
      const firstIssue = parsed.error.issues[0];
      const detail = firstIssue === undefined ? "invalid payload" : `${firstIssue.path.join(".")}: ${firstIssue.message}`;
      this.logger.debug("rejected malformed message", { ...ctx.logContext(), detail });
      this.rejectInvalid(ctx, ErrorCodes.INVALID_MESSAGE, undefined, requestId);
      return;
    }

    try {
      await this.dispatch(ctx, parsed.data);
    } catch (error) {
      this.handleDispatchError(ctx, error);
    }
  }

  /** Called when a socket closes for any reason. */
  public handleDisconnect(ctx: ConnectionContext, reason: PeerLeftReason = "disconnected"): void {
    if (ctx.peerId === null) return;
    this.releasePeer(ctx, reason);
  }

  public shutdownRooms(): void {
    for (const { room, peers } of this.roomManager.clear()) {
      for (const peer of peers) {
        sendMessage(peer.socket, {
          type: ServerMessageTypes.ROOM_CLOSED,
          roomId: room.roomId,
          reason: "server_shutdown"
        });
        this.detachContext(peer.peerId);
      }
      this.metrics.increment("rooms_closed_total");
    }
  }

  /** Removes rooms whose waiting deadline elapsed and notifies their members. */
  public sweepExpiredRooms(now: number = Date.now()): number {
    const expired = this.roomManager.sweepExpired(now);
    for (const { room, peers } of expired) {
      for (const peer of peers) {
        sendMessage(peer.socket, {
          type: ServerMessageTypes.ROOM_CLOSED,
          roomId: room.roomId,
          reason: "expired"
        });
        this.detachContext(peer.peerId);
      }
      this.metrics.increment("rooms_expired_total");
      this.metrics.increment("rooms_closed_total");
    }
    return expired.length;
  }

  // ---------------------------------------------------------------- dispatch

  private async dispatch(ctx: ConnectionContext, message: ClientMessage): Promise<void> {
    switch (message.type) {
      case ClientMessageTypes.CREATE_ROOM:
        await this.handleCreateRoom(ctx, message);
        break;
      case ClientMessageTypes.JOIN_ROOM:
        await this.handleJoinRoom(ctx, message);
        break;
      case ClientMessageTypes.LEAVE_ROOM:
        this.handleLeaveRoom(ctx, message.requestId);
        break;
      case ClientMessageTypes.CLOSE_ROOM:
        this.handleCloseRoom(ctx, message.requestId);
        break;
      case ClientMessageTypes.WEBRTC_OFFER:
      case ClientMessageTypes.WEBRTC_ANSWER:
      case ClientMessageTypes.ICE_CANDIDATE:
        this.handleSignal(ctx, message);
        break;
      case ClientMessageTypes.PING:
        ctx.lastHeartbeatAt = Date.now();
        ctx.send(pongMessage(message.requestId, new Date()));
        break;
      default: {
        // Unreachable while the schema and this switch stay in sync.
        const exhaustive: never = message;
        throw new ProtocolError(ErrorCodes.UNSUPPORTED_MESSAGE_TYPE, {
          logContext: { message: exhaustive }
        });
      }
    }
  }

  private async handleCreateRoom(ctx: ConnectionContext, message: CreateRoomMessage): Promise<void> {
    if (ctx.isInRoom) {
      throw new ProtocolError(ErrorCodes.ALREADY_IN_ROOM, { requestId: message.requestId });
    }

    if (!this.roomCreationLimiter.tryConsume(ctx.remoteAddress)) {
      this.metrics.increment("rate_limited_total");
      const retryAfterSeconds = Math.ceil(this.roomCreationLimiter.retryAfterMs(ctx.remoteAddress) / 1000);
      throw new ProtocolError(
        ErrorCodes.RATE_LIMITED,
        { requestId: message.requestId, logContext: { remoteAddress: ctx.remoteAddress } },
        `Too many rooms created from this address. Try again in ${retryAfterSeconds}s.`
      );
    }

    const created = await this.roomManager.createRoom({
      socket: ctx.socket,
      remoteAddress: ctx.remoteAddress,
      maxPlayers: message.maxPlayers
    });

    // The socket may have died while the password was being hashed.
    if (ctx.closing || ctx.socket.readyState !== WebSocket.OPEN) {
      this.roomManager.closeRoom(created.room.roomId);
      return;
    }

    this.attachContext(ctx, created.room.roomId, created.peer.peerId, "host");
    this.metrics.increment("rooms_created_total");

    ctx.send({
      type: ServerMessageTypes.ROOM_CREATED,
      requestId: message.requestId,
      roomId: created.room.roomId,
      password: created.password,
      peerId: created.peer.peerId,
      maxPlayers: created.room.maxPlayers,
      expiresAt: new Date(created.room.expiresAt).toISOString()
    });
  }

  private async handleJoinRoom(ctx: ConnectionContext, message: JoinRoomMessage): Promise<void> {
    if (ctx.isInRoom) {
      throw new ProtocolError(ErrorCodes.ALREADY_IN_ROOM, { requestId: message.requestId });
    }
    if (ctx.joinInFlight) {
      throw new ProtocolError(
        ErrorCodes.RATE_LIMITED,
        { requestId: message.requestId },
        "A join attempt is already in progress."
      );
    }

    const lockKey = `${ctx.remoteAddress}|${message.roomId}`;
    if (this.failedJoinTracker.isLocked(lockKey)) {
      this.metrics.increment("rate_limited_total");
      const retryAfterSeconds = Math.ceil(this.failedJoinTracker.retryAfterMs(lockKey) / 1000);
      throw new ProtocolError(
        ErrorCodes.TOO_MANY_FAILED_ATTEMPTS,
        { requestId: message.requestId, logContext: { remoteAddress: ctx.remoteAddress, roomId: message.roomId } },
        `Too many failed attempts. Try again in ${retryAfterSeconds}s.`
      );
    }

    ctx.joinInFlight = true;
    try {
      const joined = await this.roomManager.joinRoom({
        roomId: message.roomId,
        password: message.password,
        socket: ctx.socket,
        remoteAddress: ctx.remoteAddress
      });

      if (ctx.closing || ctx.socket.readyState !== WebSocket.OPEN) {
        this.roomManager.removePeer(joined.peer.peerId);
        return;
      }

      this.failedJoinTracker.clear(lockKey);
      this.attachContext(ctx, joined.room.roomId, joined.peer.peerId, "client");
      this.metrics.increment("joins_total");

      ctx.send({
        type: ServerMessageTypes.ROOM_JOINED,
        requestId: message.requestId,
        roomId: joined.room.roomId,
        peerId: joined.peer.peerId,
        hostPeerId: joined.room.hostPeerId,
        maxPlayers: joined.room.maxPlayers,
        peers: [...joined.room.peers.keys()].filter((peerId) => peerId !== joined.peer.peerId)
      });

      sendMessage(joined.host.socket, {
        type: ServerMessageTypes.PEER_JOINED,
        roomId: joined.room.roomId,
        peerId: joined.peer.peerId,
        peerCount: joined.room.peers.size
      });
    } catch (error) {
      if (isProtocolError(error) && error.code === ErrorCodes.INVALID_ROOM_CREDENTIALS) {
        this.metrics.increment("join_failures_total");
        const locked = this.failedJoinTracker.registerFailure(lockKey);
        this.logger.warn("join rejected", {
          ...ctx.logContext(),
          roomId: message.roomId,
          locked,
          ...(error.logContext ?? {})
        });
        throw new ProtocolError(ErrorCodes.INVALID_ROOM_CREDENTIALS, { requestId: message.requestId });
      }
      if (isProtocolError(error)) {
        this.metrics.increment("join_failures_total");
        throw new ProtocolError(error.code, { requestId: message.requestId }, error.message);
      }
      throw error;
    } finally {
      ctx.joinInFlight = false;
    }
  }

  private handleLeaveRoom(ctx: ConnectionContext, requestId: string | undefined): void {
    if (!ctx.isInRoom) {
      throw new ProtocolError(ErrorCodes.NOT_IN_ROOM, { requestId });
    }
    this.releasePeer(ctx, "left");
  }

  private handleCloseRoom(ctx: ConnectionContext, requestId: string | undefined): void {
    if (!ctx.isInRoom || ctx.roomId === null) {
      throw new ProtocolError(ErrorCodes.NOT_IN_ROOM, { requestId });
    }
    if (ctx.role !== "host") {
      throw new ProtocolError(ErrorCodes.NOT_ROOM_HOST, { requestId });
    }

    const roomId = ctx.roomId;
    const closed = this.roomManager.closeRoom(roomId);
    this.detachContext(ctx.peerId);
    ctx.detachFromRoom();

    if (closed !== undefined) {
      this.metrics.increment("rooms_closed_total");
      for (const peer of closed.peers) {
        this.detachContext(peer.peerId);
        if (peer.socket === ctx.socket) continue;
        sendMessage(peer.socket, { type: ServerMessageTypes.HOST_DISCONNECTED, roomId });
        sendMessage(peer.socket, {
          type: ServerMessageTypes.ROOM_CLOSED,
          roomId,
          reason: "host_closed" satisfies RoomClosedReason
        });
      }
    }

    ctx.send({ type: ServerMessageTypes.ROOM_CLOSED, roomId, reason: "host_closed" });
  }

  private handleSignal(ctx: ConnectionContext, message: SignalMessage): void {
    if (!ctx.isInRoom || ctx.peerId === null || ctx.roomId === null) {
      throw new ProtocolError(ErrorCodes.NOT_IN_ROOM, { requestId: message.requestId });
    }
    if (message.fromPeerId !== ctx.peerId) {
      this.logger.warn("rejected spoofed fromPeerId", {
        ...ctx.logContext(),
        claimedPeerId: message.fromPeerId
      });
      throw new ProtocolError(ErrorCodes.PEER_ID_MISMATCH, { requestId: message.requestId });
    }
    if (message.roomId !== ctx.roomId) {
      this.logger.warn("rejected cross-room relay", {
        ...ctx.logContext(),
        targetRoomId: message.roomId
      });
      throw new ProtocolError(ErrorCodes.ROOM_MISMATCH, { requestId: message.requestId });
    }

    const room = this.roomManager.getRoom(ctx.roomId);
    if (room === undefined) {
      throw new ProtocolError(ErrorCodes.NOT_IN_ROOM, { requestId: message.requestId });
    }

    const target = room.peers.get(message.targetPeerId);
    if (target === undefined || target.peerId === ctx.peerId) {
      throw new ProtocolError(ErrorCodes.PEER_NOT_FOUND, { requestId: message.requestId });
    }

    this.relay(ctx, target, message);
  }

  private relay(ctx: ConnectionContext, target: PeerConnection, message: SignalMessage): void {
    const fromPeerId = ctx.peerId as string;
    const roomId = ctx.roomId as string;

    if (message.type === ClientMessageTypes.ICE_CANDIDATE) {
      sendMessage(target.socket, {
        type: ServerMessageTypes.ICE_CANDIDATE,
        roomId,
        fromPeerId,
        targetPeerId: target.peerId,
        candidate: message.candidate
      });
    } else {
      sendMessage(target.socket, {
        type:
          message.type === ClientMessageTypes.WEBRTC_OFFER
            ? ServerMessageTypes.WEBRTC_OFFER
            : ServerMessageTypes.WEBRTC_ANSWER,
        roomId,
        fromPeerId,
        targetPeerId: target.peerId,
        sdp: message.sdp
      });
    }

    this.metrics.increment("messages_relayed_total");

    // Never log SDP or candidate bodies: they contain addresses and fingerprints.
    if (this.logger.isLevelEnabled("debug")) {
      this.logger.debug("relayed signaling message", {
        roomId,
        fromPeerId,
        targetPeerId: target.peerId,
        messageType: message.type,
        payload:
          message.type === ClientMessageTypes.ICE_CANDIDATE
            ? describePayload(message.candidate.candidate)
            : describePayload(message.sdp)
      });
    }
  }

  // ---------------------------------------------------------------- helpers

  /** Detaches this connection from its room and notifies whoever needs to know. */
  private releasePeer(ctx: ConnectionContext, reason: PeerLeftReason): void {
    const peerId = ctx.peerId;
    if (peerId === null) return;

    const result = this.roomManager.removePeer(peerId);
    this.detachContext(peerId);
    ctx.detachFromRoom();

    if (result === undefined) return;

    if (result.roomRemoved) {
      this.metrics.increment("rooms_closed_total");
      for (const peer of result.notify) {
        sendMessage(peer.socket, {
          type: ServerMessageTypes.HOST_DISCONNECTED,
          roomId: result.room.roomId
        });
        sendMessage(peer.socket, {
          type: ServerMessageTypes.ROOM_CLOSED,
          roomId: result.room.roomId,
          reason: "host_disconnected" satisfies RoomClosedReason
        });
        this.detachContext(peer.peerId);
      }
      return;
    }

    for (const peer of result.notify) {
      sendMessage(peer.socket, {
        type: ServerMessageTypes.PEER_LEFT,
        roomId: result.room.roomId,
        peerId,
        peerCount: result.room.peers.size,
        reason
      });
    }
  }

  private attachContext(ctx: ConnectionContext, roomId: string, peerId: string, role: PeerRole): void {
    ctx.attachToRoom(roomId, peerId, role);
    this.contextsByPeerId.set(peerId, ctx);
  }

  private detachContext(peerId: string | null): void {
    if (peerId === null) return;
    const other = this.contextsByPeerId.get(peerId);
    this.contextsByPeerId.delete(peerId);
    if (other !== undefined) other.detachFromRoom();
  }

  private rejectInvalid(
    ctx: ConnectionContext,
    code: typeof ErrorCodes.INVALID_MESSAGE,
    message: string | undefined,
    requestId: string | undefined
  ): void {
    this.metrics.increment("invalid_messages_total");
    ctx.invalidMessageCount += 1;
    ctx.sendError(code, message, requestId);

    if (ctx.invalidMessageCount >= this.config.maxInvalidMessagesPerConnection) {
      this.logger.warn("closing connection after repeated invalid messages", ctx.logContext());
      ctx.close(CloseCodes.POLICY_VIOLATION, "too many invalid messages");
    }
  }

  private handleDispatchError(ctx: ConnectionContext, error: unknown): void {
    if (isProtocolError(error)) {
      ctx.sendError(error.code, error.message, error.requestId);
      if (error.logContext !== undefined) {
        this.logger.debug("protocol error", { ...ctx.logContext(), code: error.code, ...error.logContext });
      }
      if (error.closeCode !== undefined) ctx.close(error.closeCode, error.code);
      return;
    }

    this.logger.error("unhandled error while dispatching message", {
      ...ctx.logContext(),
      error: error instanceof Error ? error.message : String(error)
    });
    ctx.sendError(ErrorCodes.INTERNAL_ERROR);
  }
}
