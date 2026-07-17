# Codex Theme Store

[简体中文](README.md) | [English](README.en.md)

A desktop theme browser and switcher for Codex on Windows and macOS. It syncs a declarative theme catalog from GitHub, supports categories and download mirrors, and applies the selected appearance through a loopback-only Chrome DevTools Protocol connection.

The tool does not modify the signed Codex package. User projects, tasks, conversations, and account data remain owned by Codex and cannot be replaced by a theme.

## Preview

![Codex Theme Store catalog](docs/images/theme-store-desktop.png)

## Quick Start

1. Download the Setup EXE or PKG for your platform from GitHub Releases.
2. Open Codex Theme Store, browse a category, and select a theme card.
3. Choose **Apply and restart Codex**. Use **Restore default** to remove the theme.

The catalog refreshes automatically at startup. When the network is unavailable, bundled themes remain usable. The toolbar also provides manual refresh, GitHub mirror selection, and repository settings.

## Platforms

- **Windows:** WinForms store with Store/Patched Codex support.
- **macOS:** Avalonia graphical store with app discovery, theme compilation, CDP injection, restart, and rollback.
- **Diagnostic CLI:** available in `src/CodexThemeStore.Cli` for status, refresh, apply, restart, and rollback operations.

macOS discovery checks `/Applications/Codex.app`, `~/Applications/Codex.app`, and compatible `ChatGPT.app` locations. Set `CODEX_APP_PATH` for a custom location.

## Built-in Styles

<table>
  <tr>
    <td width="50%"><img src="previews/dilraba-star.png" alt="Dilraba Star"><br><b>Dilraba Star</b> · violet starlight</td>
    <td width="50%"><img src="previews/jackson-sage.png" alt="Jackson Sage"><br><b>Jackson Sage</b> · sage and paper</td>
  </tr>
  <tr>
    <td width="50%"><img src="previews/kun-stage.png" alt="KUN Stage"><br><b>KUN Stage</b> · black and gold stage</td>
    <td width="50%"><img src="previews/enfp-pop.png" alt="ENFP Pop"><br><b>ENFP Pop</b> · colorful creative pop</td>
  </tr>
</table>

Each style can change backgrounds, surfaces, message bubbles, fixed navigation appearance, composer hints, empty-home quick actions, and an optional hero pet image. Themes cannot define project/task data or ship executable JavaScript.

## Theme Repository

The default repository is currently `lixiaobaivv/Codex-Skin` on `main` and can be changed in the store UI. A production public theme repository will be configured before release.

A repository must contain `theme-repository.json`; individual manifests must validate against `schemas/theme-v1.schema.json`. See [Theme repository standard v1](docs/theme-repository-v1.md) for protocol boundaries and [the authoring guide](docs/theme-authoring.md) for the creation, validation, packaging, and publication workflow.

Synchronization downloads to a temporary directory, validates every manifest and asset path, then atomically replaces the cache. Failed updates keep the last valid catalog or bundled themes.

## Runtime Safety

Codex is launched with:

```text
--remote-debugging-address=127.0.0.1
--remote-debugging-port=9229
```

The injector rejects non-loopback hosts, other ports, `wss`, credentials in WebSocket URLs, and non-Codex page targets. Reapplying a theme removes the previous new-document script before registering the next one.

## Packages and CI

GitHub Actions builds native installer formats rather than ZIP releases:

- `CodexThemeStore-Setup-win-x64.exe`
- `CodexThemeStore-osx-arm64.pkg`
- `CodexThemeStore-osx-x64.pkg`

Pushes and pull requests run tests, schema validation, UI rendering, and installer builds. Tags matching `v*` create a GitHub Release. See [build.yml](.github/workflows/build.yml).

The project owner cannot currently provide commercial Windows signing or Apple notarization. Windows SmartScreen or macOS Gatekeeper may therefore require explicit user confirmation. Artifacts remain reproducible from the public CI workflow and source commit.

## Package Size and .NET

Running on Linux works because the development environment already has the .NET SDK/runtime installed. Cross-platform source does not remove the runtime requirement.

| Mode                     |        Windows |     macOS arm64 | Requirement                                                |
| ------------------------ | -------------: | --------------: | ---------------------------------------------------------- |
| Framework-dependent      | about 4.91 MiB | about 32.78 MiB | .NET 8 Desktop Runtime / Runtime must already be installed |
| Self-contained (default) | about 72.8 MiB |  about 59.4 MiB | no separate .NET installation                              |

Framework-dependent packages are smaller because they move the runtime into a prerequisite. The total download does not disappear. Trimming currently produces reflection warnings in JSON/CDP paths and is not enabled for production. NativeAOT requires separate validation on native Windows and macOS runners.

## Development

Run tests:

```bash
dotnet test tests/CodexThemeStore.Core.Tests/CodexThemeStore.Core.Tests.csproj
```

Render the desktop UI in headless CI:

```bash
dotnet run --project tests/CodexThemeStore.Desktop.Headless -- artifacts/theme-store-ui.png 1120 780
```

Windows local publish:

```powershell
dotnet publish .\src\CodexThemeStore\CodexThemeStore.csproj -p:PublishProfile=win-x64
```

The full architecture and current platform validation status are documented in [cross-platform architecture](docs/cross-platform-architecture.md) and [design compliance](docs/design-compliance.md).
