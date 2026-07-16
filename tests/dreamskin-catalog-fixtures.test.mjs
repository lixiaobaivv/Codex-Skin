import assert from "node:assert/strict";
import { createHash, createPublicKey, verify } from "node:crypto";
import { readdir, readFile } from "node:fs/promises";
import test from "node:test";

const catalogDirectory = new URL("../samples/dreamskin/catalog/", import.meta.url);
const keyring = JSON.parse(await readFile(new URL("../samples/dreamskin/public-keys.json", import.meta.url), "utf8"));

test("all catalog sample packages have valid metadata, assets, and signatures", async () => {
  const catalog = JSON.parse(await readFile(new URL("catalog-entries.json", catalogDirectory), "utf8"));
  const files = (await readdir(catalogDirectory)).filter((name) => name.endsWith(".dreamskin")).sort();
  assert.equal(catalog.length, 8);
  assert.equal(files.length, 8);
  assert.equal(new Set(catalog.map((entry) => entry.slug)).size, 8);

  for (const entry of catalog) {
    const packageName = new URL(entry.package.url).pathname.split("/").at(-1);
    const archive = await readFile(new URL(packageName, catalogDirectory));
    assert.equal(archive.length, entry.package.size);
    assert.equal(sha256(archive), entry.package.sha256);
    assert.match(entry.package.url, /\/releases\/download\/catalog-v1\//);
    assert.equal(entry.package.published, true);

    const entries = readStoredZip(archive);
    assert.deepEqual([...entries.keys()], ["theme.json", "background.png", "preview.png"]);
    const manifest = JSON.parse(entries.get("theme.json").toString("utf8"));
    assert.equal(manifest.id, entry.package.id);
    assert.equal(manifest.version, entry.package.version);
    assert.equal(manifest.image, "background.png");
    for (const asset of Object.values(manifest.assets)) {
      assert.equal(entries.get(asset.path).length, asset.bytes);
      assert.equal(sha256(entries.get(asset.path)), asset.sha256);
      assert.equal(entries.get(asset.path).subarray(0, 8).toString("hex"), "89504e470d0a1a0a");
    }

    const signingManifest = structuredClone(manifest);
    const signature = Buffer.from(signingManifest.signature.value, "base64url");
    delete signingManifest.signature.value;
    const key = keyring.keys.find((item) => item.keyId === manifest.signature.keyId);
    assert.ok(key);
    const publicKey = createPublicKey({
      key: Buffer.concat([Buffer.from("302a300506032b6570032100", "hex"), Buffer.from(key.publicKey, "base64url")]),
      format: "der",
      type: "spki",
    });
    assert.equal(verify(null, Buffer.from(canonicalize(signingManifest), "utf8"), publicKey, signature), true);
  }
});

function sha256(value) {
  return createHash("sha256").update(value).digest("hex");
}

function canonicalize(value) {
  if (value === null || typeof value === "boolean" || typeof value === "number" || typeof value === "string") return JSON.stringify(value);
  if (Array.isArray(value)) return `[${value.map(canonicalize).join(",")}]`;
  return `{${Object.keys(value).sort().map((key) => `${JSON.stringify(key)}:${canonicalize(value[key])}`).join(",")}}`;
}

function readStoredZip(archive) {
  const entries = new Map();
  let offset = 0;
  while (offset + 4 <= archive.length && archive.readUInt32LE(offset) === 0x04034b50) {
    const compression = archive.readUInt16LE(offset + 8);
    const compressedSize = archive.readUInt32LE(offset + 18);
    const uncompressedSize = archive.readUInt32LE(offset + 22);
    const nameLength = archive.readUInt16LE(offset + 26);
    const extraLength = archive.readUInt16LE(offset + 28);
    assert.equal(compression, 0);
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
