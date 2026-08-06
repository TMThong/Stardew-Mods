import { describe, expect, it } from "vitest";

import { ConnectionCounter, FailedAttemptTracker, SlidingWindowRateLimiter } from "../src/security/RateLimiter.js";

describe("SlidingWindowRateLimiter", () => {
  it("allows up to the limit inside the window", () => {
    const limiter = new SlidingWindowRateLimiter(5, 10 * 60_000);
    const now = 1_000_000;

    for (let index = 0; index < 5; index += 1) {
      expect(limiter.tryConsume("1.2.3.4", now + index)).toBe(true);
    }
    expect(limiter.tryConsume("1.2.3.4", now + 5)).toBe(false);
    expect(limiter.remaining("1.2.3.4", now + 5)).toBe(0);
  });

  it("keeps buckets separate per key", () => {
    const limiter = new SlidingWindowRateLimiter(1, 60_000);
    expect(limiter.tryConsume("a", 0)).toBe(true);
    expect(limiter.tryConsume("b", 0)).toBe(true);
    expect(limiter.tryConsume("a", 0)).toBe(false);
  });

  it("frees slots once the window slides past", () => {
    const limiter = new SlidingWindowRateLimiter(2, 1000);
    expect(limiter.tryConsume("ip", 0)).toBe(true);
    expect(limiter.tryConsume("ip", 100)).toBe(true);
    expect(limiter.tryConsume("ip", 200)).toBe(false);
    expect(limiter.retryAfterMs("ip", 200)).toBe(800);
    expect(limiter.tryConsume("ip", 1101)).toBe(true);
  });

  it("sweeps idle keys", () => {
    const limiter = new SlidingWindowRateLimiter(2, 1000);
    limiter.tryConsume("ip", 0);
    expect(limiter.size).toBe(1);
    limiter.sweep(5000);
    expect(limiter.size).toBe(0);
  });
});

describe("ConnectionCounter", () => {
  it("reports when a key exceeds its quota", () => {
    const counter = new ConnectionCounter(2);
    expect(counter.increment("ip").overLimit).toBe(false);
    expect(counter.increment("ip").overLimit).toBe(false);
    expect(counter.increment("ip").overLimit).toBe(true);
    expect(counter.count("ip")).toBe(3);
  });

  it("releases slots on decrement", () => {
    const counter = new ConnectionCounter(1);
    counter.increment("ip");
    counter.decrement("ip");
    expect(counter.count("ip")).toBe(0);
    expect(counter.increment("ip").overLimit).toBe(false);
    expect(counter.total).toBe(1);
  });

  it("never goes negative", () => {
    const counter = new ConnectionCounter(1);
    counter.decrement("ghost");
    expect(counter.count("ghost")).toBe(0);
  });
});

describe("FailedAttemptTracker", () => {
  it("locks a key after the configured number of failures", () => {
    const tracker = new FailedAttemptTracker(3, 60_000);
    expect(tracker.registerFailure("ip|ROOM", 0)).toBe(false);
    expect(tracker.registerFailure("ip|ROOM", 10)).toBe(false);
    expect(tracker.registerFailure("ip|ROOM", 20)).toBe(true);

    expect(tracker.isLocked("ip|ROOM", 30)).toBe(true);
    expect(tracker.retryAfterMs("ip|ROOM", 20)).toBe(60_000);
  });

  it("expires the lockout", () => {
    const tracker = new FailedAttemptTracker(1, 1000);
    tracker.registerFailure("ip|ROOM", 0);
    expect(tracker.isLocked("ip|ROOM", 500)).toBe(true);
    expect(tracker.isLocked("ip|ROOM", 1500)).toBe(false);
  });

  it("keeps rooms independent so one bad room cannot lock every room", () => {
    const tracker = new FailedAttemptTracker(1, 1000);
    tracker.registerFailure("ip|AAAAAA", 0);
    expect(tracker.isLocked("ip|AAAAAA", 10)).toBe(true);
    expect(tracker.isLocked("ip|BBBBBB", 10)).toBe(false);
  });

  it("forgets stale failure counts", () => {
    const tracker = new FailedAttemptTracker(3, 1000);
    tracker.registerFailure("ip|ROOM", 0);
    tracker.registerFailure("ip|ROOM", 100);
    // A long pause resets the counter, so the next two failures do not lock.
    expect(tracker.registerFailure("ip|ROOM", 10_000)).toBe(false);
    expect(tracker.registerFailure("ip|ROOM", 10_100)).toBe(false);
    expect(tracker.registerFailure("ip|ROOM", 10_200)).toBe(true);
  });

  it("clears on success", () => {
    const tracker = new FailedAttemptTracker(2, 1000);
    tracker.registerFailure("ip|ROOM", 0);
    tracker.clear("ip|ROOM");
    expect(tracker.registerFailure("ip|ROOM", 10)).toBe(false);
    expect(tracker.size).toBe(1);
  });

  it("sweeps expired entries", () => {
    const tracker = new FailedAttemptTracker(1, 1000);
    tracker.registerFailure("ip|ROOM", 0);
    tracker.sweep(5000);
    expect(tracker.size).toBe(0);
  });
});
