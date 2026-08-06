import { afterAll, afterEach, beforeAll, describe, expect, it } from "vitest";

import type { AppConfig } from "../src/config.js";
import { ErrorCodes } from "../src/protocol/errors.js";
import { ROOM_ID_PATTERN } from "../src/protocol/schemas.js";
import type { SignalingServer } from "../src/websocket/WebSocketServer.js";
import { delay, TestClient } from "./helpers/testClient.js";
import { startTestServer } from "./helpers/testServer.js";

interface Harness {
  server: SignalingServer;
  url: string;
  connect: () => Promise<TestClient>;
  createRoom: (client?: TestClient) => Promise<{ client: TestClient; roomId: string; password: string; peerId: string }>;
  join: (roomId: string, password: string) => Promise<{ client: TestClient; peerId: string; hostPeerId: string }>;
}

/**
 * Boots a real server (ephemeral port) for a describe block and tears every
 * client down afterwards so the per-IP connection cap never leaks across tests.
 */
function harness(overrides: Partial<AppConfig> = {}): Harness {
  const clients: TestClient[] = [];
  let started: Awaited<ReturnType<typeof startTestServer>>;

  beforeAll(async () => {
    started = await startTestServer(overrides);
  });

  afterEach(async () => {
    await Promise.all(clients.splice(0).map((client) => client.close()));
  });

  afterAll(async () => {
    await started.stop();
  });

  const connect = async (): Promise<TestClient> => {
    const client = await TestClient.connect(started.url);
    clients.push(client);
    return client;
  };

  const createRoom = async (existing?: TestClient) => {
    const client = existing ?? (await connect());
    client.send({ type: "create_room", requestId: "req-create" });
    const created = await client.waitFor("room_created");
    return {
      client,
      roomId: created["roomId"] as string,
      password: created["password"] as string,
      peerId: created["peerId"] as string
    };
  };

  const join = async (roomId: string, password: string) => {
    const client = await connect();
    client.send({ type: "join_room", requestId: "req-join", roomId, password });
    const joined = await client.waitFor("room_joined");
    return {
      client,
      peerId: joined["peerId"] as string,
      hostPeerId: joined["hostPeerId"] as string
    };
  };

  return {
    get server() {
      return started.server;
    },
    get url() {
      return started.url;
    },
    connect,
    createRoom,
    join
  };
}

describe("room creation and joining", () => {
  const h = harness({ maxPlayersPerRoom: 3 });

  it("creates a room and returns credentials to the host", async () => {
    const client = await h.connect();
    client.send({ type: "create_room", requestId: "abc-123" });

    const created = await client.waitFor("room_created");
    expect(created["requestId"]).toBe("abc-123");
    expect(created["roomId"]).toMatch(ROOM_ID_PATTERN);
    expect(String(created["password"])).toHaveLength(8);
    expect(created["peerId"]).toMatch(/^[0-9a-f-]{36}$/);
    expect(created["maxPlayers"]).toBe(3);
    expect(Number.isNaN(Date.parse(String(created["expiresAt"])))).toBe(false);
    expect(h.server.roomCount).toBe(1);
  });

  it("lets a client join with the right password and tells the host", async () => {
    const room = await h.createRoom();
    const joiner = await h.join(room.roomId, room.password);

    expect(joiner.hostPeerId).toBe(room.peerId);
    expect(joiner.peerId).not.toBe(room.peerId);

    const peerJoined = await room.client.waitFor("peer_joined");
    expect(peerJoined["peerId"]).toBe(joiner.peerId);
    expect(peerJoined["roomId"]).toBe(room.roomId);
    expect(peerJoined["peerCount"]).toBe(2);
  });

  it("rejects a wrong password with the generic error", async () => {
    const room = await h.createRoom();
    const client = await h.connect();
    client.send({ type: "join_room", requestId: "bad-pass", roomId: room.roomId, password: "WRONGPWD" });

    const error = await client.waitFor("error");
    expect(error["code"]).toBe(ErrorCodes.INVALID_ROOM_CREDENTIALS);
    expect(error["requestId"]).toBe("bad-pass");
    expect(error["message"]).toBe("Room ID or password is invalid.");
  });

  it("returns the same error for a room that does not exist", async () => {
    const client = await h.connect();
    client.send({ type: "join_room", requestId: "no-room", roomId: "ZZZZZZ", password: "WHATEVER" });

    const error = await client.waitFor("error");
    expect(error["code"]).toBe(ErrorCodes.INVALID_ROOM_CREDENTIALS);
  });

  it("rejects a join once the room is full", async () => {
    const room = await h.createRoom();
    await h.join(room.roomId, room.password);
    await h.join(room.roomId, room.password);

    const client = await h.connect();
    client.send({ type: "join_room", requestId: "full", roomId: room.roomId, password: room.password });

    const error = await client.waitFor("error");
    expect(error["code"]).toBe(ErrorCodes.ROOM_FULL);
  });

  it("refuses to put one connection into two rooms", async () => {
    const room = await h.createRoom();
    room.client.send({ type: "create_room", requestId: "second" });

    const error = await room.client.waitFor("error");
    expect(error["code"]).toBe(ErrorCodes.ALREADY_IN_ROOM);
    expect(error["requestId"]).toBe("second");
  });

  it("answers application level pings", async () => {
    const client = await h.connect();
    client.send({ type: "ping", requestId: "hb-1" });

    const pong = await client.waitFor("pong");
    expect(pong["requestId"]).toBe("hb-1");
    expect(Number.isNaN(Date.parse(String(pong["serverTime"])))).toBe(false);
  });
});

