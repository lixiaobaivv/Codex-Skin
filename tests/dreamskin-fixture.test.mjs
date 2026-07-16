import assert from "node:assert/strict";
import { createHash, createPublicKey, verify } from "node:crypto";
import { readFile } from "node:fs/promises";
import test from "node:test";

const packageUrl = new URL("../samples/dreamskin/codex-skin-sample-1.0.0.dreamskin", import.meta.url);
const catalogUrl = new URL("../samples/dreamskin/catalog-entry.json", import.meta.url);
const keysUrl = new URL("../samples/dreamskin/public-keys.json", import.meta.url);

test("signed sample package matches the Dream Skin MVP v1 contract", async () => {
  const [archive, catalog, keyring] = await Promise.all([
    readFile(packageUrl),
    readFile(catalogUrl, "utf8").then(JSON.parse),
    readFile(keysUrl, "utf8").then(JSON.parse),
  ]);

  assert.equal(archive.length, catalog.package.size);
  assert.equal(sha256(archive), catalog.package.sha256);
  const installUri = new URL(catalog.installUri);
  assert.equal(installUri.protocol, "dreamskin:");
  assert.equal(installUri.host, "install");
  assert.equal(installUri.searchParams.get("url"), catalog.package.url);
  assert.equal(installUri.searchParams.get("sha256"), catalog.package.sha256);
  assert.equal(installUri.searchParams.get("size"), String(catalog.package.size));
  assert.equal(installUri.searchParams.get("id"), catalog.id);
  assert.equal(installUri.searchParams.get("version"), catalog.version);
  assert.match(catalog.installUri, /url=https%3A%2F%2F/);
  assert.equal(
    catalog.package.url,
    "https://github.com/lixiaobaivv/Codex-Skin/releases/download/sample-v1/codex-skin-sample-1.0.0.dreamskin",
  );

  const entries = readStoredZip(archive);
  assert.deepEqual([...entries.keys()], ["theme.json", "background.jpg", "preview.png"]);
  const manifest = JSON.parse(entries.get("theme.json").toString("utf8"));

  assert.equal(manifest.schemaVersion, 1);
  assert.equal(manifest.packageVersion, 1);
  assert.equal(manifest.image, manifest.assets.background.path);
  assert.deepEqual(manifest.platforms, ["macos", "windows"]);
  assert.equal(entries.get(manifest.assets.background.path).length, manifest.assets.background.bytes);
  assert.equal(entries.get(manifest.assets.preview.path).length, manifest.assets.preview.bytes);
  assert.equal(sha256(entries.get(manifest.assets.background.path)), manifest.assets.background.sha256);
  assert.equal(sha256(entries.get(manifest.assets.preview.path)), manifest.assets.preview.sha256);

  const signingManifest = structuredClone(manifest);
  const signature = Buffer.from(signingManifest.signature.value, "base64url");
  delete signingManifest.signature.value;
  const key = keyring.keys.find((item) => item.keyId === manifest.signature.keyId);
  assert.ok(key, "fixture key must exist in the public keyring");
  const spkiPrefix = Buffer.from("302a300506032b6570032100", "hex");
  const publicKey = createPublicKey({
    key: Buffer.concat([spkiPrefix, Buffer.from(key.publicKey, "base64url")]),
    format: "der",
    type: "spki",
  });
  assert.equal(
    verify(null, Buffer.from(canonicalize(signingManifest), "utf8"), publicKey, signature),
    true,
  );
});

function sha256(value) {
  return createHash("sha256").update(value).digest("hex");
}

function canonicalize(value) {
  if (value === null || typeof value === "boolean" || typeof value === "number" || typeof value === "string") {
    return JSON.stringify(value);
  }
  if (Array.isArray(value)) return `[${value.map(canonicalize).join(",")}]`;
  return `{${Object.keys(value).sort().map((key) => `${JSON.stringify(key)}:${canonicalize(value[key])}`).join(",")}}`;
}

function readStoredZip(archive) {
  const entries = new Map();
  let offset = 0;
  while (offset + 4 <= archive.length && archive.readUInt32LE(offset) === 0x04034b50) {
    const flags = archive.readUInt16LE(offset + 6);
    const compression = archive.readUInt16LE(offset + 8);
    const compressedSize = archive.readUInt32LE(offset + 18);
    const uncompressedSize = archive.readUInt32LE(offset + 22);
    const nameLength = archive.readUInt16LE(offset + 26);
    const extraLength = archive.readUInt16LE(offset + 28);
    assert.equal(flags & 0x0001, 0, "fixture must not be encrypted");
    assert.equal(compression, 0, "fixture generator uses deterministic stored ZIP entries");
    assert.equal(compressedSize, uncompressedSize);
    const nameStart = offset + 30;
    const dataStart = nameStart + nameLength + extraLength;
    const name = archive.subarray(nameStart, nameStart + nameLength).toString("utf8");
    assert.equal(name.includes("/") || name.includes("\\") || name.includes(".."), false);
    assert.equal(entries.has(name), false);
    entries.set(name, archive.subarray(dataStart, dataStart + uncompressedSize));
    offset = dataStart + compressedSize;
  }
  return entries;
}
