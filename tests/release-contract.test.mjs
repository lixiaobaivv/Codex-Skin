import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import test from "node:test";

test("Windows release publishes only the graphical Setup installer", async () => {
  const [project, ci, build, readme, program, shellIntegration, installer, chineseMessages] = await Promise.all([
    readFile(new URL("../src/CodexThemeStore/CodexThemeStore.csproj", import.meta.url), "utf8"),
    readFile(new URL("../.github/workflows/ci.yml", import.meta.url), "utf8"),
    readFile(new URL("../.github/workflows/build.yml", import.meta.url), "utf8"),
    readFile(new URL("../README.md", import.meta.url), "utf8"),
    readFile(new URL("../src/CodexThemeStore/Program.cs", import.meta.url), "utf8"),
    readFile(new URL("../src/CodexThemeStore/DreamSkinShellIntegration.cs", import.meta.url), "utf8"),
    readFile(new URL("../installer/windows/CodexThemeStore.iss", import.meta.url), "utf8"),
    readFile(new URL("../installer/windows/languages/ChineseSimplified.isl", import.meta.url), "utf8"),
  ]);

  assert.match(project, /<AssemblyName>Codex-Skin<\/AssemblyName>/);
  assert.match(project, /<OutputType>WinExe<\/OutputType>/);
  assert.match(ci, /Codex-Skin-win-x64\/Codex-Skin\.exe/);
  assert.match(build, /Codex-Skin-Setup-win-x64\.exe/);
  assert.match(build, /Codex-Skin-osx-arm64\.pkg|Codex-Skin-\$\{\{ matrix\.rid \}\}\.pkg/);
  assert.match(build, /Codex-Skin-installers-SHA256SUMS\.txt/);
  assert.doesNotMatch(project, /KeepThemeAssetsExternal|themes\\\*\.json|previews\\\*\.png/);
  assert.match(ci, /unexpectedly contains bundled themes/);
  assert.match(ci, /did not load the required official themes/);
  assert.match(ci, /dilraba-star.*enfp-pop.*jackson-sage.*kun-stage/);
  assert.match(project, /<ApplicationIcon>.*Codex-Skin\.ico<\/ApplicationIcon>/);
  assert.match(installer, /SetupIconFile=.*Codex-Skin\.ico/);
  assert.match(installer, /MessagesFile: "\{#SourcePath\}\\languages\\ChineseSimplified\.isl"/);
  assert.match(installer, /Parameters: "protocol register"/);
  assert.match(program, /using var instance = WindowsSingleInstance\.Create\(\)/);
  assert.match(program, /Application\.Run\(window\)/);
  assert.doesNotMatch(shellIntegration, /command\.SetValue\(null, .* import /);
  assert.match(installer, /\[UninstallRun\][\s\S]*Parameters: "protocol unregister"/);
  assert.match(chineseMessages, /Inno Setup version 6\.5\.0\+/);
  assert.doesNotMatch(readme, /Windows (?:x64 )?便携版|Windows portable|Codex-Skin-win-x64\.(?:exe|zip)/);
  assert.doesNotMatch(`${readme}\n${program}\n${ci}\n${build}\n${installer}`, /Codex主题商店\.exe|CodexThemeStore-(?:Setup|osx)|CodexSkin-theme/);
});

test("macOS package registers Codex-Skin URL and document activation", async () => {
  const [plist, project, packageScript, icon] = await Promise.all([
    readFile(new URL("../installer/macos/Info.plist", import.meta.url), "utf8"),
    readFile(new URL("../src/CodexThemeStore.Desktop/CodexThemeStore.Desktop.csproj", import.meta.url), "utf8"),
    readFile(new URL("../installer/macos/build-pkg.sh", import.meta.url), "utf8"),
    readFile(new URL("../installer/macos/AppIcon.png", import.meta.url)),
  ]);
  assert.match(project, /<AssemblyName>Codex-Skin<\/AssemblyName>/);
  assert.match(plist, /<key>CFBundleURLSchemes<\/key>[\s\S]*<string>dreamskin<\/string>/);
  assert.match(plist, /<key>CFBundleTypeExtensions<\/key>[\s\S]*<string>dreamskin<\/string>/);
  assert.match(plist, /<key>CFBundleIconFile<\/key>[\s\S]*<string>Codex-Skin\.icns<\/string>/);
  assert.match(packageScript, /Applications\/Codex-Skin\.app/);
  assert.match(packageScript, /Contents\/MacOS\/Codex-Skin/);
  assert.match(packageScript, /iconutil -c icns/);
  assert.match(packageScript, /installer\/macos\/AppIcon\.png/);
  assert.match(packageScript, /Contents\/Resources\/Codex-Skin\.icns/);
  assert.equal(icon.subarray(1, 4).toString("ascii"), "PNG");
  assert.equal(icon.readUInt32BE(16), 1024);
  assert.equal(icon.readUInt32BE(20), 1024);
  assert.equal(icon[25], 6, "AppIcon.png must be RGBA so macOS keeps transparent corners");
});

test("desktop clients use horizontal categories and Windows single-instance activation", async () => {
  const [windowsUi, macUi, macCode, singleInstance] = await Promise.all([
    readFile(new URL("../src/CodexThemeStore/Program.cs", import.meta.url), "utf8"),
    readFile(new URL("../src/CodexThemeStore.Desktop/MainWindow.axaml", import.meta.url), "utf8"),
    readFile(new URL("../src/CodexThemeStore.Desktop/MainWindow.axaml.cs", import.meta.url), "utf8"),
    readFile(new URL("../src/CodexThemeStore/WindowsSingleInstance.cs", import.meta.url), "utf8"),
  ]);
  assert.doesNotMatch(windowsUi, /_categoryCombo|选择预览，自动适配/);
  assert.match(windowsUi, /SelectCategory\("全部", reload: false\)/);
  assert.doesNotMatch(macUi, /CategoryCombo/);
  assert.match(macUi, /x:Name="CategoryBar"[\s\S]*Tag="全部"[\s\S]*Tag="其他"/);
  assert.match(macCode, /SelectCategory\("全部", applyFilter: false\)/);
  assert.match(singleInstance, /PipeOptions\.CurrentUserOnly/);
  assert.match(singleInstance, /TryForward/);
});

test("macOS release status documents the unsigned graphical client", async () => {
  const readme = await readFile(new URL("../README.md", import.meta.url), "utf8");
  assert.match(readme, /macOS Apple Silicon/);
  assert.match(readme, /PKG 会声明 `dreamskin:\/\/` 和 `\.dreamskin`/);
  assert.match(readme, /仍需更多真实 Apple Silicon 与 Intel 设备验收/);
  assert.match(readme, /macOS PKG 当前未签名、未公证/);
});
