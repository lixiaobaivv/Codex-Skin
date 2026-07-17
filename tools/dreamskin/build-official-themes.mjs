import { createHash, createPrivateKey, sign } from "node:crypto";
import { mkdir, readFile, writeFile } from "node:fs/promises";
import { dirname, extname, join, resolve } from "node:path";
import { fileURLToPath } from "node:url";

const root = resolve(dirname(fileURLToPath(import.meta.url)), "..", "..");
const outputDir = resolve(process.env.CODEX_SKIN_THEME_OUTPUT ?? join(root, "artifacts", "official-themes"));
const releaseTag = process.env.CODEX_SKIN_THEME_RELEASE_TAG ?? "official-themes-v1";
const signedAt = process.env.CODEX_SKIN_THEME_SIGNED_AT ?? "2026-07-17T06:00:00.000Z";
const seedHex = process.env.CODEX_SKIN_THEME_SIGNING_SEED?.trim();
if (!seedHex || !/^[a-f0-9]{64}$/.test(seedHex)) {
  throw new Error("CODEX_SKIN_THEME_SIGNING_SEED must be a 32-byte lowercase hex Ed25519 seed.");
}

const privateKey = createPrivateKey({
  key: Buffer.concat([Buffer.from("302e020100300506032b657004220420", "hex"), Buffer.from(seedHex, "hex")]),
  format: "der",
  type: "pkcs8",
});

const ids = ["dilraba-star", "enfp-pop", "jackson-sage", "kun-stage", "zhu-xudan-racing"];
const entries = [];
await mkdir(outputDir, { recursive: true });

for (const id of ids) {
  const definition = JSON.parse(await readFile(join(root, "themes", `${id}.json`), "utf8"));
  const backgroundPath = resolve(join(root, "themes", definition.theme.backgroundImage));
  const previewPath = resolve(join(root, "themes", definition.previewImage));
  const background = await readFile(backgroundPath);
  const preview = await readFile(previewPath);
  const backgroundName = `background${extname(backgroundPath).toLowerCase()}`;
  const backgroundType = backgroundName.endsWith(".png") ? "image/png" : "image/jpeg";
  const backgroundSize = readImageSize(background, backgroundName);
  const previewSize = readImageSize(preview, "preview.png");
  const surface = definition.theme.surface;
  const ink = definition.theme.ink;

  const manifest = {
    $schema: "https://raw.githubusercontent.com/lixiaobaivv/Codex-Skin-Store/main/spec/theme-package.schema.json",
    schemaVersion: 1,
    packageVersion: 1,
    id: definition.codeThemeId,
    name: definition.displayName,
    version: definition.version,
    description: definition.description,
    author: {
      name: definition.author,
      homepage: "https://github.com/lixiaobaivv/Codex-Skin-Store",
    },
    engineVersion: { min: "1.0.0", maxExclusive: "2.0.0" },
    platforms: ["macos", "windows"],
    brandSubtitle: definition.home.eyebrow,
    tagline: definition.home.subtitle,
    projectPrefix: definition.home.tags[0],
    projectLabel: definition.home.brand,
    statusText: definition.home.badge,
    quote: definition.home.footerNote,
    image: backgroundName,
    colors: {
      background: surface,
      panel: mixHex(surface, "#ffffff", definition.variant === "dark" ? 0.06 : 0.22),
      panelAlt: mixHex(surface, definition.theme.accent, 0.12),
      accent: definition.theme.accent,
      accentAlt: definition.theme.semanticColors.diffRemoved,
      secondary: definition.theme.semanticColors.skill,
      highlight: definition.theme.semanticColors.diffAdded,
      text: ink,
      muted: mixHex(ink, surface, 0.48),
      line: mixHex(ink, surface, 0.76),
    },
    assets: {
      background: asset(backgroundName, backgroundType, background, backgroundSize),
      preview: asset("preview.png", "image/png", preview, previewSize),
    },
    signature: {
      algorithm: "Ed25519",
      canonicalization: "RFC8785",
      keyId: "codex-skin.official.2026-01",
      signedAt,
      value: "",
    },
  };
  const signingManifest = structuredClone(manifest);
  delete signingManifest.signature.value;
  manifest.signature.value = sign(null, Buffer.from(canonicalize(signingManifest), "utf8"), privateKey).toString("base64url");

  const packageName = `Codex-Skin-theme-${id}-${definition.version}.dreamskin`;
  const bytes = createStoredZip([
    ["theme.json", Buffer.from(`${JSON.stringify(manifest, null, 2)}\n`, "utf8")],
    [backgroundName, background],
    ["preview.png", preview],
  ]);
  await writeFile(join(outputDir, packageName), bytes);
  entries.push({
    slug: id,
    name: definition.displayName,
    version: definition.version,
    package: {
      published: true,
      id: definition.codeThemeId,
      version: definition.version,
      url: `https://github.com/lixiaobaivv/Codex-Skin/releases/download/${releaseTag}/${packageName}`,
      sha256: sha256(bytes),
      size: bytes.length,
    },
  });
}