describe("webrtc signaling relay", () => {
  const h = harness();

  it("relays offer, answer and ICE candidates between host and client", async () => {
    const room = await h.createRoom();
    const joiner = await h.join(room.roomId, room.password);
    await room.client.waitFor("peer_joined");

    room.client.send({
      type: "webrtc_offer",
      roomId: room.roomId,
      fromPeerId: room.peerId,
      targetPeerId: joiner.peerId,
      sdp: "v=0\r\no=- 1 1 IN IP4 127.0.0.1\r\n"
    });

    const offer = await joiner.client.waitFor("webrtc_offer");
    expect(offer["fromPeerId"]).toBe(room.peerId);
    expect(offer["targetPeerId"]).toBe(joiner.peerId);
    expect(offer["roomId"]).toBe(room.roomId);
    expect(offer["sdp"]).toContain("v=0");

    joiner.client.send({
      type: "webrtc_answer",
      roomId: room.roomId,
      fromPeerId: joiner.peerId,
      targetPeerId: room.peerId,
      sdp: "v=0\r\na=answer\r\n"
    });

    const answer = await room.client.waitFor("webrtc_answer");
    expect(answer["fromPeerId"]).toBe(joiner.peerId);

    joiner.client.send({
      type: "ice_candidate",
      roomId: room.roomId,
      fromPeerId: joiner.peerId,
      targetPeerId: room.peerId,
      candidate: { candidate: "candidate:1 1 udp 2130706431 192.0.2.1 54321 typ host", sdpMid: "0", sdpMLineIndex: 0 }
    });

    const ice = await room.client.waitFor("ice_candidate");
    expect((ice["candidate"] as Record<string, unknown>)["sdpMid"]).toBe("0");
    expect((ice["candidate"] as Record<string, unknown>)["sdpMLineIndex"]).toBe(0);
  });

  it("rejects a spoofed fromPeerId and does not deliver the message", async () => {
    const room = await h.createRoom();
    const joiner = await h.join(room.roomId, room.password);

    joiner.client.send({
      type: "webrtc_offer",
      requestId: "spoof",
      roomId: room.roomId,
      fromPeerId: room.peerId, // claiming to be the host
      targetPeerId: room.peerId,
      sdp: "v=0\r\n"
    });

    const error = await joiner.client.waitFor("error");
    expect(error["code"]).toBe(ErrorCodes.PEER_ID_MISMATCH);

    await delay(100);
    expect(room.client.has("webrtc_offer")).toBe(false);
  });

  it("rejects a relay addressed to another room", async () => {
    const room = await h.createRoom();
    const joiner = await h.join(room.roomId, room.password);
    const otherRoom = await h.createRoom();

    joiner.client.send({
      type: "ice_candidate",
      requestId: "cross-room",
      roomId: otherRoom.roomId,
      fromPeerId: joiner.peerId,
      targetPeerId: otherRoom.peerId,
      candidate: { candidate: "candidate:1 1 udp 1 192.0.2.1 1 typ host", sdpMid: "0", sdpMLineIndex: 0 }
    });

    const error = await joiner.client.waitFor("error");
    expect(error["code"]).toBe(ErrorCodes.ROOM_MISMATCH);

    await delay(100);
    expect(otherRoom.client.has("ice_candidate")).toBe(false);
  });

  it("rejects a relay to a peer that is not in the room", async () => {
    const room = await h.createRoom();
    const joiner = await h.join(room.roomId, room.password);

    joiner.client.send({
      type: "webrtc_answer",
      requestId: "ghost",
      roomId: room.roomId,
      fromPeerId: joiner.peerId,
      targetPeerId: "00000000-0000-4000-8000-000000000000",
      sdp: "v=0\r\n"
    });

    const error = await joiner.client.waitFor("error");
    expect(error["code"]).toBe(ErrorCodes.PEER_NOT_FOUND);
  });

  it("rejects signaling from a connection that is not in a room", async () => {
    const client = await h.connect();
    client.send({
      type: "webrtc_offer",
      requestId: "orphan",
      roomId: "ABCDEF",
      fromPeerId: "00000000-0000-4000-8000-000000000001",
      targetPeerId: "00000000-0000-4000-8000-000000000002",
      sdp: "v=0\r\n"
    });

    const error = await client.waitFor("error");
    expect(error["code"]).toBe(ErrorCodes.NOT_IN_ROOM);
  });
});

