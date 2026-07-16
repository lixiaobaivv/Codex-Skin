import { createHash, createPrivateKey, sign } from "node:crypto";
import { mkdir, writeFile } from "node:fs/promises";
import { dirname, join, resolve } from "node:path";
import { fileURLToPath } from "node:url";
import { deflateSync } from "node:zlib";

const toolDir = dirname(fileURLToPath(import.meta.url));
const root = resolve(toolDir, "..", "..");
const outputDir = join(root, "samples", "dreamskin", "catalog");
const releaseTag = "catalog-v1";
const signedAt = "2026-07-16T15:30:00.000Z";

// Public development fixture seed. These packages exercise interoperability only.
// Never use this key to establish production publisher identity.
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

const themes = [
  {
    slug: "aurora-drift",
    name: "极光漫游",
    version: "1.3.0",
    description: "蓝紫极光掠过深夜的双平台签名互操作样例。",
    platforms: ["macos", "windows"],
    pattern: "aurora",
    colors: ["#080b18", "#0b1121", "#17112f", "#8be8ff", "#c4b5fd", "#6ee7ff", "#a78bfa", "#f4f7ff", "#9aa9c6", "#52617d"],
  },
  {
    slug: "ember-terminal",
    name: "余烬终端",
    version: "1.0.0",
    description: "炭黑与琥珀余温构成的双平台签名互操作样例。",
    platforms: ["macos", "windows"],
    pattern: "embers",
    colors: ["#0f0d0c", "#14110f", "#211813", "#e9a35b", "#f1bd76", "#9a5d31", "#ef8537", "#f4eee7", "#a69a8f", "#6d4c35"],
  },
  {
    slug: "linen-light",
    name: "亚麻晨光",
    version: "1.0.6",
    description: "米白纸张质感的 macOS 签名互操作样例。",
    platforms: ["macos"],
    pattern: "paper",
    colors: ["#eee8dc", "#fffdf8", "#e4d8c6", "#a55e2f", "#7c4a2e", "#c9b79d", "#ffffff", "#332e27", "#817666", "#c8bda9"],
  },
  {
    slug: "midnight-grid",
    name: "午夜网格",
    version: "1.1.2",
    description: "蓝黑精密网格构成的双平台签名互操作样例。",
    platforms: ["macos", "windows"],
    pattern: "grid",
    colors: ["#080a0f", "#0d1118", "#111b2a", "#65a5ff", "#8cc8ff", "#327eff", "#64a0e8", "#eef4ff", "#8491a5", "#344968"],
  },
  {
    slug: "moss-and-mist",
    name: "苔色山雾",
    version: "1.0.1",
    description: "层叠苔绿与清晨薄雾构成的双平台签名互操作样例。",
    platforms: ["macos", "windows"],
    pattern: "mist",
    colors: ["#17221d", "#141f19", "#4b654c", "#a9cf9b", "#c3dda7", "#789478", "#cfdeca", "#eef4eb", "#9dad9e", "#607861"],
  },
  {
    slug: "ocean-glass",
    name: "海面玻璃",
    version: "1.2.1",
    description: "海岸青蓝与玻璃面板构成的双平台签名互操作样例。",
    platforms: ["macos", "windows"],
    pattern: "horizon",
    colors: ["#9ed7dc", "#edfbfa", "#5bbbc5", "#087f8c", "#075f72", "#155c79", "#ffffff", "#163b45", "#47727a", "#8fcbd0"],
  },
  {
    slug: "paper-observatory",
    name: "纸上天文台",
    version: "1.0.2",
    description: "暖灰纸面与深蓝星图构成的 macOS 签名互操作样例。",
    platforms: ["macos"],
    pattern: "stars",
    colors: ["#c9c2b3", "#f0ece2", "#b4ab9a", "#284d7a", "#513f64", "#697f9a", "#fffdf7", "#202c3d", "#697080", "#8d8b87"],
  },
  {
    slug: "solar-bloom",
    name: "日光绽放",
    version: "1.0.0",
    description: "珊瑚橙与玫瑰渐变构成的 Windows 签名互操作样例。",
    platforms: ["windows"],
    pattern: "orb",
    colors: ["#7f284d", "#481b40", "#4d2b82", "#ffd68d", "#ffc6a5", "#bc3f6b", "#ee8c52", "#fff8f4", "#f2c6c3", "#9c4e70"],
  },
];

