import type { WebSocket } from "ws";

import { ErrorCodes, ProtocolError } from "../protocol/errors.js";
import type { PasswordService } from "../security/PasswordService.js";
import type { Logger } from "../utilities/logger.js";
import {
  DEFAULT_PASSWORD_LENGTH,
  DEFAULT_ROOM_ID_LENGTH,
  generatePassword,
  generatePeerId,
  generateUniqueRoomId
} from "./RoomIdGenerator.js";
import { getHost, hasClients, isRoomExpired, isRoomFull, type PeerConnection, type Room } from "./Room.js";

export interface RoomManagerOptions {
  passwordService: PasswordService;
  logger: Logger;
  maxPlayersPerRoom: number;
  roomTtlMs: number;
  roomIdLength?: number;
  passwordLength?: number;
  /** Injectable clock; tests use it to fast-forward room expiry. */
  now?: () => number;
}

export interface CreateRoomParams {
  socket: WebSocket;
  remoteAddress?: string | undefined;
  maxPlayers?: number | undefined;
}

export interface CreateRoomResult {
  room: Room;
  peer: PeerConnection;
  /** Plaintext password - returned once to the host and then dropped. */
  password: string;
}

export interface JoinRoomParams {
  roomId: string;
  password: string;
  socket: WebSocket;
  remoteAddress?: string | undefined;
}

export interface JoinRoomResult {
  room: Room;
  peer: PeerConnection;
  host: PeerConnection;
}

export interface RemovePeerResult {
  room: Room;
  peer: PeerConnection;
  /** True when removing this peer also destroyed the room (host left). */
  roomRemoved: boolean;
  /** Peers that are still connected and should be notified. */
  notify: PeerConnection[];
}

export interface RoomManagerStats {
  rooms: number;
  peers: number;
}

/**
 * Owns the room registry. This class is deliberately transport-agnostic: it
 * never writes to a socket, it only reports which peers the caller should
 * notify. That keeps the room rules unit-testable without a live server.
 */
export class RoomManager {
  private readonly rooms = new Map<string, Room>();
  /** peerId -> roomId, so peer lookups do not scan every room. */
  private readonly peerIndex = new Map<string, string>();

  private readonly passwordService: PasswordService;
  private readonly logger: Logger;
  private readonly maxPlayersPerRoom: number;
  private readonly roomTtlMs: number;
  private readonly roomIdLength: number;
  private readonly passwordLength: number;
  private readonly now: () => number;

  public constructor(options: RoomManagerOptions) {
    this.passwordService = options.passwordService;
    this.logger = options.logger.child({ component: "RoomManager" });
    this.maxPlayersPerRoom = options.maxPlayersPerRoom;
    this.roomTtlMs = options.roomTtlMs;
    this.roomIdLength = options.roomIdLength ?? DEFAULT_ROOM_ID_LENGTH;
    this.passwordLength = options.passwordLength ?? DEFAULT_PASSWORD_LENGTH;
    this.now = options.now ?? (() => Date.now());
  }

  public async createRoom(params: CreateRoomParams): Promise<CreateRoomResult> {
    const roomId = generateUniqueRoomId((candidate) => this.rooms.has(candidate), this.roomIdLength);
    const password = generatePassword(this.passwordLength);
    const passwordHash = await this.passwordService.hash(password);

    const createdAt = this.now();
    const hostPeerId = generatePeerId();
    const maxPlayers = Math.min(params.maxPlayers ?? this.maxPlayersPerRoom, this.maxPlayersPerRoom);

    const host: PeerConnection = {
      peerId: hostPeerId,
      socket: params.socket,
      role: "host",
      connectedAt: createdAt,
      lastHeartbeatAt: createdAt
    };
    if (params.remoteAddress !== undefined) host.remoteAddress = params.remoteAddress;

    const room: Room = {
      roomId,
      passwordHash,
      hostPeerId,
      peers: new Map([[hostPeerId, host]]),
      createdAt,
      expiresAt: createdAt + this.roomTtlMs,
      maxPlayers
    };

    this.rooms.set(roomId, room);
    this.peerIndex.set(hostPeerId, roomId);

    this.logger.info("room created", { roomId, hostPeerId, maxPlayers, expiresAt: room.expiresAt });
    return { room, peer: host, password };
  }

