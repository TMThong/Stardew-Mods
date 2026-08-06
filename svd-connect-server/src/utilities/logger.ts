/**
 * Minimal dependency-free structured logger.
 *
 * Two output modes:
 *  - `json: true`  -> newline delimited JSON, meant for production log shippers.
 *  - `json: false` -> human readable single line, meant for local development.
 *
 * The logger deliberately has no knowledge of the room/protocol layers so it can
 * be injected anywhere without creating import cycles.
 */

export type LogLevel = "error" | "warn" | "info" | "debug" | "trace";

export const LOG_LEVELS: readonly LogLevel[] = ["error", "warn", "info", "debug", "trace"];

const LEVEL_PRIORITY: Record<LogLevel, number> = {
  error: 0,
  warn: 1,
  info: 2,
  debug: 3,
  trace: 4
};

export type LogContext = Record<string, unknown>;

export interface Logger {
  readonly level: LogLevel;
  error(message: string, context?: LogContext): void;
  warn(message: string, context?: LogContext): void;
  info(message: string, context?: LogContext): void;
  debug(message: string, context?: LogContext): void;
  trace(message: string, context?: LogContext): void;
  /** Returns a logger that merges `bindings` into every log record. */
  child(bindings: LogContext): Logger;
  isLevelEnabled(level: LogLevel): boolean;
}

export interface LoggerOptions {
  level: LogLevel;
  json: boolean;
  /** Overridable output sink; defaults to stdout/stderr. */
  sink?: (level: LogLevel, line: string) => void;
}

export function isLogLevel(value: string): value is LogLevel {
  return (LOG_LEVELS as readonly string[]).includes(value);
}

function defaultSink(level: LogLevel, line: string): void {
  if (level === "error" || level === "warn") {
    process.stderr.write(`${line}\n`);
  } else {
    process.stdout.write(`${line}\n`);
  }
}

function formatContext(context: LogContext): string {
  const parts: string[] = [];
  for (const [key, value] of Object.entries(context)) {
    if (value === undefined) continue;
    parts.push(`${key}=${typeof value === "string" ? value : JSON.stringify(value)}`);
  }
  return parts.join(" ");
}

function safeStringify(record: Record<string, unknown>): string {
  const seen = new WeakSet<object>();
  return JSON.stringify(record, (_key, value: unknown) => {
    if (typeof value === "bigint") return value.toString();
    if (typeof value === "object" && value !== null) {
      if (seen.has(value)) return "[circular]";
      seen.add(value);
    }
    if (value instanceof Error) {
      return { name: value.name, message: value.message, stack: value.stack };
    }
    return value;
  });
}

export function createLogger(options: LoggerOptions): Logger {
  const sink = options.sink ?? defaultSink;
  const threshold = LEVEL_PRIORITY[options.level];

  function build(bindings: LogContext): Logger {
    const emit = (level: LogLevel, message: string, context?: LogContext): void => {
      if (LEVEL_PRIORITY[level] > threshold) return;

      const merged: LogContext = { ...bindings, ...context };
      const timestamp = new Date().toISOString();

      if (options.json) {
        sink(level, safeStringify({ time: timestamp, level, msg: message, ...merged }));
        return;
      }

      const tail = formatContext(merged);
      sink(level, `${timestamp} ${level.toUpperCase().padEnd(5)} ${message}${tail ? ` | ${tail}` : ""}`);
    };

    return {
      level: options.level,
      error: (message, context) => emit("error", message, context),
      warn: (message, context) => emit("warn", message, context),
      info: (message, context) => emit("info", message, context),
      debug: (message, context) => emit("debug", message, context),
      trace: (message, context) => emit("trace", message, context),
      child: (extra) => build({ ...bindings, ...extra }),
      isLevelEnabled: (level) => LEVEL_PRIORITY[level] <= threshold
    };
  }

  return build({});
}

/** A logger that swallows everything; handy in unit tests. */
export function createSilentLogger(): Logger {
  return createLogger({ level: "error", json: true, sink: () => undefined });
}

/**
 * SDP blobs and ICE candidates can contain host names, private IP addresses and
 * fingerprints. Never log them verbatim - log a size descriptor instead.
 */
export function describePayload(payload: string): string {
  return `<${payload.length} chars>`;
}