await mkdir(outputDir, { recursive: true });
const catalog = [];

for (const theme of themes) {
  const background = createThemePng(1280, 800, theme.colors, theme.pattern);
  const preview = createThemePng(960, 600, theme.colors, theme.pattern);
  const [backgroundColor, panel, panelAlt, accent, accentAlt, secondary, highlight, text, muted, line] = theme.colors;
  const id = `codex-skin.catalog.${theme.slug}`;
  const manifest = {
    schemaVersion: 1,
    packageVersion: 1,
    id,
    name: theme.name,
    version: theme.version,
    description: theme.description,
    author: {
      name: "Codex-Skin contributors",
      homepage: "https://github.com/lixiaobaivv/Codex-Skin",
    },
    engineVersion: { min: "1.0.0", maxExclusive: "2.0.0" },
    platforms: theme.platforms,
    brandSubtitle: "Codex-Skin-Store · Signed Sample",
    tagline: theme.description,
    projectPrefix: "主题样例",
    projectLabel: theme.name,
    statusText: "Ed25519 已签名",
    quote: "先验证来源与完整性，再确认安装和应用。",
    image: "background.png",
    colors: { background: backgroundColor, panel, panelAlt, accent, accentAlt, secondary, highlight, text, muted, line },
    assets: {
      background: asset("background.png", background, 1280, 800),
      preview: asset("preview.png", preview, 960, 600),
    },
    signature: {
      algorithm: "Ed25519",
      canonicalization: "RFC8785",
      keyId: "codex-skin.sample.2026-01",
      signedAt,
      value: "",
    },
  };
  const signingManifest = structuredClone(manifest);
  delete signingManifest.signature.value;
  manifest.signature.value = sign(
    null,
    Buffer.from(canonicalize(signingManifest), "utf8"),
    privateKey,
  ).toString("base64url");

  const packageName = `${theme.slug}-${theme.version}.dreamskin`;
  const packageBytes = createStoredZip([
    ["theme.json", Buffer.from(`${JSON.stringify(manifest, null, 2)}\n`, "utf8")],
    ["background.png", background],
    ["preview.png", preview],
  ]);
  const packageUrl = `https://github.com/lixiaobaivv/Codex-Skin/releases/download/${releaseTag}/${packageName}`;
  const entry = {
    slug: theme.slug,
    package: {
      published: true,
      id,
      version: theme.version,
      url: packageUrl,
      sha256: sha256(packageBytes),
      size: packageBytes.length,
    },
  };
  await writeFile(join(outputDir, packageName), packageBytes);
  catalog.push(entry);
}

await writeFile(join(outputDir, "catalog-entries.json"), `${JSON.stringify(catalog, null, 2)}\n`);
console.log(JSON.stringify({ releaseTag, packages: catalog }, null, 2));

function asset(path, bytes, width, height) {
  return { path, mediaType: "image/png", bytes: bytes.length, width, height, sha256: sha256(bytes) };
}

function createThemePng(width, height, palette, pattern) {
  const colors = palette.map(parseHex);
  const rowLength = width * 4 + 1;
  const raw = Buffer.alloc(rowLength * height);
  for (let y = 0; y < height; y += 1) {
    const row = y * rowLength;
    raw[row] = 0;
    for (let x = 0; x < width; x += 1) {
      const nx = x / Math.max(1, width - 1);
      const ny = y / Math.max(1, height - 1);
      let color = mix(colors[0], colors[2], clamp(nx * 0.45 + ny * 0.55));
      color = mix(color, colors[5], radial(nx, ny, 0.78, 0.18, 0.48) * 0.65);
      color = mix(color, colors[3], radial(nx, ny, 0.22, 0.16, 0.42) * 0.42);
      color = applyPattern(color, colors, pattern, x, y, nx, ny, width, height);
      const offset = row + 1 + x * 4;
      raw[offset] = color[0];
      raw[offset + 1] = color[1];
      raw[offset + 2] = color[2];
      raw[offset + 3] = 255;
    }
  }
  const header = Buffer.alloc(13);
  header.writeUInt32BE(width, 0);
  header.writeUInt32BE(height, 4);
  header[8] = 8;
  header[9] = 6;
  return Buffer.concat([
    Buffer.from("89504e470d0a1a0a", "hex"),
    pngChunk("IHDR", header),
    pngChunk("IDAT", deflateSync(raw, { level: 9 })),
    pngChunk("IEND", Buffer.alloc(0)),
  ]);
}

