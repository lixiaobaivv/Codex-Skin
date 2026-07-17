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
  assert.equal(
    (project.match(/ExcludeFromSingleFile="true"/g) ?? []).length,
    6,
    "all mutable theme resource groups must remain beside the single-file executable",
  );
  for (const directory of ["themes", "previews", "backgrounds", "logos", "pets"]) {
    assert.match(project, new RegExp(`\\\\${directory}\\\\`));
  }
  assert.match(readme, /Codex-Skin-win-x64\.exe/);
  assert.doesNotMatch(`${readme}\n${program}\n${ci}\n${release}`, /Codex主题商店\.exe/);
});

test("macOS release status documents the unsigned graphical client", async () => {
  const readme = await readFile(new URL("../README.md", import.meta.url), "utf8");
  assert.match(readme, /macOS Apple Silicon/);
  assert.match(readme, /Avalonia 图形客户端、共享 Core 和 PKG 构建链已接入/);
  assert.match(readme, /尚待真实设备验收/);
  assert.match(readme, /当前 CI 产物保持未签名/);
  assert.match(readme, /代码签名与公证/);
});
