# Publish the signed interoperability sample

The sample package is a public development fixture. Its signing seed is checked
into the repository on purpose and must never be used as a production trust key.

## Preflight

From the repository root, run:

```powershell
node tools/dreamskin/build-sample.mjs
node --test tests/dreamskin-fixture.test.mjs
cargo test --manifest-path src-tauri/Cargo.toml --locked
```

The checked-in package must remain deterministic:

- file: `samples/dreamskin/codex-skin-sample-1.0.0.dreamskin`
- bytes: `2041227`
- SHA-256: `7a75fff8086fe6949ef9e37e82c161a8e015a1e00e02181938cd479e9ae41387`
- release tag: `sample-v1`

## Publish

1. Commit and push the importer, sample, tests and workflows together.
2. Run the **Publish signed sample package** workflow with `sample-v1`, or push
   the exact `sample-v1` tag.
3. The workflow creates the Release once. On a rerun it compares every existing
   asset byte-for-byte and refuses to overwrite an immutable asset with different
   content.
4. Confirm the package URL returns HTTP 200 and its downloaded SHA-256 matches the
   catalog entry.
5. In `Codex-Skin-Store/lib/themes.ts`, change only the signed sample package's
   `published` field from `false` to `true`, rerun the Store build/tests, then
   publish the static storefront.

Do not enable the Store link before step 4. Accounts, uploads, moderation and
object storage are deliberately outside this release.
