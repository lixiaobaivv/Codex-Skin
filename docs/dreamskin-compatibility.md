# Codex-Skin-Store import compatibility

This repository implements the shared Windows and macOS side of the Codex-Skin-Store MVP v1
contract. The canonical protocol and JSON Schema remain owned by the store
repository:

- <https://github.com/lixiaobaivv/Codex-Skin-Store/blob/main/spec/import-protocol.md>
- <https://github.com/lixiaobaivv/Codex-Skin-Store/blob/main/spec/theme-package.schema.json>

## Frozen interoperability slice

The first client slice intentionally implements local files before URL scheme
registration:

1. Accept one absolute `.dreamskin` file path.
2. Calculate and display the package SHA-256.
3. Require exactly `theme.json`, one background image and one preview image.
4. Reject traversal, nested paths, links, duplicate names and size overruns.
5. Enforce the closed MVP v1 manifest shape and current-platform compatibility range.
6. Remove `signature.value`, canonicalize the remaining JSON and verify its
   Ed25519 signature against the application keyring.
7. Verify asset sizes, hashes, media signatures, decoded dimensions and pixel
   limits.
8. Commit the validated payload to an immutable `<id>/<version>` directory.
9. Keep installation and application as separate user choices.

The same fixture and validation order are used by Windows and macOS. Platform
code differs only around URL/file activation, confirmation UI and the final
theme activation bridge.

## Runtime mapping

Imported store manifests use a smaller cross-platform color model than the
official desktop catalog themes. The shared renderer maps it as follows:

| Store v1 | Shared runtime |
| --- | --- |
| `id` | `codeThemeId` |
| `name` | `displayName` |
| `colors.background` / `colors.panel` | base surface |
| `colors.accent` | accent |
| `colors.text` | foreground ink |
| `colors.highlight` | positive/diff accent |
| `colors.accentAlt` | negative/diff accent |
| `image` | background image |
| `tagline`, `brandSubtitle`, `statusText`, `quote` | home copy |

Fields not present in the store schema, such as custom fonts, logos and quick
actions, use safe defaults. A future schema version must explicitly whitelist
such extensions before remote packages can use them.

## Development fixture trust

`codex-skin.sample.2026-01` is a public, repository-only fixture key. Its seed
is intentionally committed in the generator so Windows and macOS implementations
can reproduce deterministic tests. Production builds must replace this with a
maintainer-controlled or publisher trust registry and must not treat the sample
key as production authority.

## Deep-link slice

The shared implementation now also includes:

1. strict `dreamskin://install` parsing with duplicate and unknown field rejection;
2. a 4096-byte URI limit and exactly-once UTF-8 percent decoding;
3. bounded HTTPS download with public-address DNS enforcement;
4. manual redirect handling with a maximum of three HTTPS redirects;
5. package size and SHA-256 checks before package parsing and installation;
6. current-user Windows association commands and macOS Avalonia activation handling;
7. separate confirmation prompts for download/install and immediate application.

Windows Setup registers the protocol during installation. macOS PKG declares
the URL scheme and document type; real-device LaunchServices validation remains
required. The website continues to offer a manual `.dreamskin` fallback.
