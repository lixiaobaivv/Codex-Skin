import { createHash, createPrivateKey, createPublicKey, sign } from "node:crypto";
import { mkdir, readFile, writeFile } from "node:fs/promises";
import { dirname, join, resolve } from "node:path";
import { fileURLToPath } from "node:url";

const toolDir = dirname(fileURLToPath(import.meta.url));
const root = resolve(toolDir, "..", "..");
const outputDir = join(root, "samples", "dreamskin");
const packageName = "codex-skin-sample-1.0.0.dreamskin";
const packageOutput = join(outputDir, packageName);

// Public development fixture seed. Never use this key for production themes.
const fixtureSeed = Buffer.from(
  "7f9b1c4a2d6e8f1032547698badcfe0123456789abcdef001122334455667788",
  "hex",
);
const pkcs8Prefix = Buffer.from("302e020100300506032b657004220420", "hex");
const privateKey = createPrivateKey({
  key: Buffer.concat([pkcs8Prefix, fixtureSeed]),
  format: "der",
  type: "pkcs8",
});
const publicDer = createPublicKey(privateKey).export({ format: "der", type: "spki" });
const publicKey = publicDer.subarray(-32);

const fixturePackage = await readFile(packageOutput);
const background = readStoredZipEntry(fixturePackage, "background.jpg");
const preview = readStoredZipEntry(fixturePackage, "preview.png");
const backgroundSize = readJpegSize(background);
const previewSize = readPngSize(preview);

const manifest = {
  schemaVersion: 1,
  packageVersion: 1,
  id: "codex-skin.jackson-sage-sample",
  name: "千玺星球签名示例",
  version: "1.0.0",
  description: "用于验证 Codex-Skin-Store 与 Windows/macOS 导入器兼容性的签名示例主题。",
  author: {
    name: "Codex-Skin contributors",
    homepage: "https://github.com/lixiaobaivv/Codex-Skin",
  },
  engineVersion: {
    min: "1.0.0",
    maxExclusive: "2.0.0",
  },
  platforms: ["macos", "windows"],
  brandSubtitle: "Codex Dream Skin · Signed Fixture",
  tagline: "先验证，再安装。",
  projectPrefix: "示例",
  projectLabel: "千玺星球",
  statusText: "Ed25519 已签名",
  quote: "同一个主题包，跨平台保持同一份信任契约。",
  image: "background.jpg",
  colors: {
    background: "#cbd7ca",
    panel: "#f4f5ef",
    panelAlt: "#dfe8dc",
    accent: "#516b54",
    accentAlt: "#9d694f",
    secondary: "#78937b",
    highlight: "#d7b46a",
    text: "#1d2a20",
    muted: "#647066",
    line: "#aebdaf",
  },
  assets: {
    background: {
      path: "background.jpg",
      mediaType: "image/jpeg",
      bytes: background.length,
      width: backgroundSize.width,
      height: backgroundSize.height,
      sha256: sha256(background),
    },
    preview: {
      path: "preview.png",
      mediaType: "image/png",
      bytes: preview.length,
      width: previewSize.width,
      height: previewSize.height,
      sha256: sha256(preview),
    },
  },
  signature: {
    algorithm: "Ed25519",
    canonicalization: "RFC8785",
    keyId: "codex-skin.sample.2026-01",
    signedAt: "2026-07-16T12:00:00.000Z",
    value: "",
  },
};

const signingManifest = structuredClone(manifest);
delete signingManifest.signature.value;
const canonical = Buffer.from(canonicalize(signingManifest), "utf8");
manifest.signature.value = sign(null, canonical, privateKey).toString("base64url");

const manifestBytes = Buffer.from(`${JSON.stringify(manifest, null, 2)}\n`, "utf8");
const packageBytes = createStoredZip([
  ["theme.json", manifestBytes],
  ["background.jpg", background],
  ["preview.png", preview],
]);

