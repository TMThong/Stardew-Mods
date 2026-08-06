import { beforeEach, describe, expect, it } from "vitest";
import type { WebSocket } from "ws";

import { ErrorCodes, ProtocolError } from "../src/protocol/errors.js";
import { ROOM_ID_PATTERN } from "../src/protocol/schemas.js";
import { RoomManager } from "../src/rooms/RoomManager.js";
import { PasswordService } from "../src/security/PasswordService.js";
import { createSilentLogger } from "../src/utilities/logger.js";

const ROOM_TTL_MS = 30 * 60_000;

/** RoomManager never touches the socket, so a bare object is enough here. */
function fakeSocket(name: string): WebSocket {
  return { __name: name } as unknown as WebSocket;
}

async function expectProtocolError(promise: Promise<unknown>, code: string): Promise<ProtocolError> {
  try {
    await promise;
  } catch (error) {
    expect(error).toBeInstanceOf(ProtocolError);
    const protocolError = error as ProtocolError;
    expect(protocolError.code).toBe(code);
    return protocolError;
  }
  throw new Error(`Expected the promise to reject with ${code}`);
}

describe("RoomManager", () => {
  let clock = 1_700_000_000_000;
  let manager: RoomManager;

  beforeEach(() => {
    clock = 1_700_000_000_000;
    manager = new RoomManager({
      passwordService: new PasswordService({ N: 1024, r: 8, p: 1 }),
      logger: createSilentLogger(),
      maxPlayersPerRoom: 4,
      roomTtlMs: ROOM_TTL_MS,
      now: () => clock
    });
  });

  describe("createRoom", () => {
    it("creates a room with a formatted id, a password and a host peer", async () => {
      const { room, peer, password } = await manager.createRoom({ socket: fakeSocket("host"), remoteAddress: "1.1.1.1" });

      expect(room.roomId).toMatch(ROOM_ID_PATTERN);
      expect(password).toHaveLength(8);
      expect(room.passwordHash).not.toContain(password);
      expect(room.hostPeerId).toBe(peer.peerId);
      expect(peer.role).toBe("host");
      expect(peer.remoteAddress).toBe("1.1.1.1");
      expect(room.peers.size).toBe(1);
      expect(room.createdAt).toBe(clock);
      expect(room.expiresAt).toBe(clock + ROOM_TTL_MS);
      expect(room.maxPlayers).toBe(4);
      expect(manager.getRoom(room.roomId)).toBe(room);
      expect(manager.stats()).toEqual({ rooms: 1, peers: 1 });
    });

    it("clamps a requested maxPlayers to the server limit", async () => {
      const big = await manager.createRoom({ socket: fakeSocket("a"), maxPlayers: 99 });
      expect(big.room.maxPlayers).toBe(4);

      const small = await manager.createRoom({ socket: fakeSocket("b"), maxPlayers: 2 });
      expect(small.room.maxPlayers).toBe(2);
    });

    it("looks rooms up case-insensitively", async () => {
      const { room } = await manager.createRoom({ socket: fakeSocket("host") });
      expect(manager.getRoom(room.roomId.toLowerCase())).toBe(room);
    });
  });

  describe("joinRoom", () => {
    it("admits a client that supplies the right password", async () => {
      const created = await manager.createRoom({ socket: fakeSocket("host") });
      const joined = await manager.joinRoom({
        roomId: created.room.roomId,
        password: created.password,
        socket: fakeSocket("client"),
        remoteAddress: "2.2.2.2"
      });

      expect(joined.room).toBe(created.room);
      expect(joined.peer.role).toBe("client");
      expect(joined.peer.peerId).not.toBe(created.peer.peerId);
      expect(joined.host.peerId).toBe(created.room.hostPeerId);
      expect(created.room.peers.size).toBe(2);
      expect(manager.getRoomOfPeer(joined.peer.peerId)).toBe(created.room);
    });

    it("accepts a lowercase room id", async () => {
      const created = await manager.createRoom({ socket: fakeSocket("host") });
      const joined = await manager.joinRoom({
        roomId: created.room.roomId.toLowerCase(),
        password: created.password,
        socket: fakeSocket("client")
      });
      expect(joined.room.roomId).toBe(created.room.roomId);
    });

    it("rejects a wrong password with the generic credentials error", async () => {
      const created = await manager.createRoom({ socket: fakeSocket("host") });
      await expectProtocolError(
        manager.joinRoom({
          roomId: created.room.roomId,
          password: `${created.password}X`,
          socket: fakeSocket("client")
        }),
        ErrorCodes.INVALID_ROOM_CREDENTIALS
      );
      expect(created.room.peers.size).toBe(1);
    });

    it("uses the same error for an unknown room so rooms cannot be enumerated", async () => {
      await expectProtocolError(
        manager.joinRoom({ roomId: "ZZZZZZ", password: "WHATEVER", socket: fakeSocket("client") }),
        ErrorCodes.INVALID_ROOM_CREDENTIALS
      );
    });

    it("uses the same error for an expired room", async () => {
      const created = await manager.createRoom({ socket: fakeSocket("host") });
      clock += ROOM_TTL_MS + 1;

      await expectProtocolError(
        manager.joinRoom({
          roomId: created.room.roomId,
          password: created.password,
          socket: fakeSocket("client")
        }),
        ErrorCodes.INVALID_ROOM_CREDENTIALS
      );
    });

    it("rejects a join when the room is full", async () => {
      const created = await manager.createRoom({ socket: fakeSocket("host"), maxPlayers: 2 });
      await manager.joinRoom({
        roomId: created.room.roomId,
        password: created.password,
        socket: fakeSocket("client-1")
      });

      await expectProtocolError(
        manager.joinRoom({
          roomId: created.room.roomId,
          password: created.password,
          socket: fakeSocket("client-2")
        }),
        ErrorCodes.ROOM_FULL
      );
      expect(created.room.peers.size).toBe(2);
    });
  });

  describe("peer removal", () => {
    it("destroys the room and reports orphans when the host disconnects", async () => {
      const created = await manager.createRoom({ socket: fakeSocket("host") });
      const clientA = await manager.joinRoom({
        roomId: created.room.roomId,
        password: created.password,
        socket: fakeSocket("client-a")
      });
      const clientB = await manager.joinRoom({
        roomId: created.room.roomId,
        password: created.password,
        socket: fakeSocket("client-b")
      });

      const result = manager.removePeer(created.peer.peerId);

      expect(result?.roomRemoved).toBe(true);
      expect(result?.notify.map((peer) => peer.peerId).sort()).toEqual(
        [clientA.peer.peerId, clientB.peer.peerId].sort()
      );
      expect(manager.getRoom(created.room.roomId)).toBeUndefined();
      expect(manager.getRoomOfPeer(clientA.peer.peerId)).toBeUndefined();
      expect(manager.stats()).toEqual({ rooms: 0, peers: 0 });
    });

    it("keeps the room alive and notifies the host when a client disconnects", async () => {
      const created = await manager.createRoom({ socket: fakeSocket("host") });
      const client = await manager.joinRoom({
        roomId: created.room.roomId,
        password: created.password,
        socket: fakeSocket("client")
      });

      clock += 5 * 60_000;
      const result = manager.removePeer(client.peer.peerId);

      expect(result?.roomRemoved).toBe(false);
      expect(result?.notify).toHaveLength(1);
      expect(result?.notify[0]?.peerId).toBe(created.peer.peerId);
      expect(manager.getRoom(created.room.roomId)).toBe(created.room);
      expect(created.room.peers.size).toBe(1);
      // The waiting deadline is re-armed once the room is empty again.
      expect(created.room.expiresAt).toBe(clock + ROOM_TTL_MS);
    });

    it("ignores unknown peer ids", () => {
      expect(manager.removePeer("00000000-0000-4000-8000-000000000000")).toBeUndefined();
    });
  });

  describe("expiry", () => {
    it("does not expire a room while a client is connected", async () => {
      const created = await manager.createRoom({ socket: fakeSocket("host") });
      await manager.joinRoom({
        roomId: created.room.roomId,
        password: created.password,
        socket: fakeSocket("client")
      });

      clock += ROOM_TTL_MS * 10;
      expect(manager.sweepExpired(clock)).toHaveLength(0);
      expect(manager.getRoom(created.room.roomId)).toBe(created.room);
    });

    it("expires a room that never received a client", async () => {
      const created = await manager.createRoom({ socket: fakeSocket("host") });

      expect(manager.sweepExpired(clock + ROOM_TTL_MS - 1)).toHaveLength(0);

      const swept = manager.sweepExpired(clock + ROOM_TTL_MS);
      expect(swept).toHaveLength(1);
      expect(swept[0]?.room.roomId).toBe(created.room.roomId);
      expect(swept[0]?.peers).toHaveLength(1);
      expect(manager.getRoom(created.room.roomId)).toBeUndefined();
      expect(manager.stats()).toEqual({ rooms: 0, peers: 0 });
    });
  });

  describe("closeRoom / clear", () => {
    it("closes a room and returns every member", async () => {
      const created = await manager.createRoom({ socket: fakeSocket("host") });
      await manager.joinRoom({
        roomId: created.room.roomId,
        password: created.password,
        socket: fakeSocket("client")
      });

      const closed = manager.closeRoom(created.room.roomId);
      expect(closed?.peers).toHaveLength(2);
      expect(manager.getRoom(created.room.roomId)).toBeUndefined();
      expect(manager.stats()).toEqual({ rooms: 0, peers: 0 });
      expect(manager.closeRoom(created.room.roomId)).toBeUndefined();
    });

    it("clears every room on shutdown", async () => {
      await manager.createRoom({ socket: fakeSocket("a") });
      await manager.createRoom({ socket: fakeSocket("b") });

      expect(manager.clear()).toHaveLength(2);
      expect(manager.stats()).toEqual({ rooms: 0, peers: 0 });
    });
  });
});
