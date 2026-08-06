#!/usr/bin/env node
/**
 * End-to-end smoke test for a running svd-connect-server.
 *
 *   node scripts/manual-test.mjs [url]
 *
 * Default url: ws://127.0.0.1:8080
 *
 * It walks the exact flow the StardewConnect mod performs:
 *   1. host connects and creates a room
 *   2. client connects and joins with the room id + password
 *   3. host receives peer_joined
 *   4. host -> client offer, client -> host answer, client -> host ICE
 *   5. a spoofed fromPeerId is rejected
 *   6. client leaves (host sees peer_left), host closes the room
 *
 * Every frame is printed, so the output doubles as protocol documentation.
 * Exits with a non-zero status when a step fails.
 */

import { randomUUID } from "node:crypto";
import WebSocket from "ws";

const url = process.argv[2] ?? "ws://127.0.0.1:8080";
const TIMEOUT_MS = 10_000;

/** Truncates SDP-ish fields so the transcript stays readable. */
function preview(message) {
  const clone = { ...message };
  if (typeof clone.sdp === "string" && clone.sdp.length > 60) clone.sdp = `${clone.sdp.slice(0, 57)}...`;
  return JSON.stringify(clone);
}

class Peer {
  constructor(name) {
    this.name = name;
    this.received = [];
    this.waiters = [];
    this.socket = null;
  }

  connect() {
    return new Promise((resolve, reject) => {
      const socket = new WebSocket(url);
      this.socket = socket;
      const failFast = (error) => reject(new Error(`${this.name} failed to connect to ${url}: ${error.message}`));

      socket.once("open", () => {
        socket.off("error", failFast);
        this.log("connected");
        resolve();
      });
      socket.once("error", failFast);

      socket.on("message", (data) => {
        const message = JSON.parse(data.toString());
        this.received.push(message);
        this.log(`<- ${preview(message)}`);
        for (let index = this.waiters.length - 1; index >= 0; index -= 1) {
          if (this.waiters[index].type === message.type) {
            const [waiter] = this.waiters.splice(index, 1);
            waiter.resolve(message);
          }
        }
      });

      socket.on("close", (code) => this.log(`closed (${code})`));
    });
  }

  send(message) {
    this.log(`-> ${preview(message)}`);
    this.socket.send(JSON.stringify(message));
  }

  waitFor(type) {
    const buffered = this.received.find((message) => message.type === type);
    if (buffered !== undefined) return Promise.resolve(buffered);

    return new Promise((resolve, reject) => {
      const timer = setTimeout(() => {
        reject(new Error(`${this.name} timed out waiting for "${type}"`));
      }, TIMEOUT_MS);
      this.waiters.push({
        type,
        resolve: (message) => {
          clearTimeout(timer);
          resolve(message);
        }
      });
    });
  }

  log(text) {
    process.stdout.write(`[${this.name.padEnd(6)}] ${text}\n`);
  }

  close() {
    return new Promise((resolve) => {
      if (this.socket === null || this.socket.readyState === WebSocket.CLOSED) {
        resolve();
        return;
      }
      this.socket.once("close", () => resolve());
      this.socket.close();
    });
  }
}

function step(text) {
  process.stdout.write(`\n--- ${text} ---\n`);
}

function assert(condition, message) {
  if (!condition) throw new Error(message);
}

async function main() {
  process.stdout.write(`Signaling server: ${url}\n`);

  const host = new Peer("host");
  const client = new Peer("client");

  step("1. host creates a room");
  await host.connect();
  host.send({ type: "create_room", requestId: randomUUID() });
  const created = await host.waitFor("room_created");
  assert(/^[A-HJ-NP-Z2-9]{6}$/.test(created.roomId), "room id is not in the expected format");
  assert(typeof created.password === "string" && created.password.length === 8, "password is not 8 characters");

  process.stdout.write(
    `\nInvite text:\n  Join my Stardew Connect room\n  Room ID: ${created.roomId}\n  Password: ${created.password}\n`
  );

  step("2. client joins the room");
  await client.connect();
  client.send({
    type: "join_room",
    requestId: randomUUID(),
    roomId: created.roomId,
    password: created.password
  });
  const joined = await client.waitFor("room_joined");
  assert(joined.hostPeerId === created.peerId, "hostPeerId does not match the host peer");

  const peerJoined = await host.waitFor("peer_joined");
  assert(peerJoined.peerId === joined.peerId, "peer_joined announced a different peer");

  step("3. WebRTC offer / answer / ICE relay");
  host.send({
    type: "webrtc_offer",
    roomId: created.roomId,
    fromPeerId: created.peerId,
    targetPeerId: joined.peerId,
    sdp: "v=0\r\no=- 0 0 IN IP4 127.0.0.1\r\ns=-\r\nm=application 9 UDP/DTLS/SCTP webrtc-datachannel\r\n"
  });
  await client.waitFor("webrtc_offer");

  client.send({
    type: "webrtc_answer",
    roomId: created.roomId,
    fromPeerId: joined.peerId,
    targetPeerId: created.peerId,
    sdp: "v=0\r\no=- 0 0 IN IP4 127.0.0.1\r\ns=-\r\nm=application 9 UDP/DTLS/SCTP webrtc-datachannel\r\n"
  });
  await host.waitFor("webrtc_answer");

  client.send({
    type: "ice_candidate",
    roomId: created.roomId,
    fromPeerId: joined.peerId,
    targetPeerId: created.peerId,
    candidate: {
      candidate: "candidate:1 1 udp 2130706431 192.0.2.10 54321 typ host",
      sdpMid: "0",
      sdpMLineIndex: 0
    }
  });
  await host.waitFor("ice_candidate");

  step("4. a spoofed fromPeerId must be rejected");
  client.send({
    type: "webrtc_offer",
    requestId: "spoof-peer",
    roomId: created.roomId,
    fromPeerId: created.peerId,
    targetPeerId: created.peerId,
    sdp: "v=0\r\n"
  });
  const spoofError = await client.waitFor("error");
  assert(spoofError.code === "PEER_ID_MISMATCH", `expected PEER_ID_MISMATCH, got ${spoofError.code}`);

  step("5. client leaves, host closes the room");
  client.send({ type: "leave_room", requestId: randomUUID() });
  const left = await host.waitFor("peer_left");
  assert(left.peerId === joined.peerId, "peer_left announced a different peer");

  host.send({ type: "close_room", requestId: randomUUID() });
  const closed = await host.waitFor("room_closed");
  assert(closed.reason === "host_closed", `expected reason host_closed, got ${closed.reason}`);

  await client.close();
  await host.close();

  process.stdout.write("\nAll signaling checks passed.\n");
}

main().catch((error) => {
  process.stderr.write(`\nFAILED: ${error.message}\n`);
  process.exitCode = 1;
  setTimeout(() => process.exit(1), 250).unref();
});