describe("room lifecycle", () => {
  const h = harness();

  it("tells clients when the host disconnects and destroys the room", async () => {
    const room = await h.createRoom();
    const joiner = await h.join(room.roomId, room.password);
    await room.client.waitFor("peer_joined");

    await room.client.close();

    const hostGone = await joiner.client.waitFor("host_disconnected");
    expect(hostGone["roomId"]).toBe(room.roomId);

    const closed = await joiner.client.waitFor("room_closed");
    expect(closed["reason"]).toBe("host_disconnected");
    expect(h.server.roomCount).toBe(0);
  });

  it("tells the host when a client disconnects and keeps the room", async () => {
    const room = await h.createRoom();
    const joiner = await h.join(room.roomId, room.password);
    await room.client.waitFor("peer_joined");

    await joiner.client.close();

    const left = await room.client.waitFor("peer_left");
    expect(left["peerId"]).toBe(joiner.peerId);
    expect(left["reason"]).toBe("disconnected");
    expect(left["peerCount"]).toBe(1);
    expect(h.server.roomCount).toBe(1);
  });

  it("reports peer_left with reason 'left' for an explicit leave_room", async () => {
    const room = await h.createRoom();
    const joiner = await h.join(room.roomId, room.password);
    await room.client.waitFor("peer_joined");

    joiner.client.send({ type: "leave_room", requestId: "bye" });

    const left = await room.client.waitFor("peer_left");
    expect(left["reason"]).toBe("left");
    expect(left["peerId"]).toBe(joiner.peerId);
  });

  it("lets the host close the room and notifies every client", async () => {
    const room = await h.createRoom();
    const joiner = await h.join(room.roomId, room.password);
    await room.client.waitFor("peer_joined");

    room.client.send({ type: "close_room", requestId: "close" });

    const hostView = await room.client.waitFor("room_closed");
    expect(hostView["reason"]).toBe("host_closed");

    await joiner.client.waitFor("host_disconnected");
    const clientView = await joiner.client.waitFor("room_closed");
    expect(clientView["reason"]).toBe("host_closed");
    expect(h.server.roomCount).toBe(0);
  });

  it("does not let a client close the room", async () => {
    const room = await h.createRoom();
    const joiner = await h.join(room.roomId, room.password);

    joiner.client.send({ type: "close_room", requestId: "nope" });

    const error = await joiner.client.waitFor("error");
    expect(error["code"]).toBe(ErrorCodes.NOT_ROOM_HOST);
    expect(h.server.roomCount).toBe(1);
  });

  it("rejects leave_room when the connection is not in a room", async () => {
    const client = await h.connect();
    client.send({ type: "leave_room", requestId: "nothing" });

    const error = await client.waitFor("error");
    expect(error["code"]).toBe(ErrorCodes.NOT_IN_ROOM);
  });
});

describe("room expiry", () => {
  const h = harness({ roomTtlMs: 60, cleanupIntervalMs: 20 });

  it("expires a room that never received a client", async () => {
    const room = await h.createRoom();

    const closed = await room.client.waitFor("room_closed", 3000);
    expect(closed["reason"]).toBe("expired");
    expect(closed["roomId"]).toBe(room.roomId);
    expect(h.server.roomCount).toBe(0);
  });
});

describe("room creation rate limit", () => {
  // Isolated harness: the limiter is per address and every test connects from 127.0.0.1.
  const h = harness({ maxRoomsPerIpWindow: 2, roomCreationWindowMs: 60_000 });

  it("rate limits room creation per address", async () => {
    const first = await h.connect();
    first.send({ type: "create_room", requestId: "r1" });
    await first.waitFor("room_created");

    const second = await h.connect();
    second.send({ type: "create_room", requestId: "r2" });
    await second.waitFor("room_created");

    const third = await h.connect();
    third.send({ type: "create_room", requestId: "r3" });
    const error = await third.waitFor("error");
    expect(error["code"]).toBe(ErrorCodes.RATE_LIMITED);
    expect(error["requestId"]).toBe("r3");
  });
});

