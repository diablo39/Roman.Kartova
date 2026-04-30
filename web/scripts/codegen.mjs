// @ts-check
import { spawnSync } from "node:child_process";
import { writeFileSync, readFileSync, mkdirSync, existsSync } from "node:fs";
import { resolve } from "node:path";

const baseUrl = process.env.VITE_API_BASE_URL ?? "http://localhost:8080";
const liveUrl = `${baseUrl}/openapi/v1.json`;
const snapshotPath = resolve("openapi-snapshot.json");
const outDir = resolve("src/generated");
const outFile = resolve(outDir, "openapi.ts");

mkdirSync(outDir, { recursive: true });

let spec;
try {
  const res = await fetch(liveUrl);
  if (!res.ok) throw new Error(`HTTP ${res.status}`);
  spec = await res.text();
  writeFileSync(snapshotPath, spec, "utf8");
  console.log(`codegen: fetched live OpenAPI from ${liveUrl}`);
} catch (err) {
  if (!existsSync(snapshotPath)) {
    console.error(
      `codegen: live fetch failed (${err}) and no snapshot at ${snapshotPath}.`
    );
    process.exit(1);
  }
  console.warn(
    `codegen: live fetch failed (${err}); falling back to snapshot ${snapshotPath}.`
  );
  spec = readFileSync(snapshotPath, "utf8");
}

const tmpInput = resolve(outDir, ".live.json");
writeFileSync(tmpInput, spec, "utf8");

// On Windows, `npx.cmd` via spawnSync raises EINVAL in MSYS/Git Bash environments.
// Using `cmd /c npx` avoids the issue without requiring shell:true (which emits a
// deprecation warning and skips argument escaping).
const isWindows = process.platform === "win32";
const result = spawnSync(
  isWindows ? "cmd" : "npx",
  isWindows
    ? ["/c", "npx", "openapi-typescript", tmpInput, "-o", outFile]
    : ["openapi-typescript", tmpInput, "-o", outFile],
  { stdio: "inherit" }
);
if (result.status !== 0) process.exit(result.status ?? 1);
console.log(`codegen: wrote ${outFile}`);
