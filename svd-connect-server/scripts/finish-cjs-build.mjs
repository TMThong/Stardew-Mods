#!/usr/bin/env node
/**
 * Finishes the CommonJS build by writing a deploy-ready `dist-cjs/package.json`.
 *
 * Two things make this file necessary:
 *
 *  1. The project is `"type": "module"`, so without an override every `.js` file under it -
 *     including the CommonJS output - would be loaded as ESM and `require()` would fail.
 *     A nested package.json with `"type": "commonjs"` overrides the parent for that subtree.
 *
 *  2. `dist-cjs/` is meant to be uploaded on its own to hosts like cPanel. It therefore has
 *     to carry the runtime `dependencies` too, so the host's "Run NPM Install" installs
 *     `ws` and `zod`. Copying the project's own package.json next to it instead would
 *     collide with this file and reintroduce `"type": "module"`.
 *
 * devDependencies are deliberately omitted: the uploaded folder is already compiled.
 */

import { readFileSync, writeFileSync } from "node:fs";
import { dirname, join } from "node:path";
import { fileURLToPath } from "node:url";

const projectRoot = join(dirname(fileURLToPath(import.meta.url)), "..");
const source = JSON.parse(readFileSync(join(projectRoot, "package.json"), "utf8"));
const target = join(projectRoot, "dist-cjs", "package.json");

const manifest = {
  name: source.name,
  version: source.version,
  description: source.description,
  private: true,
  type: "commonjs",
  main: "index.js",
  engines: source.engines,
  scripts: { start: "node index.js" },
  dependencies: source.dependencies
};

writeFileSync(target, `${JSON.stringify(manifest, null, 2)}\n`, "utf8");

const names = Object.keys(manifest.dependencies ?? {});
process.stdout.write(`Wrote ${target}\n`);
process.stdout.write(`  type: commonjs, dependencies: ${names.length > 0 ? names.join(", ") : "none"}\n`);