describe("abuse protection", () => {
  const h = harness({
    maxFailedJoinAttempts: 2,
    failedJoinLockoutMs: 60_000,
    maxInvalidMessagesPerConnection: 3,
    maxMessageSizeBytes: 4096,
    maxSdpLength: 256
  });

  it("locks an address out of a room after repeated wrong passwords", async () => {
    const room = await h.createRoom();
    const attacker = await h.connect();

    attacker.send({ type: "join_room", requestId: "a1", roomId: room.roomId, password: "AAAAAAAA" });
    expect((await attacker.waitWhere((m) => m["requestId"] === "a1"))["code"]).toBe(
      ErrorCodes.INVALID_ROOM_CREDENTIALS
    );

    attacker.send({ type: "join_room", requestId: "a2", roomId: room.roomId, password: "BBBBBBBB" });
    expect((await attacker.waitWhere((m) => m["requestId"] === "a2"))["code"]).toBe(
      ErrorCodes.INVALID_ROOM_CREDENTIALS
    );

    attacker.send({ type: "join_room", requestId: "a3", roomId: room.roomId, password: room.password });
    const locked = await attacker.waitWhere((m) => m["requestId"] === "a3");
    expect(locked["code"]).toBe(ErrorCodes.TOO_MANY_FAILED_ATTEMPTS);
  });

  it("rejects an oversized SDP", async () => {
    const room = await h.createRoom();
    const joiner = await h.join(room.roomId, room.password);

    joiner.client.send({
      type: "webrtc_answer",
      requestId: "huge",
      roomId: room.roomId,
      fromPeerId: joiner.peerId,
      targetPeerId: room.peerId,
      sdp: "x".repeat(300)
    });

    const error = await joiner.client.waitFor("error");
    expect(error["code"]).toBe(ErrorCodes.INVALID_MESSAGE);
    expect(error["requestId"]).toBe("huge");
  });

  it("closes a connection that keeps sending garbage", async () => {
    const client = await h.connect();

    client.sendRaw("not json at all");
    client.sendRaw("{ still not valid");
    client.sendRaw("{}");

    const closeCode = await client.waitForClose();
    expect(closeCode).toBe(1008);
    expect(client.messages.filter((message) => message.type === "error")).toHaveLength(3);
  });

  it("drops a frame larger than the configured maximum", async () => {
    const client = await h.connect();
    client.sendRaw(JSON.stringify({ type: "ping", requestId: "x".repeat(8192) }));

    const closeCode = await client.waitForClose();
    expect(closeCode).toBe(1009);
  });

  it("rejects binary frames", async () => {
    const client = await h.connect();
    client.sendRaw(Buffer.from([0x01, 0x02, 0x03]));

    const error = await client.waitFor("error");
    expect(error["code"]).toBe(ErrorCodes.INVALID_MESSAGE);
  });
});

describe("connection limits", () => {
  const h = harness({ maxConnectionsPerIp: 2 });

  it("refuses more than the allowed number of concurrent connections per address", async () => {
    await h.connect();
    await h.connect();

    const third = await h.connect();
    const error = await third.waitFor("error");
    expect(error["code"]).toBe(ErrorCodes.TOO_MANY_CONNECTIONS);
    expect(await third.waitForClose()).toBe(1008);
  });
});

describe("http endpoints", () => {
  const h = harness();

  it("serves /health", async () => {
    const room = await h.createRoom();
    const response = await fetch(`http://127.0.0.1:${h.server.port}/health`);
    expect(response.status).toBe(200);

    const body = (await response.json()) as Record<string, unknown>;
    expect(body["status"]).toBe("ok");
    expect(body["rooms"]).toBe(1);
    expect(body["peers"]).toBe(1);
    expect(typeof body["uptimeSeconds"]).toBe("number");
    expect(room.roomId).toMatch(ROOM_ID_PATTERN);
  });

  it("serves Prometheus metrics", async () => {
    const response = await fetch(`http://127.0.0.1:${h.server.port}/metrics`);
    expect(response.status).toBe(200);

    const text = await response.text();
    expect(text).toContain("svdconnect_rooms_created_total");
    expect(text).toContain("svdconnect_active_rooms");
    expect(text).toContain("# TYPE svdconnect_connections_total counter");
  });

  it("returns 404 for unknown paths", async () => {
    const response = await fetch(`http://127.0.0.1:${h.server.port}/nope`);
    expect(response.status).toBe(404);
  });
});
