import type { WebSocket } from "ws";

export type PeerRole = "host" | "client";

/**
 * A peer is one live WebSocket that has been admitted to a room.
 * The socket is stored in memory only - it is never persisted anywhere.
 */
export interface PeerConnection {
  peerId: string;
  socket: WebSocket;
  role: PeerRole;
  connectedAt: number;
  remoteAddress?: string;
  lastHeartbeatAt: number;
}

export interface Room {
  roomId: string;
  /** scrypt hash. The plaintext password is never retained by the server. */
  passwordHash: string;
  hostPeerId: string;
  peers: Map<string, PeerConnection>;
  createdAt: number;
  /**
   * Deadline for a room that is still *waiting* for its first client.
   * Once at least one client is connected the deadline is ignored, and it is
   * re-armed when the last client leaves.
   */
  expiresAt: number;
  maxPlayers: number;
}

export function getHost(room: Room): PeerConnection | undefined {
  return room.peers.get(room.hostPeerId);
}

export function getClients(room: Room): PeerConnection[] {
  return [...room.peers.values()].filter((peer) => peer.role === "client");
}

export function hasClients(room: Room): boolean {
  for (const peer of room.peers.values()) {
    if (peer.role === "client") return true;
  }
  return false;
}

export function isRoomFull(room: Room): boolean {
  return room.peers.size >= room.maxPlayers;
}

/** A room only ages out while nobody has joined the host yet. */
export function isRoomExpired(room: Room, now: number): boolean {
  return !hasClients(room) && now >= room.expiresAt;
}

export interface RoomSnapshot {
  roomId: string;
  hostPeerId: string;
  peerIds: string[];
  peerCount: number;
  maxPlayers: number;
  createdAt: number;
  expiresAt: number;
}

export function roomSnapshot(room: Room): RoomSnapshot {
  return {
    roomId: room.roomId,
    hostPeerId: room.hostPeerId,
    peerIds: [...room.peers.keys()],
    peerCount: room.peers.size,
    maxPlayers: room.maxPlayers,
    createdAt: room.createdAt,
    expiresAt: room.expiresAt
  };
}
