import { describe, expect, it } from "vitest";

import {
  DEFAULT_PASSWORD_LENGTH,
  DEFAULT_ROOM_ID_LENGTH,
  PASSWORD_ALPHABET,
  ROOM_ID_ALPHABET,
  generatePassword,
  generatePeerId,
  generateRoomId,
  generateUniqueRoomId,
  randomString
} from "../src/rooms/RoomIdGenerator.js";
import { ROOM_ID_PATTERN } from "../src/protocol/schemas.js";

describe("RoomIdGenerator", () => {
  it("generates 6 character uppercase room ids", () => {
    for (let index = 0; index < 200; index += 1) {
      const roomId = generateRoomId();
      expect(roomId).toHaveLength(DEFAULT_ROOM_ID_LENGTH);
      expect(roomId).toMatch(ROOM_ID_PATTERN);
    }
  });

  it("never emits the ambiguous characters 0, O, 1, I", () => {
    expect(ROOM_ID_ALPHABET).not.toMatch(/[0O1I]/);

    let sample = "";
    for (let index = 0; index < 500; index += 1) sample += generateRoomId();
    expect(sample).not.toMatch(/[0O1I]/);
  });

  it("generates 8 character passwords from the safe alphabet", () => {
    for (let index = 0; index < 200; index += 1) {
      const password = generatePassword();
      expect(password).toHaveLength(DEFAULT_PASSWORD_LENGTH);
      for (const character of password) {
        expect(PASSWORD_ALPHABET).toContain(character);
      }
    }
  });

  it("produces distinct values (no obvious collisions in a small sample)", () => {
    const ids = new Set<string>();
    for (let index = 0; index < 2000; index += 1) ids.add(generateRoomId());
    // 32^6 = ~1.07e9 possibilities, so 2000 draws should essentially never collide.
    expect(ids.size).toBeGreaterThan(1990);
  });

  it("uses every character of the alphabet roughly uniformly", () => {
    const counts = new Map<string, number>();
    const sample = randomString(32_000, ROOM_ID_ALPHABET);
    for (const character of sample) counts.set(character, (counts.get(character) ?? 0) + 1);

    expect(counts.size).toBe(ROOM_ID_ALPHABET.length);
    const expected = 32_000 / ROOM_ID_ALPHABET.length;
    for (const count of counts.values()) {
      expect(count).toBeGreaterThan(expected * 0.7);
      expect(count).toBeLessThan(expected * 1.3);
    }
  });

  it("rejects nonsensical parameters", () => {
    expect(() => randomString(0, ROOM_ID_ALPHABET)).toThrow(RangeError);
    expect(() => randomString(4, "A")).toThrow(RangeError);
  });

  it("generates peer ids as UUIDs", () => {
    expect(generatePeerId()).toMatch(/^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i);
    expect(generatePeerId()).not.toBe(generatePeerId());
  });

  it("retries until it finds a free room id", () => {
    const taken = new Set<string>();
    const first = generateRoomId();
    taken.add(first);

    let attempts = 0;
    const unique = generateUniqueRoomId((candidate) => {
      attempts += 1;
      return attempts === 1 ? true : taken.has(candidate);
    });

    expect(unique).toMatch(ROOM_ID_PATTERN);
    expect(attempts).toBeGreaterThanOrEqual(2);
  });

  it("throws when the key space is exhausted", () => {
    expect(() => generateUniqueRoomId(() => true, 6, 3)).toThrow(/Unable to allocate/);
  });
});
