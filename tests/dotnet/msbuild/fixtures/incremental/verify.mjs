import assert from "node:assert/strict";
import { execFileSync } from "node:child_process";
import { existsSync, readFileSync, statSync, unlinkSync, writeFileSync } from "node:fs";
import { join } from "node:path";
import { setTimeout } from "node:timers/promises";

const input = "message.txt";
const output = join("obj", "message.txt");
const original = readFileSync(input, "utf8");
let invocation = 0;

function build(target = "Build") {
  return execFileSync("dotnet", [
    "msbuild", "Incremental.proj", `-t:${target}`, "-nologo", "-v:minimal",
    `-bl:verification-${process.pid}-${++invocation}.binlog`,
  ], { encoding: "utf8" });
}

try {
  if (existsSync(output)) {
    unlinkSync(output);
  }

  assert.match(build(), /GENERATION_EXECUTED/, "A missing output must trigger generation.");
  assert.equal(readFileSync(output, "utf8").trim(), original.trim());
  const firstWrite = statSync(output).mtimeMs;

  await setTimeout(1100);
  assert.doesNotMatch(build(), /GENERATION_EXECUTED/, "An unchanged build must skip generation.");
  assert.equal(statSync(output).mtimeMs, firstWrite, "An unchanged build must preserve the output timestamp.");

  await setTimeout(1100);
  writeFileSync(input, "changed message\n");
  assert.match(build(), /GENERATION_EXECUTED/, "A changed input must trigger generation.");
  assert.equal(readFileSync(output, "utf8").trim(), "changed message");

  unlinkSync(output);
  assert.match(build(), /GENERATION_EXECUTED/, "A deleted output must be recreated.");
  assert.equal(readFileSync(output, "utf8").trim(), "changed message");

  build("Clean");
  assert.equal(existsSync(output), false, "Clean must remove the generated output.");
  console.log("Incremental generation, changed-input, missing-output, and Clean checks passed.");
} finally {
  writeFileSync(input, original);
}
