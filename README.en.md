# Codex-Skin

[简体中文](README.md) | [English](README.en.md)

[![CI](https://github.com/lixiaobaivv/Codex-Skin/actions/workflows/ci.yml/badge.svg)](https://github.com/lixiaobaivv/Codex-Skin/actions/workflows/ci.yml)
[![Build and package](https://github.com/lixiaobaivv/Codex-Skin/actions/workflows/build.yml/badge.svg)](https://github.com/lixiaobaivv/Codex-Skin/actions/workflows/build.yml)

Codex-Skin is a theme client for Codex Desktop on Windows and macOS. It provides a visual theme catalog, previews, safe application and rollback, plus signed imports from [Codex-Skin-Store](https://lixiaobaivv.github.io/Codex-Skin-Store/).

This first public release focuses on a complete, auditable path from browsing and downloading a theme to validation, installation, application, and rollback.

> Codex-Skin is a community open-source project, not an OpenAI or official Codex product. It does not modify the signed Codex installation or read API keys, project files, tasks, or conversations.

![Codex-Skin theme catalog](docs/images/theme-store-desktop.png)

## Features

- Synchronize the official catalog and filter themes by category.
- Preview a theme before applying it.
- Apply to a running Codex session or restart Codex and apply automatically.
- Restore the default Codex appearance.
- Import through `dreamskin://` links or local `.dreamskin` files.
- Verify SHA-256, Ed25519/RFC8785 signatures, manifests, ZIP paths, and decoded images.
- Fall back across GitHub, GH Proxy, and GHFast while preserving the last valid local cache.
- Forward web and file activations to one application instance.

## Download

Download the package for your platform from the [latest release](https://github.com/lixiaobaivv/Codex-Skin/releases/latest):

| Platform | File | Description |
| --- | --- | --- |
| Windows x64 | `Codex-Skin-Setup-win-x64.exe` | Graphical installer with web protocol and file association |
| macOS Apple Silicon | `Codex-Skin-osx-arm64.pkg` | Apple Silicon Macs |
| macOS Intel | `Codex-Skin-osx-x64.pkg` | Intel Macs |

Windows is distributed through the Setup installer. It uses the system WebView2 runtime; macOS uses WKWebView. No separate application runtime is required.

Current packages do not have commercial code signing. Windows SmartScreen or macOS Gatekeeper may require explicit approval. Before running a package, download `Codex-Skin-installers-SHA256SUMS.txt` from the same release and verify its hash.

Windows PowerShell:

```powershell
Get-FileHash .\Codex-Skin-Setup-win-x64.exe -Algorithm SHA256
```

macOS:

```bash
shasum -a 256 Codex-Skin-osx-arm64.pkg
```

## Quick Start

### Windows

1. Install `Codex-Skin-Setup-win-x64.exe`.
2. Open Codex-Skin from the Start menu.
3. On first use, choose **Refresh** and wait for the official catalog to synchronize.
4. Select a category and theme card, then choose **Apply and restart Codex**.
5. Choose **Restore default** when you want to remove the theme.

Setup registers `dreamskin://` and `.dreamskin` for the current user. Uninstall removes only the associations created by Codex-Skin.

### macOS

1. Install `Codex-Skin-osx-arm64.pkg` or `Codex-Skin-osx-x64.pkg` for your processor.
2. Open `Codex-Skin.app` from Applications.
3. If Gatekeeper blocks the package, verify its SHA-256 first, then use **System Settings → Privacy & Security → Open Anyway**.
4. Synchronize the catalog, select a theme, and choose **Apply and restart Codex**.

The PKG declares the `dreamskin://` URL scheme and `.dreamskin` document type. Open Codex-Skin at least once after installation so LaunchServices can complete registration.

macOS PKGs are currently unsigned and not notarized. CI builds both Apple Silicon and Intel targets; matching physical hardware should still be used to smoke-test launch and system activation before release.

## Apply And Restore Themes

The command bar contains three actions:

- **Apply theme** injects the selected theme into a Codex instance that is already running in theme mode, without restarting it.
- **Apply and restart Codex** stops Codex, starts it with a loopback-only CDP endpoint, waits for the connection, and applies the theme. This is the recommended action.
- **Restore default** removes the active injection and persistent new-document script.

Codex-Skin connects only to `127.0.0.1:9229` or an equivalent loopback target. It never exposes the debugging endpoint to the local network and never modifies the Codex installation directory.

## Import Themes

### One-Click Web Import

1. Open [Codex-Skin-Store](https://lixiaobaivv.github.io/Codex-Skin-Store/) and select a theme.
2. Choose one-click import; the browser opens a `dreamskin://` link.
3. Codex-Skin shows the source and theme hint and asks before downloading.
4. The client downloads and verifies size, SHA-256, signature, manifest, and images.
5. A valid package is atomically installed in the local library but is not applied automatically.
6. Select it in the catalog, then choose whether to apply immediately or restart Codex first.

A website cannot silently install or apply a theme. If Codex-Skin is already open, the activation is forwarded to the existing window and brings it to the foreground.

### Local Files

Double-click a `.dreamskin` file or open it with Codex-Skin. The client shows the local path, requests confirmation, and performs the same signature and asset validation used for web imports.

Several versions of one theme may be installed; the catalog selects the highest semantic version. Conflicting content for an existing ID and version is rejected.

## Catalog And Download Routes

The desktop catalog comes from the public [lixiaobaivv/Codex-Skin-Store](https://github.com/lixiaobaivv/Codex-Skin-Store) repository. Available routes are:

- direct GitHub;
- GH Proxy;
- GHFast.

Synchronization tries the selected route first and then the other built-in routes. A new catalog is downloaded into a temporary location and replaces the cache only after complete validation. If every route fails, the last valid catalog remains available.

Installers do not bundle online themes, so first use requires a catalog synchronization. Third-party mirrors may be unavailable or stale; downloaded content still receives the same validation.

## Theme Scope

Themes are declarative data and may configure:

- backgrounds, surfaces, text, borders, and accent colors;
- the visual appearance of the native sidebar and top region;
- user and assistant message bubbles;
- the home hero, logo, labels, copy, and prompt cards;
- composer appearance and placeholder copy;
- optional background, logo, and pet images.

Themes cannot carry arbitrary CSS, JavaScript, HTML, SVG, or executable files. They cannot replace project, task, conversation, progress, or account data. Prompt cards only insert predefined text into the real composer.

## Security Model

- The signed Codex application is never modified.
- CDP accepts only loopback targets on the fixed debugging port.
- Catalogs, manifests, and images are treated as untrusted input.
- `.dreamskin` bounds URI length, downloads, redirects, file count, expanded size, paths, formats, dimensions, and pixel count.
- Deep links reject unknown or duplicate fields, private addresses, and unsafe redirects.
- SHA-256 protects transport integrity; Ed25519/RFC8785 verifies publisher signatures.
- Download/installation and application require separate user confirmation.
- Temporary locations and atomic replacement preserve the last valid cache or installation after failure.

See [cross-platform architecture](docs/cross-platform-architecture.md), [desktop catalog v1](docs/theme-repository-v1.md), and [DreamSkin compatibility](docs/dreamskin-compatibility.md) for protocol details.

## Troubleshooting

### No themes appear on first launch

Installers do not contain theme assets. Stay online, select a route, and choose **Refresh**. Theme cards appear after synchronization succeeds.

### “Apply theme” says Codex is not running in theme mode

Use **Apply and restart Codex** the first time. It starts Codex with the loopback CDP endpoint; later theme switches can use **Apply theme** without a restart.

### One-click import does not open the client

- On Windows, rerun Setup to repair the current-user protocol association.
- On macOS, confirm that the current PKG is installed and Codex-Skin has been opened at least once.
- As a fallback, download the `.dreamskin` file and open it manually.

### Catalog synchronization fails

Switch between GitHub, GH Proxy, and GHFast and retry. Failed refreshes do not delete the existing valid cache.

### The operating system warns about an unknown publisher

Current packages are not commercially signed or Apple-notarized. Download only from this repository's releases and verify SHA-256 before running them.

## Architecture

Codex-Skin is built with Tauri 2, Rust, and TypeScript/Vite:

- **Interface** (`src/`) owns catalog browsing, filtering, previews, confirmation flows, and status feedback.
- **Application bridge** (`src-tauri/src/lib.rs`) owns Tauri commands/events, single-instance behavior, deep links, and file activation.
- **Domain core** (`repository.rs`, `catalog.rs`, `compiler.rs`, `dreamskin.rs`, and `protocol.rs`) owns synchronization, validation, version selection, compilation, signatures, and safe downloads.
- **Runtime integration** (`cdp.rs` and `platform.rs`) owns Codex discovery and launch, loopback CDP injection, verification, and rollback.
- **Tooling and release** (`authoring.rs`, `src-tauri/src/bin/`, and `installer/`) owns catalog authoring, package diagnostics, and cross-platform installers.

The catalog flow is: download into a temporary directory → validate the catalog, manifests, and images → atomically replace the cache → compile the declarative theme → apply it through loopback CDP. `.dreamskin` follows a separate signed-import path into an immutable local version library.

Windows and macOS share the interface and domain logic. Platform-specific behavior is limited to Codex discovery, process launch, window activation, and installer associations.

## Repository Layout

```text
src/                    TypeScript/Vite interface
src-tauri/src/          Rust application core and platform adapters
src-tauri/src/bin/      catalog-tool and dreamskin-verify
installer/windows/      Windows Inno Setup
installer/macos/        macOS app and PKG packaging
tools/dreamskin/        signed theme package tooling
schemas/                desktop catalog schemas
samples/dreamskin/      reproducible signed fixture
tests/                  contract and release configuration tests
docs/                   architecture, protocol, and authoring guides
```

## Development

Development requires Node.js 22, stable Rust, and the Tauri prerequisites for the host platform. Windows requires MSVC Build Tools and WebView2; macOS requires Xcode Command Line Tools.

```bash
npm ci
npm run tauri -- dev
```

Run the complete verification suite before submitting a change:

```bash
npm run build
node --test tests/dreamskin-catalog-fixtures.test.mjs tests/dreamskin-fixture.test.mjs tests/release-contract.test.mjs
cargo fmt --manifest-path src-tauri/Cargo.toml -- --check
cargo clippy --manifest-path src-tauri/Cargo.toml --all-targets --locked -- -D warnings
cargo test --manifest-path src-tauri/Cargo.toml --locked
```

Build an optimized application for the current platform:

```bash
npm run tauri -- build --no-bundle
```

The [Build and package](https://github.com/lixiaobaivv/Codex-Skin/actions/workflows/build.yml) workflow produces the Windows Setup executable and both macOS PKGs.

## Theme Authoring

The desktop catalog and web-distributed `.dreamskin` packages are separate protocols:

- Read [theme authoring](docs/theme-authoring.md) to create or maintain a desktop catalog.
- Read [signed sample publishing](docs/publish-signed-sample.md) for signed fixtures and release packages.
- Submit public themes through the [Codex-Skin-Store submission guide](https://github.com/lixiaobaivv/Codex-Skin-Store/blob/main/docs/theme-submission.md).

The Rust `catalog-tool` can create an index, validate a repository, and pack a catalog. `dreamskin-verify` validates signed packages for Windows or macOS.

## Feedback And Contributions

Report client problems through [Codex-Skin Issues](https://github.com/lixiaobaivv/Codex-Skin/issues). Include the operating system, Codex version, Codex-Skin version, reproduction steps, and error message. Code contributions should pass every local check listed above.
