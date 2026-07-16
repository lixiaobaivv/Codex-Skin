# Signed `.dreamskin` fixture

This directory contains the cross-platform MVP v1 fixture used to test the
Codex-Skin-Store import contract.

Regenerate it with:

```powershell
node tools/dreamskin/build-sample.mjs
```

The fixture private key is intentionally public and must never be trusted for
production publishing. The Windows client trusts it only so the repository can
exercise a complete Ed25519 verification path without storing a secret.

The generated package contains exactly:

```text
theme.json
background.jpg
preview.png
```

`catalog-entry.json` contains the package SHA-256, size and future GitHub
Release URL used by `dreamskin://install`.
