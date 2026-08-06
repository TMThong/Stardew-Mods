import type { ErrorCode } from "./errors.js";
import { DEFAULT_ERROR_MESSAGES } from "./errors.js";

/** Message types the client (mod) may send. */
export const ClientMessageTypes = {
  CREATE_ROOM: "create_room",
  JOIN_ROOM: "join_room",
  LEAVE_ROOM: "leave_room",
  CLOSE_ROOM: "close_room",
  WEBRTC_OFFER: "webrtc_offer",
  WEBRTC_ANSWER: "webrtc_answer",
  ICE_CANDIDATE: "ice_candidate",
  PING: "ping"
} as const;

export type ClientMessageType = (typeof ClientMessageTypes)[keyof typeof ClientMessageTypes];

/** Message types the server may send. */
export const ServerMessageTypes = {
  ROOM_CREATED: "room_created",
  ROOM_JOINED: "room_joined",
  PEER_JOINED: "peer_joined",
  PEER_LEFT: "peer_left",
  HOST_DISCONNECTED: "host_disconnected",
  ROOM_CLOSED: "room_closed",
  WEBRTC_OFFER: "webrtc_offer",
  WEBRTC_ANSWER: "webrtc_answer",
  ICE_CANDIDATE: "ice_candidate",
  PONG: "pong",
  ERROR: "error"
} as const;

export type ServerMessageType = (typeof ServerMessageTypes)[keyof typeof ServerMessageTypes];

export interface IceCandidatePayload {
  candidate: string;
  sdpMid: string | null;
  sdpMLineIndex: number | null;
}

export interface RoomCreatedMessage {
  type: typeof ServerMessageTypes.ROOM_CREATED;
  requestId: string;
  roomId: string;
  password: string;
  peerId: string;
  maxPlayers: number;
  expiresAt: string;
}

export interface RoomJoinedMessage {
  type: typeof ServerMessageTypes.ROOM_JOINED;
  requestId: string;
  roomId: string;
  peerId: string;
  hostPeerId: string;
  maxPlayers: number;
  peers: string[];
}

export interface PeerJoinedMessage {
  type: typeof ServerMessageTypes.PEER_JOINED;
  roomId: string;
  peerId: string;
  peerCount: number;
}

export interface PeerLeftMessage {
  type: typeof ServerMessageTypes.PEER_LEFT;
  roomId: string;
  peerId: string;
  peerCount: number;
  reason: PeerLeftReason;
}

export type PeerLeftReason = "left" | "disconnected" | "timeout" | "kicked";

export interface HostDisconnectedMessage {
  type: typeof ServerMessageTypes.HOST_DISCONNECTED;
  roomId: string;
}

export type RoomClosedReason = "host_closed" | "expired" | "host_disconnected" | "server_shutdown";

export interface RoomClosedMessage {
  type: typeof ServerMessageTypes.ROOM_CLOSED;
  roomId: string;
  reason: RoomClosedReason;
}

export interface WebRtcOfferMessage {
  type: typeof ServerMessageTypes.WEBRTC_OFFER;
  roomId: string;
  fromPeerId: string;
  targetPeerId: string;
  sdp: string;
}

export interface WebRtcAnswerMessage {
  type: typeof ServerMessageTypes.WEBRTC_ANSWER;
  roomId: string;
  fromPeerId: string;
  targetPeerId: string;
  sdp: string;
}

export interface IceCandidateMessage {
  type: typeof ServerMessageTypes.ICE_CANDIDATE;
  roomId: string;
  fromPeerId: string;
  targetPeerId: string;
  candidate: IceCandidatePayload;
}

export interface PongMessage {
  type: typeof ServerMessageTypes.PONG;
  requestId?: string;
  serverTime: string;
}

export interface ErrorMessage {
  type: typeof ServerMessageTypes.ERROR;
  requestId?: string;
  code: ErrorCode;
  message: string;
}

export type ServerMessage =
  | RoomCreatedMessage
  | RoomJoinedMessage
  | PeerJoinedMessage
  | PeerLeftMessage
  | HostDisconnectedMessage
  | RoomClosedMessage
  | WebRtcOfferMessage
  | WebRtcAnswerMessage
  | IceCandidateMessage
  | PongMessage
  | ErrorMessage;

export function errorMessage(code: ErrorCode, message?: string, requestId?: string): ErrorMessage {
  const payload: ErrorMessage = {
    type: ServerMessageTypes.ERROR,
    code,
    message: message ?? DEFAULT_ERROR_MESSAGES[code]
  };
  if (requestId !== undefined) payload.requestId = requestId;
  return payload;
}

export function pongMessage(requestId: string | undefined, serverTime: Date): PongMessage {
  const payload: PongMessage = {
    type: ServerMessageTypes.PONG,
    serverTime: serverTime.toISOString()
  };
  if (requestId !== undefined) payload.requestId = requestId;
  return payload;
}