await mkdir(outputDir, { recursive: true });
const packageUrl = `https://github.com/lixiaobaivv/Codex-Skin/releases/download/sample-v1/${packageName}`;
const packageSha256 = sha256(packageBytes);
const installParameters = new URLSearchParams({
  url: packageUrl,
  sha256: packageSha256,
  size: String(packageBytes.length),
  id: manifest.id,
  version: manifest.version,
});
await writeFile(packageOutput, packageBytes);
await writeFile(join(outputDir, "public-keys.json"), `${JSON.stringify({
  schemaVersion: 1,
  keys: [{
    keyId: manifest.signature.keyId,
    algorithm: "Ed25519",
    publicKey: publicKey.toString("base64url"),
    purpose: "development-fixture-only",
  }],
}, null, 2)}\n`);
await writeFile(join(outputDir, "catalog-entry.json"), `${JSON.stringify({
  schemaVersion: 1,
  id: manifest.id,
  name: manifest.name,
  version: manifest.version,
  package: {
    url: packageUrl,
    sha256: packageSha256,
    size: packageBytes.length,
  },
  installUri: `dreamskin://install?${installParameters}`,
  compatibility: {
    client: ">=1.0.0 <2.0.0",
    platforms: manifest.platforms,
  },
}, null, 2)}\n`);

console.log(JSON.stringify({
  package: packageOutput,
  bytes: packageBytes.length,
  sha256: sha256(packageBytes),
  keyId: manifest.signature.keyId,
  publicKey: publicKey.toString("base64url"),
}, null, 2));

function sha256(value) {
  return createHash("sha256").update(value).digest("hex");
}

function canonicalize(value) {
  if (value === null || typeof value === "boolean" || typeof value === "number" || typeof value === "string") {
    return JSON.stringify(value);
  }
  if (Array.isArray(value)) {
    return `[${value.map(canonicalize).join(",")}]`;
  }
  return `{${Object.keys(value).sort().map((key) => `${JSON.stringify(key)}:${canonicalize(value[key])}`).join(",")}}`;
}

function readPngSize(bytes) {
  if (!bytes.subarray(0, 8).equals(Buffer.from("89504e470d0a1a0a", "hex"))) {
    throw new Error("Preview fixture is not PNG.");
  }
  return { width: bytes.readUInt32BE(16), height: bytes.readUInt32BE(20) };
}

function readJpegSize(bytes) {
  if (bytes[0] !== 0xff || bytes[1] !== 0xd8) throw new Error("Background fixture is not JPEG.");
  let offset = 2;
  while (offset + 9 < bytes.length) {
    if (bytes[offset] !== 0xff) { offset += 1; continue; }
    const marker = bytes[offset + 1];
    if (marker === 0xd8 || marker === 0xd9) { offset += 2; continue; }
    const length = bytes.readUInt16BE(offset + 2);
    if (length < 2 || offset + 2 + length > bytes.length) break;
    if ([0xc0, 0xc1, 0xc2, 0xc3, 0xc5, 0xc6, 0xc7, 0xc9, 0xca, 0xcb, 0xcd, 0xce, 0xcf].includes(marker)) {
      return { height: bytes.readUInt16BE(offset + 5), width: bytes.readUInt16BE(offset + 7) };
    }
    offset += 2 + length;
  }
  throw new Error("Cannot read JPEG dimensions.");
}

function readStoredZipEntry(archive, wantedName) {
  let offset = 0;
  while (offset + 30 <= archive.length && archive.readUInt32LE(offset) === 0x04034b50) {
    const method = archive.readUInt16LE(offset + 8);
    const compressedSize = archive.readUInt32LE(offset + 18);
    const nameLength = archive.readUInt16LE(offset + 26);
    const extraLength = archive.readUInt16LE(offset + 28);
    const nameStart = offset + 30;
    const dataStart = nameStart + nameLength + extraLength;
    const dataEnd = dataStart + compressedSize;
    if (dataEnd > archive.length) throw new Error("Fixture package contains a truncated ZIP entry.");
    const name = archive.subarray(nameStart, nameStart + nameLength).toString("utf8");
    if (name === wantedName) {
      if (method !== 0) throw new Error(`${wantedName} must use the deterministic stored ZIP method.`);
      return archive.subarray(dataStart, dataEnd);
    }
    offset = dataEnd;
  }
  throw new Error(`Fixture package is missing ${wantedName}.`);
}

function createStoredZip(files) {
  const localParts = [];
  const centralParts = [];
  let offset = 0;
  for (const [name, data] of files) {
    const nameBytes = Buffer.from(name, "utf8");
    const checksum = crc32(data);
    const local = Buffer.alloc(30);
    local.writeUInt32LE(0x04034b50, 0);
    local.writeUInt16LE(20, 4);
    local.writeUInt16LE(0x0800, 6);
    local.writeUInt16LE(0, 8);
    local.writeUInt16LE(0, 10);
    local.writeUInt16LE(0x5c21, 12);
    local.writeUInt32LE(checksum, 14);
    local.writeUInt32LE(data.length, 18);
    local.writeUInt32LE(data.length, 22);
    local.writeUInt16LE(nameBytes.length, 26);
    local.writeUInt16LE(0, 28);
    localParts.push(local, nameBytes, data);

    const central = Buffer.alloc(46);
    central.writeUInt32LE(0x02014b50, 0);
    central.writeUInt16LE((3 << 8) | 20, 4);
    central.writeUInt16LE(20, 6);
    central.writeUInt16LE(0x0800, 8);
    central.writeUInt16LE(0, 10);
    central.writeUInt16LE(0, 12);
    central.writeUInt16LE(0x5c21, 14);
    central.writeUInt32LE(checksum, 16);
    central.writeUInt32LE(data.length, 20);
    central.writeUInt32LE(data.length, 24);
    central.writeUInt16LE(nameBytes.length, 28);
    central.writeUInt16LE(0, 30);
    central.writeUInt16LE(0, 32);
    central.writeUInt16LE(0, 34);
    central.writeUInt16LE(0, 36);
    central.writeUInt32LE((0o100644 * 0x10000) >>> 0, 38);
    central.writeUInt32LE(offset, 42);
    centralParts.push(central, nameBytes);
    offset += local.length + nameBytes.length + data.length;
  }
  const centralDirectory = Buffer.concat(centralParts);
  const end = Buffer.alloc(22);
  end.writeUInt32LE(0x06054b50, 0);
  end.writeUInt16LE(0, 4);
  end.writeUInt16LE(0, 6);
  end.writeUInt16LE(files.length, 8);
  end.writeUInt16LE(files.length, 10);
  end.writeUInt32LE(centralDirectory.length, 12);
  end.writeUInt32LE(offset, 16);
  end.writeUInt16LE(0, 20);
  return Buffer.concat([...localParts, centralDirectory, end]);
}

function crc32(bytes) {
  const table = getCrcTable();
  let value = 0xffffffff;
  for (const byte of bytes) value = (value >>> 8) ^ table[(value ^ byte) & 0xff];
  return (value ^ 0xffffffff) >>> 0;
}

function getCrcTable() {
  return Array.from({ length: 256 }, (_, index) => {
    let value = index;
    for (let bit = 0; bit < 8; bit += 1) value = (value >>> 1) ^ ((value & 1) ? 0xedb88320 : 0);
    return value >>> 0;
  });
}
