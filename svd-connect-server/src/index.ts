import { ConfigError, loadConfig } from "./config.js";
import { createLogger } from "./utilities/logger.js";
import { SignalingServer } from "./websocket/WebSocketServer.js";

const SHUTDOWN_GRACE_MS = 10_000;

async function main(): Promise<void> {
  const config = loadConfig();
  const logger = createLogger({ level: config.logLevel, json: config.logJson });
  const server = new SignalingServer({ config, logger });

  let shuttingDown = false;
  const shutdown = (signal: string): void => {
    if (shuttingDown) return;
    shuttingDown = true;
    logger.info("received shutdown signal", { signal });

    const forceExit = setTimeout(() => {
      logger.error("graceful shutdown timed out; forcing exit");
      process.exit(1);
    }, SHUTDOWN_GRACE_MS);
    forceExit.unref();

    server
      .stop()
      .then(() => {
        clearTimeout(forceExit);
        process.exit(0);
      })
      .catch((error: unknown) => {
        logger.error("error during shutdown", { error: error instanceof Error ? error.message : String(error) });
        process.exit(1);
      });
  };

  process.on("SIGINT", () => shutdown("SIGINT"));
  process.on("SIGTERM", () => shutdown("SIGTERM"));

  process.on("unhandledRejection", (reason: unknown) => {
    logger.error("unhandled promise rejection", {
      error: reason instanceof Error ? reason.message : String(reason)
    });
  });

  process.on("uncaughtException", (error: Error) => {
    logger.error("uncaught exception", { error: error.message, stack: error.stack });
    shutdown("uncaughtException");
  });

  await server.start();
}

main().catch((error: unknown) => {
  if (error instanceof ConfigError) {
    process.stderr.write(`${error.message}\n`);
    process.exit(78); // EX_CONFIG
  }
  process.stderr.write(`Fatal startup error: ${error instanceof Error ? error.stack ?? error.message : String(error)}\n`);
  process.exit(1);
});