await writeFile(join(outputDir, "catalog-entries.json"), `${JSON.stringify(entries, null, 2)}\n`);
console.log(`Built ${entries.length} official theme packages in ${outputDir}.`);

function asset(path, mediaType, bytes, dimensions) {
  return { path, mediaType, bytes: bytes.length, ...dimensions, sha256: sha256(bytes) };
}

function sha256(value) {
  return createHash("sha256").update(value).digest("hex");
}

function mixHex(left, right, amount) {
  const parse = value => [1, 3, 5].map(index => Number.parseInt(value.slice(index, index + 2), 16));
  const a = parse(left);
  const b = parse(right);
  return `#${a.map((value, index) => Math.round(value + (b[index] - value) * amount).toString(16).padStart(2, "0")).join("")}`;
}

function readImageSize(bytes, name) {
  if (name.endsWith(".png")) {
    if (!bytes.subarray(0, 8).equals(Buffer.from("89504e470d0a1a0a", "hex"))) throw new Error(`${name} is not PNG.`);
    return { width: bytes.readUInt32BE(16), height: bytes.readUInt32BE(20) };
  }
  if (bytes[0] !== 0xff || bytes[1] !== 0xd8) throw new Error(`${name} is not JPEG.`);
  let offset = 2;
  while (offset + 9 < bytes.length) {
    if (bytes[offset] !== 0xff) { offset += 1; continue; }
    const marker = bytes[offset + 1];
    const length = bytes.readUInt16BE(offset + 2);
    if (length < 2 || offset + 2 + length > bytes.length) break;
    if ([0xc0, 0xc1, 0xc2, 0xc3, 0xc5, 0xc6, 0xc7, 0xc9, 0xca, 0xcb, 0xcd, 0xce, 0xcf].includes(marker)) {
      return { height: bytes.readUInt16BE(offset + 5), width: bytes.readUInt16BE(offset + 7) };
    }
    offset += 2 + length;
  }
  throw new Error(`Cannot read dimensions for ${name}.`);
}

function canonicalize(value) {
  if (value === null || typeof value === "boolean" || typeof value === "number" || typeof value === "string") return JSON.stringify(value);
  if (Array.isArray(value)) return `[${value.map(canonicalize).join(",")}]`;
  return `{${Object.keys(value).sort().map(key => `${JSON.stringify(key)}:${canonicalize(value[key])}`).join(",")}}`;
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
    localParts.push(local, nameBytes, data);
    const central = Buffer.alloc(46);
    central.writeUInt32LE(0x02014b50, 0);
    central.writeUInt16LE((3 << 8) | 20, 4);
    central.writeUInt16LE(20, 6);
    central.writeUInt16LE(0x0800, 8);
    central.writeUInt32LE(checksum, 16);
    central.writeUInt32LE(data.length, 20);
    central.writeUInt32LE(data.length, 24);
    central.writeUInt16LE(nameBytes.length, 28);
    central.writeUInt32LE((0o100644 * 0x10000) >>> 0, 38);
    central.writeUInt32LE(offset, 42);
    centralParts.push(central, nameBytes);
    offset += local.length + nameBytes.length + data.length;
  }
  const directory = Buffer.concat(centralParts);
  const end = Buffer.alloc(22);
  end.writeUInt32LE(0x06054b50, 0);
  end.writeUInt16LE(files.length, 8);
  end.writeUInt16LE(files.length, 10);
  end.writeUInt32LE(directory.length, 12);
  end.writeUInt32LE(offset, 16);
  return Buffer.concat([...localParts, directory, end]);
}

function crc32(bytes) {
  let crc = 0xffffffff;
  for (const byte of bytes) {
    crc ^= byte;
    for (let bit = 0; bit < 8; bit += 1) crc = (crc >>> 1) ^ (0xedb88320 & -(crc & 1));
  }
  return (crc ^ 0xffffffff) >>> 0;
}
