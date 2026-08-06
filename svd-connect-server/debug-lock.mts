import { startTestServer } from "./tests/helpers/testServer.js";
import { TestClient } from "./tests/helpers/testClient.js";

const started = await startTestServer({
  maxFailedJoinAttempts: 2,
  failedJoinLockoutMs: 60_000
});

const host = await TestClient.connect(started.url);
host.send({ type: "create_room", requestId: "c1" });
const created = await host.waitFor("room_created");
const roomId = created["roomId"] as string;
const password = created["password"] as string;

const attacker = await TestClient.connect(started.url);
for (const [requestId, pwd] of [
  ["a1", "AAAAAAAA"],
  ["a2", "BBBBBBBB"],
  ["a3", password]
] as const) {
  attacker.send({ type: "join_room", requestId, roomId, password: pwd });
  const reply = await attacker.waitWhere((m) => m["requestId"] === requestId);
  console.log(requestId, "->", JSON.stringify(reply));
}

await attacker.close();
await host.close();
await started.stop();
