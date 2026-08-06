import { randomBytes, randomUUID } from "node:crypto";

/**
 * Unambiguous uppercase alphabet (32 symbols).
 * Excludes `0`, `O`, `1`, `I` so players can read codes out loud without typos.
 */
export const ROOM_ID_ALPHABET = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";

/** Passwords reuse the same alphabet: 8 chars = 40 bits of entropy. */
export const PASSWORD_ALPHABET = ROOM_ID_ALPHABET;

export const DEFAULT_ROOM_ID_LENGTH = 6;
export const DEFAULT_PASSWORD_LENGTH = 8;

/**
 * Uniformly samples `length` characters from `alphabet` using
 * `crypto.randomBytes` with rejection sampling, so no character is more likely
 * than another regardless of the alphabet size.
 */
export function randomString(length: number, alphabet: string): string {
  if (length <= 0) throw new RangeError("length must be greater than zero");
  if (alphabet.length < 2 || alphabet.length > 256) {
    throw new RangeError("alphabet must contain between 2 and 256 characters");
  }

  const alphabetSize = alphabet.length;
  // Largest multiple of alphabetSize that fits in a byte; values above it are rejected.
  const limit = Math.floor(256 / alphabetSize) * alphabetSize;

  let result = "";
  while (result.length < length) {
    const buffer = randomBytes(Math.max(8, (length - result.length) * 2));
    for (const byte of buffer) {
      if (byte >= limit) continue;
      result += alphabet.charAt(byte % alphabetSize);
      if (result.length === length) break;
    }
  }
  return result;
}

export function generateRoomId(length: number = DEFAULT_ROOM_ID_LENGTH): string {
  return randomString(length, ROOM_ID_ALPHABET);
}

export function generatePassword(length: number = DEFAULT_PASSWORD_LENGTH): string {
  return randomString(length, PASSWORD_ALPHABET);
}

export function generatePeerId(): string {
  return randomUUID();
}

/**
 * Generates a room id that is not already taken.
 * Throws after `maxAttempts` so a saturated key space fails loudly instead of
 * spinning forever.
 */
export function generateUniqueRoomId(
  isTaken: (roomId: string) => boolean,
  length: number = DEFAULT_ROOM_ID_LENGTH,
  maxAttempts = 32
): string {
  for (let attempt = 0; attempt < maxAttempts; attempt += 1) {
    const candidate = generateRoomId(length);
    if (!isTaken(candidate)) return candidate;
  }
  throw new Error(`Unable to allocate a free room id after ${maxAttempts} attempts.`);
}
