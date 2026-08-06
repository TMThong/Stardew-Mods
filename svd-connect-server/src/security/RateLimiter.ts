/**
 * In-memory anti-abuse primitives.
 *
 * All state is per-process. When the server is scaled horizontally these need a
 * shared backend (Redis); the interfaces are intentionally narrow so that swap
 * only touches this file.
 */

/** Fixed-size sliding window counter, keyed by an arbitrary string (usually an IP). */
export class SlidingWindowRateLimiter {
  private readonly hits = new Map<string, number[]>();

  public constructor(
    private readonly limit: number,
    private readonly windowMs: number
  ) {
    if (limit < 1) throw new RangeError("limit must be >= 1");
    if (windowMs < 1) throw new RangeError("windowMs must be >= 1");
  }

  /** Records a hit and returns false when the caller is over the limit. */
  public tryConsume(key: string, now: number = Date.now()): boolean {
    const timestamps = this.prune(key, now);
    if (timestamps.length >= this.limit) return false;
    timestamps.push(now);
    this.hits.set(key, timestamps);
    return true;
  }

  public remaining(key: string, now: number = Date.now()): number {
    return Math.max(0, this.limit - this.prune(key, now).length);
  }

  /** Milliseconds until the caller may retry, or 0 when a slot is free. */
  public retryAfterMs(key: string, now: number = Date.now()): number {
    const timestamps = this.prune(key, now);
    if (timestamps.length < this.limit) return 0;
    const oldest = timestamps[0];
    if (oldest === undefined) return 0;
    return Math.max(0, oldest + this.windowMs - now);
  }

  public reset(key?: string): void {
    if (key === undefined) this.hits.clear();
    else this.hits.delete(key);
  }

  /** Drops empty buckets; call periodically so idle keys do not leak memory. */
  public sweep(now: number = Date.now()): void {
    for (const key of [...this.hits.keys()]) {
      this.prune(key, now);
    }
  }

  public get size(): number {
    return this.hits.size;
  }

  private prune(key: string, now: number): number[] {
    const timestamps = this.hits.get(key);
    if (timestamps === undefined) return [];
    const cutoff = now - this.windowMs;
    const kept = timestamps.filter((timestamp) => timestamp > cutoff);
    if (kept.length === 0) this.hits.delete(key);
    else this.hits.set(key, kept);
    return kept;
  }
}

/** Tracks concurrent connections per key. */
export class ConnectionCounter {
  private readonly counts = new Map<string, number>();

  public constructor(private readonly maxPerKey: number) {
    if (maxPerKey < 1) throw new RangeError("maxPerKey must be >= 1");
  }

  /** Increments unconditionally and reports whether the key is now over quota. */
  public increment(key: string): { count: number; overLimit: boolean } {
    const count = (this.counts.get(key) ?? 0) + 1;
    this.counts.set(key, count);
    return { count, overLimit: count > this.maxPerKey };
  }

  public decrement(key: string): void {
    const count = (this.counts.get(key) ?? 0) - 1;
    if (count <= 0) this.counts.delete(key);
    else this.counts.set(key, count);
  }

  public count(key: string): number {
    return this.counts.get(key) ?? 0;
  }

  public get total(): number {
    let total = 0;
    for (const value of this.counts.values()) total += value;
    return total;
  }

  public reset(): void {
    this.counts.clear();
  }
}

/**
 * Counts failed join attempts and locks the key out for a while once the
 * threshold is reached. Keys are usually `${ip}:${roomId}` so a brute force
 * attempt against one room cannot lock a player out of every room.
 */
export class FailedAttemptTracker {
  private readonly entries = new Map<string, { failures: number; lockedUntil: number; lastFailureAt: number }>();

  public constructor(
    private readonly maxAttempts: number,
    private readonly lockoutMs: number
  ) {
    if (maxAttempts < 1) throw new RangeError("maxAttempts must be >= 1");
    if (lockoutMs < 1) throw new RangeError("lockoutMs must be >= 1");
  }

  public isLocked(key: string, now: number = Date.now()): boolean {
    const entry = this.entries.get(key);
    if (entry === undefined) return false;
    if (entry.lockedUntil > now) return true;
    // Not locked. Only forget the key once the failure streak itself went stale -
    // dropping it eagerly here would reset the counter on every single attempt.
    if (entry.failures === 0 && entry.lastFailureAt + this.lockoutMs <= now) this.entries.delete(key);
    return false;
  }

  public retryAfterMs(key: string, now: number = Date.now()): number {
    const entry = this.entries.get(key);
    if (entry === undefined) return 0;
    return Math.max(0, entry.lockedUntil - now);
  }

  /** Registers a failure and returns true when the key just became locked. */
  public registerFailure(key: string, now: number = Date.now()): boolean {
    const existing = this.entries.get(key);
    const entry = existing === undefined || existing.lastFailureAt + this.lockoutMs <= now
      ? { failures: 0, lockedUntil: 0, lastFailureAt: now }
      : existing;

    entry.failures += 1;
    entry.lastFailureAt = now;

    if (entry.failures >= this.maxAttempts) {
      entry.lockedUntil = now + this.lockoutMs;
      entry.failures = 0;
      this.entries.set(key, entry);
      return true;
    }
    this.entries.set(key, entry);
    return false;
  }

  public clear(key: string): void {
    this.entries.delete(key);
  }

  public sweep(now: number = Date.now()): void {
    for (const [key, entry] of [...this.entries.entries()]) {
      if (entry.lockedUntil <= now && entry.lastFailureAt + this.lockoutMs <= now) this.entries.delete(key);
    }
  }

  public get size(): number {
    return this.entries.size;
  }
}
