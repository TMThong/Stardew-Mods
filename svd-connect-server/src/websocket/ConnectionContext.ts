import { randomUUID } from "node:crypto";
import { WebSocket } from "ws";

import type { ErrorCode } from "../protocol/errors.js";
import { errorMessage, type ServerMessage } from "../protocol/messages.js";
import type { PeerRole } from "../rooms/Room.js";

/** Serialises and writes a message, ignoring sockets that are already closing. */
export function sendMessage(socket: WebSocket, message: ServerMessage): boolean {
  if (socket.readyState !== WebSocket.OPEN) return false;
  socket.send(JSON.stringify(message));
  return true;
}

export interface ConnectionContextOptions {
  socket: WebSocket;
  remoteAddress: string;
  now?: number;
}

/**
 * Per-socket state.
 *
 * `peerId` / `roomId` / `role` are assigned by the server only. Nothing a
 * client sends is ever trusted to change them, which is what makes peer id
 * spoofing detectable: the router compares `fromPeerId` against this object.
 */
export class ConnectionContext {
  public readonly id: string;
  public readonly socket: WebSocket;
  public readonly remoteAddress: string;
  public readonly connectedAt: number;

  public peerId: string | null = null;
  public roomId: string | null = null;
  public role: PeerRole | null = null;

  /** Heartbeat bookkeeping (WebSocket ping/pong frames). */
  public isAlive = true;
  public lastHeartbeatAt: number;

  public invalidMessageCount = 0;
  /** Guards against a client firing many expensive join attempts in parallel. */
  public joinInFlight = false;
  public closing = false;

  public constructor(options: ConnectionContextOptions) {
    this.id = randomUUID();
    this.socket = options.socket;
    this.remoteAddress = options.remoteAddress;
    this.connectedAt = options.now ?? Date.now();
    this.lastHeartbeatAt = this.connectedAt;
  }

  public get isInRoom(): boolean {
    return this.peerId !== null && this.roomId !== null;
  }

  public attachToRoom(roomId: string, peerId: string, role: PeerRole): void {
    this.roomId = roomId;
    this.peerId = peerId;
    this.role = role;
  }

  public detachFromRoom(): void {
    this.roomId = null;
    this.peerId = null;
    this.role = null;
  }

  public send(message: ServerMessage): boolean {
    return sendMessage(this.socket, message);
  }

  public sendError(code: ErrorCode, message?: string, requestId?: string): boolean {
    return this.send(errorMessage(code, message, requestId));
  }

  public close(code: number, reason: string): void {
    this.closing = true;
    if (this.socket.readyState === WebSocket.OPEN || this.socket.readyState === WebSocket.CONNECTING) {
      // `reason` is capped by the protocol at 123 bytes.
      this.socket.close(code, reason.slice(0, 120));
    }
  }

  public logContext(): Record<string, unknown> {
    return {
      connectionId: this.id,
      remoteAddress: this.remoteAddress,
      peerId: this.peerId ?? undefined,
      roomId: this.roomId ?? undefined,
      role: this.role ?? undefined
    };
  }
}
