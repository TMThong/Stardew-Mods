import { loadConfig, type AppConfig } from "../../src/config.js";
import { PasswordService } from "../../src/security/PasswordService.js";
import { createSilentLogger } from "../../src/utilities/logger.js";
import { SignalingServer } from "../../src/websocket/WebSocketServer.js";

/** scrypt parameters tuned for speed, not for production strength. */
export const FAST_SCRYPT = { N: 1024, r: 8, p: 1 };

export function testConfig(overrides: Partial<AppConfig> = {}): AppConfig {
  const base = loadConfig({ NODE_ENV: "test", PORT: "0", LOG_LEVEL: "error" });
  return { ...base, ...overrides };
}

export interface StartedTestServer {
  server: SignalingServer;
  url: string;
  stop: () => Promise<void>;
}

export async function startTestServer(overrides: Partial<AppConfig> = {}): Promise<StartedTestServer> {
  const config = testConfig(overrides);
  const server = new SignalingServer({
    config,
    logger: createSilentLogger(),
    passwordService: new PasswordService(FAST_SCRYPT)
  });
  await server.start();
  return {
    server,
    url: server.websocketUrl,
    stop: () => server.stop()
  };
}
