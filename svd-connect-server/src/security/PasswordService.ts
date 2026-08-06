import { randomBytes, scrypt, timingSafeEqual } from "node:crypto";
import { promisify } from "node:util";

const scryptAsync = promisify(scrypt) as (
  password: string | Buffer,
  salt: string | Buffer,
  keylen: number,
  options: { N: number; r: number; p: number; maxmem: number }
) => Promise<Buffer>;

export interface ScryptParameters {
  /** CPU/memory cost. Must be a power of two. */
  N: number;
  /** Block size. */
  r: number;
  /** Parallelisation. */
  p: number;
}

export const DEFAULT_SCRYPT_PARAMETERS: ScryptParameters = { N: 16_384, r: 8, p: 1 };

const KEY_LENGTH = 32;
const SALT_LENGTH = 16;
const PREFIX = "scrypt";

/**
 * Hashes and verifies room passwords.
 *
 * Room passwords are short-lived and server-generated, but they are still
 * treated as secrets: only the hash is retained in the `Room`, and comparisons
 * use `timingSafeEqual`.
 */
export class PasswordService {
  private readonly parameters: ScryptParameters;
  private readonly maxmem: number;
  /** Used to burn a comparable amount of CPU when a room does not exist. */
  private dummyHash: string | null = null;

  public constructor(parameters: ScryptParameters = DEFAULT_SCRYPT_PARAMETERS) {
    this.parameters = parameters;
    // scrypt needs roughly 128 * N * r bytes; give it generous headroom.
    this.maxmem = Math.max(32 * 1024 * 1024, 256 * parameters.N * parameters.r);
  }

  public async hash(password: string): Promise<string> {
    const salt = randomBytes(SALT_LENGTH);
    const derived = await this.derive(password, salt);
    const { N, r, p } = this.parameters;
    return [PREFIX, N, r, p, salt.toString("base64"), derived.toString("base64")].join("$");
  }

  public async verify(password: string, storedHash: string): Promise<boolean> {
    const parsed = this.parse(storedHash);
    if (parsed === null) return false;

    let derived: Buffer;
    try {
      derived = await scryptAsync(password, parsed.salt, parsed.key.length, {
        N: parsed.N,
        r: parsed.r,
        p: parsed.p,
        maxmem: Math.max(this.maxmem, 256 * parsed.N * parsed.r)
      });
    } catch {
      return false;
    }

    if (derived.length !== parsed.key.length) return false;
    return timingSafeEqual(derived, parsed.key);
  }

  /**
   * Performs a verification against a throwaway hash so "room not found" costs
   * about as much wall clock time as "wrong password". Always resolves false.
   */
  public async burnVerify(password: string): Promise<false> {
    if (this.dummyHash === null) {
      this.dummyHash = await this.hash(randomBytes(16).toString("base64"));
    }
    await this.verify(password, this.dummyHash);
    return false;
  }

  private derive(password: string, salt: Buffer): Promise<Buffer> {
    const { N, r, p } = this.parameters;
    return scryptAsync(password, salt, KEY_LENGTH, { N, r, p, maxmem: this.maxmem });
  }

  private parse(storedHash: string): { N: number; r: number; p: number; salt: Buffer; key: Buffer } | null {
    const segments = storedHash.split("$");
    if (segments.length !== 6) return null;
    const [prefix, rawN, rawR, rawP, rawSalt, rawKey] = segments as [
      string,
      string,
      string,
      string,
      string,
      string
    ];
    if (prefix !== PREFIX) return null;

    const N = Number.parseInt(rawN, 10);
    const r = Number.parseInt(rawR, 10);
    const p = Number.parseInt(rawP, 10);
    if (!Number.isInteger(N) || !Number.isInteger(r) || !Number.isInteger(p)) return null;
    if (N < 2 || (N & (N - 1)) !== 0 || r < 1 || p < 1) return null;

    const salt = Buffer.from(rawSalt, "base64");
    const key = Buffer.from(rawKey, "base64");
    if (salt.length === 0 || key.length === 0) return null;

    return { N, r, p, salt, key };
  }
}