  /**
   * Validates credentials and admits a client.
   *
   * Every failure that could reveal whether a room exists is reported as
   * `INVALID_ROOM_CREDENTIALS`, and the "no such room" path still performs a
   * throwaway scrypt verification so the response time does not leak the answer.
   */
  public async joinRoom(params: JoinRoomParams): Promise<JoinRoomResult> {
    const roomId = params.roomId.toUpperCase();
    const room = this.rooms.get(roomId);
    const now = this.now();

    if (room === undefined || isRoomExpired(room, now) || getHost(room) === undefined) {
      await this.passwordService.burnVerify(params.password);
      throw new ProtocolError(ErrorCodes.INVALID_ROOM_CREDENTIALS, {
        logContext: { roomId, reason: room === undefined ? "unknown_room" : "expired_or_hostless" }
      });
    }

    const passwordMatches = await this.passwordService.verify(params.password, room.passwordHash);
    if (!passwordMatches) {
      throw new ProtocolError(ErrorCodes.INVALID_ROOM_CREDENTIALS, {
        logContext: { roomId, reason: "bad_password" }
      });
    }

    // The room may have been destroyed while the hash was being verified.
    const current = this.rooms.get(roomId);
    if (current === undefined || current !== room) {
      throw new ProtocolError(ErrorCodes.INVALID_ROOM_CREDENTIALS, {
        logContext: { roomId, reason: "room_vanished" }
      });
    }

    const host = getHost(current);
    if (host === undefined) {
      throw new ProtocolError(ErrorCodes.INVALID_ROOM_CREDENTIALS, {
        logContext: { roomId, reason: "host_gone" }
      });
    }

    if (isRoomFull(current)) {
      throw new ProtocolError(ErrorCodes.ROOM_FULL, { logContext: { roomId } });
    }

    const peerId = generatePeerId();
    const peer: PeerConnection = {
      peerId,
      socket: params.socket,
      role: "client",
      connectedAt: now,
      lastHeartbeatAt: now
    };
    if (params.remoteAddress !== undefined) peer.remoteAddress = params.remoteAddress;

    current.peers.set(peerId, peer);
    this.peerIndex.set(peerId, roomId);

    this.logger.info("peer joined room", { roomId, peerId, peerCount: current.peers.size });
    return { room: current, peer, host };
  }

  public getRoom(roomId: string): Room | undefined {
    return this.rooms.get(roomId.toUpperCase());
  }

  public getRoomOfPeer(peerId: string): Room | undefined {
    const roomId = this.peerIndex.get(peerId);
    return roomId === undefined ? undefined : this.rooms.get(roomId);
  }

  public getPeer(peerId: string): PeerConnection | undefined {
    return this.getRoomOfPeer(peerId)?.peers.get(peerId);
  }

  /**
   * Removes a peer. When the host leaves the room is destroyed and every
   * remaining client is returned so the caller can send `host_disconnected`.
   */
  public removePeer(peerId: string): RemovePeerResult | undefined {
    const room = this.getRoomOfPeer(peerId);
    if (room === undefined) {
      this.peerIndex.delete(peerId);
      return undefined;
    }

    const peer = room.peers.get(peerId);
    if (peer === undefined) {
      this.peerIndex.delete(peerId);
      return undefined;
    }

    room.peers.delete(peerId);
    this.peerIndex.delete(peerId);

    if (peer.role === "host") {
      const remaining = [...room.peers.values()];
      for (const other of remaining) this.peerIndex.delete(other.peerId);
      room.peers.clear();
      this.rooms.delete(room.roomId);
      this.logger.info("room removed (host left)", { roomId: room.roomId, orphanedPeers: remaining.length });
      return { room, peer, roomRemoved: true, notify: remaining };
    }

    // Last client left: re-arm the waiting deadline instead of keeping the room forever.
    if (!hasClients(room)) {
      room.expiresAt = this.now() + this.roomTtlMs;
    }

    const host = getHost(room);
    this.logger.info("peer left room", { roomId: room.roomId, peerId, peerCount: room.peers.size });
    return { room, peer, roomRemoved: false, notify: host === undefined ? [] : [host] };
  }

  /** Destroys a room and returns every peer that was still in it. */
  public closeRoom(roomId: string): { room: Room; peers: PeerConnection[] } | undefined {
    const room = this.rooms.get(roomId.toUpperCase());
    if (room === undefined) return undefined;

    const peers = [...room.peers.values()];
    for (const peer of peers) this.peerIndex.delete(peer.peerId);
    room.peers.clear();
    this.rooms.delete(room.roomId);
    this.logger.info("room closed", { roomId: room.roomId, peers: peers.length });
    return { room, peers };
  }

  /** Removes every room whose waiting deadline elapsed. */
  public sweepExpired(now: number = this.now()): Array<{ room: Room; peers: PeerConnection[] }> {
    const removed: Array<{ room: Room; peers: PeerConnection[] }> = [];
    for (const room of [...this.rooms.values()]) {
      if (!isRoomExpired(room, now)) continue;
      const closed = this.closeRoom(room.roomId);
      if (closed !== undefined) {
        this.logger.info("room expired", { roomId: room.roomId });
        removed.push(closed);
      }
    }
    return removed;
  }

  /** Tears everything down; used on graceful shutdown. */
  public clear(): Array<{ room: Room; peers: PeerConnection[] }> {
    const all: Array<{ room: Room; peers: PeerConnection[] }> = [];
    for (const roomId of [...this.rooms.keys()]) {
      const closed = this.closeRoom(roomId);
      if (closed !== undefined) all.push(closed);
    }
    this.peerIndex.clear();
    return all;
  }

  public stats(): RoomManagerStats {
    return { rooms: this.rooms.size, peers: this.peerIndex.size };
  }

  public get roomCount(): number {
    return this.rooms.size;
  }
}
