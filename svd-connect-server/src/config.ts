import { readFileSync } from "node:fs";
import { z } from "zod";

import { LOG_LEVELS, type LogLevel } from "./utilities/logger.js";

/** Coerce `"true" | "1" | "yes"` style env values into booleans. */
const booleanFromEnv = (defaultValue: boolean) =>
  z
    .union([z.boolean(), z.string()])
    .optional()
    .transform((value) => {
      if (value === undefined || value === "") return defaultValue;
      if (typeof value === "boolean") return value;
      return ["1", "true", "yes", "on"].includes(value.trim().toLowerCase());
    });

const intFromEnv = (defaultValue: number, min: number, max: number) =>
  z
    .string()
    .optional()
    .transform((value) => (value === undefined || value.trim() === "" ? String(defaultValue) : value))
    .pipe(z.coerce.number().int().min(min).max(max));

const envSchema = z.object({
  NODE_ENV: z.enum(["development", "test", "production"]).default("development"),
  HOST: z.string().min(1).default("0.0.0.0"),
  PORT: intFromEnv(8080, 0, 65535),

  ROOM_TTL_MINUTES: intFromEnv(30, 1, 24 * 60),
  MAX_PLAYERS_PER_ROOM: intFromEnv(8, 2, 64),
  CLEANUP_INTERVAL_SECONDS: intFromEnv(30, 1, 3600),

  MAX_ROOMS_PER_IP_WINDOW: intFromEnv(5, 1, 1000),
  ROOM_CREATION_WINDOW_MINUTES: intFromEnv(10, 1, 24 * 60),
  MAX_CONNECTIONS_PER_IP: intFromEnv(10, 1, 1000),
  MAX_FAILED_JOIN_ATTEMPTS: intFromEnv(5, 1, 100),
  FAILED_JOIN_LOCKOUT_MINUTES: intFromEnv(5, 1, 24 * 60),
  MAX_INVALID_MESSAGES_PER_CONNECTION: intFromEnv(5, 1, 1000),

  MAX_MESSAGE_SIZE_BYTES: intFromEnv(65_536, 1024, 4 * 1024 * 1024),
  MAX_SDP_LENGTH: intFromEnv(32_768, 128, 1024 * 1024),
  MAX_ICE_CANDIDATE_LENGTH: intFromEnv(1024, 32, 64 * 1024),

  HEARTBEAT_INTERVAL_SECONDS: intFromEnv(30, 5, 3600),

  LOG_LEVEL: z.enum(LOG_LEVELS as unknown as [LogLevel, ...LogLevel[]]).default("info"),
  LOG_JSON: z.string().optional(),

  ALLOWED_ORIGINS: z.string().optional().default(""),
  TRUST_PROXY: booleanFromEnv(false),
  REQUIRE_SECURE_TRANSPORT: booleanFromEnv(true),

  TLS_CERT_PATH: z.string().optional().default(""),
  TLS_KEY_PATH: z.string().optional().default(""),

  METRICS_ENABLED: booleanFromEnv(true)
});

export interface TlsConfig {
  cert: Buffer;
  key: Buffer;
}

export interface AppConfig {
  nodeEnv: "development" | "test" | "production";
  isProduction: boolean;
  host: string;
  port: number;

  roomTtlMs: number;
  maxPlayersPerRoom: number;
  cleanupIntervalMs: number;

  maxRoomsPerIpWindow: number;
  roomCreationWindowMs: number;
  maxConnectionsPerIp: number;
  maxFailedJoinAttempts: number;
  failedJoinLockoutMs: number;
  maxInvalidMessagesPerConnection: number;

  maxMessageSizeBytes: number;
  maxSdpLength: number;
  maxIceCandidateLength: number;

  heartbeatIntervalMs: number;

  logLevel: LogLevel;
  logJson: boolean;

  allowedOrigins: readonly string[];
  trustProxy: boolean;
  requireSecureTransport: boolean;

  tls: TlsConfig | null;

  metricsEnabled: boolean;
}

export class ConfigError extends Error {
  public constructor(message: string) {
    super(message);
    this.name = "ConfigError";
  }
}

function loadTls(certPath: string, keyPath: string): TlsConfig | null {
  const cert = certPath.trim();
  const key = keyPath.trim();
  if (cert === "" && key === "") return null;
  if (cert === "" || key === "") {
    throw new ConfigError("TLS_CERT_PATH and TLS_KEY_PATH must be set together.");
  }
  try {
    return { cert: readFileSync(cert), key: readFileSync(key) };
  } catch (error) {
    throw new ConfigError(`Unable to read TLS material: ${(error as Error).message}`);
  }
}

export function loadConfig(env: NodeJS.ProcessEnv = process.env): AppConfig {
  const parsed = envSchema.safeParse(env);
  if (!parsed.success) {
    const details = parsed.error.issues.map((issue) => `${issue.path.join(".")}: ${issue.message}`).join("; ");
    throw new ConfigError(`Invalid environment configuration -> ${details}`);
  }

  const raw = parsed.data;
  const isProduction = raw.NODE_ENV === "production";
  const logJson = raw.LOG_JSON === undefined || raw.LOG_JSON.trim() === ""
    ? isProduction
    : ["1", "true", "yes", "on"].includes(raw.LOG_JSON.trim().toLowerCase());

  const allowedOrigins = raw.ALLOWED_ORIGINS.split(",")
    .map((origin) => origin.trim().toLowerCase())
    .filter((origin) => origin.length > 0);

  return {
    nodeEnv: raw.NODE_ENV,
    isProduction,
    host: raw.HOST,
    port: raw.PORT,

    roomTtlMs: raw.ROOM_TTL_MINUTES * 60_000,
    maxPlayersPerRoom: raw.MAX_PLAYERS_PER_ROOM,
    cleanupIntervalMs: raw.CLEANUP_INTERVAL_SECONDS * 1000,

    maxRoomsPerIpWindow: raw.MAX_ROOMS_PER_IP_WINDOW,
    roomCreationWindowMs: raw.ROOM_CREATION_WINDOW_MINUTES * 60_000,
    maxConnectionsPerIp: raw.MAX_CONNECTIONS_PER_IP,
    maxFailedJoinAttempts: raw.MAX_FAILED_JOIN_ATTEMPTS,
    failedJoinLockoutMs: raw.FAILED_JOIN_LOCKOUT_MINUTES * 60_000,
    maxInvalidMessagesPerConnection: raw.MAX_INVALID_MESSAGES_PER_CONNECTION,

    maxMessageSizeBytes: raw.MAX_MESSAGE_SIZE_BYTES,
    maxSdpLength: raw.MAX_SDP_LENGTH,
    maxIceCandidateLength: raw.MAX_ICE_CANDIDATE_LENGTH,

    heartbeatIntervalMs: raw.HEARTBEAT_INTERVAL_SECONDS * 1000,

    logLevel: raw.LOG_LEVEL,
    logJson,

    allowedOrigins,
    trustProxy: raw.TRUST_PROXY,
    requireSecureTransport: raw.REQUIRE_SECURE_TRANSPORT,

    tls: loadTls(raw.TLS_CERT_PATH, raw.TLS_KEY_PATH),

    metricsEnabled: raw.METRICS_ENABLED
  };
}
