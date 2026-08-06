import { describe, expect, it } from "vitest";

import { generatePassword } from "../src/rooms/RoomIdGenerator.js";
import { PasswordService } from "../src/security/PasswordService.js";

const service = new PasswordService({ N: 1024, r: 8, p: 1 });

describe("PasswordService", () => {
  it("never stores the plaintext password", async () => {
    const password = generatePassword();
    const hash = await service.hash(password);

    expect(hash).not.toContain(password);
    expect(hash.startsWith("scrypt$1024$8$1$")).toBe(true);
    expect(hash.split("$")).toHaveLength(6);
  });

  it("verifies the correct password", async () => {
    const password = generatePassword();
    const hash = await service.hash(password);
    await expect(service.verify(password, hash)).resolves.toBe(true);
  });

  it("rejects a wrong password", async () => {
    const hash = await service.hash("K8F2Q7MX");
    await expect(service.verify("K8F2Q7MY", hash)).resolves.toBe(false);
    await expect(service.verify("", hash)).resolves.toBe(false);
    await expect(service.verify("k8f2q7mx", hash)).resolves.toBe(false);
  });

  it("uses a fresh salt for every hash", async () => {
    const first = await service.hash("SAMEPASS");
    const second = await service.hash("SAMEPASS");
    expect(first).not.toBe(second);
    await expect(service.verify("SAMEPASS", first)).resolves.toBe(true);
    await expect(service.verify("SAMEPASS", second)).resolves.toBe(true);
  });

  it("returns false for malformed stored hashes instead of throwing", async () => {
    for (const malformed of ["", "nonsense", "scrypt$1024$8$1$only-five", "bcrypt$1024$8$1$c2FsdA==$a2V5", "scrypt$0$8$1$c2FsdA==$a2V5"]) {
      await expect(service.verify("whatever", malformed)).resolves.toBe(false);
    }
  });

  it("can verify a hash produced with different cost parameters", async () => {
    const cheap = new PasswordService({ N: 1024, r: 8, p: 1 });
    const expensive = new PasswordService({ N: 2048, r: 8, p: 1 });

    const hash = await cheap.hash("PORTABLE");
    await expect(expensive.verify("PORTABLE", hash)).resolves.toBe(true);
  });

  it("burnVerify always resolves false", async () => {
    await expect(service.burnVerify("anything")).resolves.toBe(false);
  });
});