function applyPattern(base, colors, pattern, x, y, nx, ny, width, height) {
  if (pattern === "grid" && (x % Math.max(24, Math.round(width / 20)) < 2 || y % Math.max(24, Math.round(height / 13)) < 2)) return mix(base, colors[3], 0.22);
  if (pattern === "paper" && (y % 7 === 0 || x % 43 === 0)) return mix(base, colors[8], 0.06);
  if (pattern === "horizon") {
    if (ny < 0.34) return mix(base, colors[6], 0.48 * (1 - ny / 0.34));
    if (Math.abs(ny - 0.55) < 0.025) return mix(base, colors[6], 0.32);
  }
  if (pattern === "mist") {
    const band = Math.abs(ny - (0.34 + Math.sin(nx * 10) * 0.06));
    if (band < 0.09) return mix(base, colors[6], (0.09 - band) * 4.2);
  }
  if (pattern === "orb") {
    const glow = radial(nx, ny, 0.72, 0.22, 0.28);
    return mix(base, colors[6], glow * 0.82);
  }
  if (pattern === "stars" && ((x * 73 + y * 37) % 997 < 3)) return mix(base, colors[6], 0.82);
  if (pattern === "embers" && ((x * 31 + y * 71) % 1301 < 4)) return mix(base, colors[3], 0.78);
  if (pattern === "aurora") {
    const ribbonA = Math.abs(ny - (0.22 + Math.sin(nx * 8.5) * 0.11));
    const ribbonB = Math.abs(ny - (0.52 + Math.sin(nx * 6.2 + 1.7) * 0.12));
    if (ribbonA < 0.08) base = mix(base, colors[3], (0.08 - ribbonA) * 6.3);
    if (ribbonB < 0.07) base = mix(base, colors[4], (0.07 - ribbonB) * 5.7);
  }
  return base;
}

function pngChunk(type, data) {
  const typeBytes = Buffer.from(type, "ascii");
  const chunk = Buffer.alloc(12 + data.length);
  chunk.writeUInt32BE(data.length, 0);
  typeBytes.copy(chunk, 4);
  data.copy(chunk, 8);
  chunk.writeUInt32BE(crc32(Buffer.concat([typeBytes, data])), 8 + data.length);
  return chunk;
}

function parseHex(value) {
  return [Number.parseInt(value.slice(1, 3), 16), Number.parseInt(value.slice(3, 5), 16), Number.parseInt(value.slice(5, 7), 16)];
}

function mix(left, right, amount) {
  const value = clamp(amount);
  return left.map((channel, index) => Math.round(channel + (right[index] - channel) * value));
}

function radial(x, y, centerX, centerY, radius) {
  return clamp(1 - Math.hypot(x - centerX, y - centerY) / radius);
}

function clamp(value) {
  return Math.max(0, Math.min(1, value));
}

function sha256(value) {
  return createHash("sha256").update(value).digest("hex");
}

function canonicalize(value) {
  if (value === null || typeof value === "boolean" || typeof value === "number" || typeof value === "string") return JSON.stringify(value);
  if (Array.isArray(value)) return `[${value.map(canonicalize).join(",")}]`;
  return `{${Object.keys(value).sort().map((key) => `${JSON.stringify(key)}:${canonicalize(value[key])}`).join(",")}}`;
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
    central.writeUInt16LE(0, 10);
    central.writeUInt16LE(0, 12);
    central.writeUInt16LE(0x5c21, 14);
    central.writeUInt32LE(checksum, 16);
    central.writeUInt32LE(data.length, 20);
    central.writeUInt32LE(data.length, 24);
    central.writeUInt16LE(nameBytes.length, 28);
    central.writeUInt32LE((0o100644 * 0x10000) >>> 0, 38);
    central.writeUInt32LE(offset, 42);
    centralParts.push(central, nameBytes);
    offset += local.length + nameBytes.length + data.length;
  }
  const centralDirectory = Buffer.concat(centralParts);
  const end = Buffer.alloc(22);
  end.writeUInt32LE(0x06054b50, 0);
  end.writeUInt16LE(files.length, 8);
  end.writeUInt16LE(files.length, 10);
  end.writeUInt32LE(centralDirectory.length, 12);
  end.writeUInt32LE(offset, 16);
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
