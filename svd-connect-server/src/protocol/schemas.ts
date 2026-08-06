import { z } from "zod";

import { ClientMessageTypes } from "./messages.js";

/**
 * Room ids use an unambiguous uppercase alphabet: no `0`, `O`, `1`, `I`.
 * `A-H J-N P-Z` drops `I` and `O`; digits are limited to `2-9`.
 */
export const ROOM_ID_PATTERN = /^[A-HJ-NP-Z2-9]{6}$/;

export interface SchemaLimits {
  maxSdpLength: number;
  maxIceCandidateLength: number;
}

export const roomIdSchema = z
  .string()
  .trim()
  .toUpperCase()
  .regex(ROOM_ID_PATTERN, "Room ID must be 6 unambiguous uppercase characters.");

/**
 * Passwords are validated loosely on join: the server generates 8 characters,
 * but rejecting a malformed password early would leak the exact format and give
 * an attacker a cheap oracle. Only an absolute length cap is enforced.
 */
export const passwordSchema = z.string().min(1).max(128);

export const requestIdSchema = z.string().min(1).max(64);
export const peerIdSchema = z.string().uuid();

function iceCandidateSchema(limits: SchemaLimits) {
  return z.object({
    candidate: z.string().max(limits.maxIceCandidateLength),
    sdpMid: z.string().max(64).nullish().transform((value) => value ?? null),
    sdpMLineIndex: z.number().int().min(0).max(1024).nullish().transform((value) => value ?? null)
  });
}

/**
 * Builds the discriminated union of every message a client may send.
 * Size limits come from configuration, so the schema is created per server
 * instance rather than being a module level singleton.
 */
export function createClientMessageSchema(limits: SchemaLimits) {
  const sdpSchema = z.string().min(1).max(limits.maxSdpLength);
  const candidateSchema = iceCandidateSchema(limits);

  const createRoom = z.object({
    type: z.literal(ClientMessageTypes.CREATE_ROOM),
    requestId: requestIdSchema,
    maxPlayers: z.number().int().min(2).max(64).optional()
  });

  const joinRoom = z.object({
    type: z.literal(ClientMessageTypes.JOIN_ROOM),
    requestId: requestIdSchema,
    roomId: roomIdSchema,
    password: passwordSchema
  });

  const leaveRoom = z.object({
    type: z.literal(ClientMessageTypes.LEAVE_ROOM),
    requestId: requestIdSchema.optional()
  });

  const closeRoom = z.object({
    type: z.literal(ClientMessageTypes.CLOSE_ROOM),
    requestId: requestIdSchema.optional()
  });

  const offer = z.object({
    type: z.literal(ClientMessageTypes.WEBRTC_OFFER),
    requestId: requestIdSchema.optional(),
    roomId: roomIdSchema,
    fromPeerId: peerIdSchema,
    targetPeerId: peerIdSchema,
    sdp: sdpSchema
  });

  const answer = z.object({
    type: z.literal(ClientMessageTypes.WEBRTC_ANSWER),
    requestId: requestIdSchema.optional(),
    roomId: roomIdSchema,
    fromPeerId: peerIdSchema,
    targetPeerId: peerIdSchema,
    sdp: sdpSchema
  });

  const ice = z.object({
    type: z.literal(ClientMessageTypes.ICE_CANDIDATE),
    requestId: requestIdSchema.optional(),
    roomId: roomIdSchema,
    fromPeerId: peerIdSchema,
    targetPeerId: peerIdSchema,
    candidate: candidateSchema
  });

  const ping = z.object({
    type: z.literal(ClientMessageTypes.PING),
    requestId: requestIdSchema.optional()
  });

  return z.discriminatedUnion("type", [createRoom, joinRoom, leaveRoom, closeRoom, offer, answer, ice, ping]);
}

export type ClientMessageSchema = ReturnType<typeof createClientMessageSchema>;
export type ClientMessage = z.infer<ClientMessageSchema>;

export type CreateRoomMessage = Extract<ClientMessage, { type: typeof ClientMessageTypes.CREATE_ROOM }>;
export type JoinRoomMessage = Extract<ClientMessage, { type: typeof ClientMessageTypes.JOIN_ROOM }>;
export type SignalMessage = Extract<
  ClientMessage,
  {
    type:
      | typeof ClientMessageTypes.WEBRTC_OFFER
      | typeof ClientMessageTypes.WEBRTC_ANSWER
      | typeof ClientMessageTypes.ICE_CANDIDATE;
  }
>;

/** Best-effort extraction of `requestId` from an unvalidated payload. */
export function peekRequestId(payload: unknown): string | undefined {
  if (typeof payload !== "object" || payload === null) return undefined;
  const value = (payload as Record<string, unknown>)["requestId"];
  return typeof value === "string" && value.length > 0 && value.length <= 64 ? value : undefined;
}
