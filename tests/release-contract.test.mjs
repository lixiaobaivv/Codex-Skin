import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import test from "node:test";

test("Windows client release uses stable English artifact names", async () => {
  const [project, ci, release, readme, program] = await Promise.all([
    readFile(new URL("../src/CodexThemeStore/CodexThemeStore.csproj", import.meta.url), "utf8"),
    readFile(new URL("../.github/workflows/ci.yml", import.meta.url), "utf8"),
    readFile(new URL("../.github/workflows/release-client.yml", import.meta.url), "utf8"),
    readFile(new URL("../README.md", import.meta.url), "utf8"),
    readFile(new URL("../src/CodexThemeStore/Program.cs", import.meta.url), "utf8"),
  ]);

  assert.match(project, /<AssemblyName>Codex-Skin<\/AssemblyName>/);
  assert.match(ci, /Codex-Skin-win-x64\/Codex-Skin\.exe/);
  assert.match(release, /Codex-Skin-win-x64\.exe/);
  assert.match(release, /Codex-Skin-win-x64\.zip/);
  assert.match(release, /SHA256SUMS\.txt/);
  assert.match(release, /PublishSingleFile=true/);
  assert.match(release, /IncludeAllContentForSelfExtract=true/);
  assert.match(readme, /Codex-Skin-win-x64\.exe/);
  assert.doesNotMatch(`${readme}\n${program}\n${ci}\n${release}`, /Codex主题商店\.exe/);
});

test("macOS release status is explicit and does not claim a nonexistent binary", async () => {
  const readme = await readFile(new URL("../README.md", import.meta.url), "utf8");
  assert.match(readme, /macOS Apple Silicon/);
  assert.match(readme, /尚未包含可独立构建的 macOS 客户端源码/);
  assert.match(readme, /代码签名与公证/);
});
