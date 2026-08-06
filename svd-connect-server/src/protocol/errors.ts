/**
 * Error codes shared with the StardewConnect mod.
 *
 * The wire format is intentionally coarse: a room that does not exist, a room
 * that expired and a wrong password all collapse into
 * `INVALID_ROOM_CREDENTIALS` so an attacker cannot enumerate live rooms.
 */
export const ErrorCodes = {
  INVALID_MESSAGE: "INVALID_MESSAGE",
  MESSAGE_TOO_LARGE: "MESSAGE_TOO_LARGE",
  UNSUPPORTED_MESSAGE_TYPE: "UNSUPPORTED_MESSAGE_TYPE",

  INVALID_ROOM_CREDENTIALS: "INVALID_ROOM_CREDENTIALS",
  ROOM_FULL: "ROOM_FULL",
  ALREADY_IN_ROOM: "ALREADY_IN_ROOM",
  NOT_IN_ROOM: "NOT_IN_ROOM",
  NOT_ROOM_HOST: "NOT_ROOM_HOST",

  PEER_NOT_FOUND: "PEER_NOT_FOUND",
  PEER_ID_MISMATCH: "PEER_ID_MISMATCH",
  ROOM_MISMATCH: "ROOM_MISMATCH",

  RATE_LIMITED: "RATE_LIMITED",
  TOO_MANY_CONNECTIONS: "TOO_MANY_CONNECTIONS",
  TOO_MANY_FAILED_ATTEMPTS: "TOO_MANY_FAILED_ATTEMPTS",

  SERVER_SHUTTING_DOWN: "SERVER_SHUTTING_DOWN",
  INTERNAL_ERROR: "INTERNAL_ERROR"
} as const;

export type ErrorCode = (typeof ErrorCodes)[keyof typeof ErrorCodes];

/** User-facing default text. Deliberately vague for credential failures. */
export const DEFAULT_ERROR_MESSAGES: Record<ErrorCode, string> = {
  INVALID_MESSAGE: "The message could not be understood.",
  MESSAGE_TOO_LARGE: "The message exceeds the maximum allowed size.",
  UNSUPPORTED_MESSAGE_TYPE: "The message type is not supported.",

  INVALID_ROOM_CREDENTIALS: "Room ID or password is invalid.",
  ROOM_FULL: "The room is full.",
  ALREADY_IN_ROOM: "This connection already belongs to a room.",
  NOT_IN_ROOM: "This connection is not in a room.",
  NOT_ROOM_HOST: "Only the room host may perform this action.",

  PEER_NOT_FOUND: "The target peer is not in this room.",
  PEER_ID_MISMATCH: "The sender peer id does not match this connection.",
  ROOM_MISMATCH: "The message targets a different room.",

  RATE_LIMITED: "Too many requests. Please try again later.",
  TOO_MANY_CONNECTIONS: "Too many concurrent connections from this address.",
  TOO_MANY_FAILED_ATTEMPTS: "Too many failed attempts. Please try again later.",

  SERVER_SHUTTING_DOWN: "The signaling server is shutting down.",
  INTERNAL_ERROR: "An unexpected server error occurred."
};

/** WebSocket close codes used by the server. */
export const CloseCodes = {
  NORMAL: 1000,
  GOING_AWAY: 1001,
  POLICY_VIOLATION: 1008,
  MESSAGE_TOO_BIG: 1009,
  INTERNAL_ERROR: 1011
} as const;

export interface ProtocolErrorOptions {
  /** Echoed back so the client can settle the matching pending request. */
  requestId?: string | undefined;
  /** When set, the connection is closed right after the error is delivered. */
  closeCode?: number | undefined;
  /** Extra structured data for server-side logs only - never sent to clients. */
  logContext?: Record<string, unknown> | undefined;
}

/** An error that maps 1:1 onto an `error` message on the wire. */
export class ProtocolError extends Error {
  public readonly code: ErrorCode;
  public readonly requestId: string | undefined;
  public readonly closeCode: number | undefined;
  public readonly logContext: Record<string, unknown> | undefined;

  public constructor(code: ErrorCode, options: ProtocolErrorOptions = {}, message?: string) {
    super(message ?? DEFAULT_ERROR_MESSAGES[code]);
    this.name = "ProtocolError";
    this.code = code;
    this.requestId = options.requestId;
    this.closeCode = options.closeCode;
    this.logContext = options.logContext;
  }
}

export function isProtocolError(error: unknown): error is ProtocolError {
  return error instanceof ProtocolError;
}
