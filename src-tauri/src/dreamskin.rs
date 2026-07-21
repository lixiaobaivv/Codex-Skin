use crate::{
    error::{AppError, Result},
    paths,
};
use base64::Engine;
use ed25519_dalek::{Signature, Verifier, VerifyingKey};
use serde::{
    Deserialize, Deserializer,
    de::{MapAccess, SeqAccess, Visitor},
};
use serde_json::{Map, Value};
use sha2::{Digest, Sha256};
use std::{
    collections::{BTreeMap, HashSet},
    fs::{self, File},
    io::Read,
    path::{Path, PathBuf},
};

const MAX_PACKAGE: u64 = 28 * 1024 * 1024;
const MAX_MANIFEST: u64 = 64 * 1024;
const MAX_BACKGROUND: u64 = 16 * 1024 * 1024;
const MAX_PREVIEW: u64 = 2 * 1024 * 1024;
const MAX_EFFECT: u64 = 4 * 1024 * 1024;
const ROOT_ALLOWED: &[&str] = &[
    "$schema",
    "schemaVersion",
    "packageVersion",
    "id",
    "name",
    "version",
    "description",
    "author",
    "engineVersion",
    "platforms",
    "brandSubtitle",
    "tagline",
    "projectPrefix",
    "projectLabel",
    "statusText",
    "quote",
    "image",
    "colors",
    "assets",
    "effects",
    "signature",
];
const ROOT_REQUIRED: &[&str] = &[
    "schemaVersion",
    "packageVersion",
    "id",
    "name",
    "version",
    "description",
    "author",
    "engineVersion",
    "platforms",
    "image",
    "colors",
    "assets",
    "signature",
];
const COLORS: &[&str] = &[
    "background",
    "panel",
    "panelAlt",
    "accent",
    "accentAlt",
    "secondary",
    "highlight",
    "text",
    "muted",
    "line",
];

#[derive(Clone, Debug)]
struct Asset {
    path: String,
    media_type: String,
    bytes: u64,
    width: u32,
    height: u32,
    sha256: String,
}
#[derive(Clone, Debug)]
struct Manifest {
    id: String,
    name: String,
    version: String,
    background: Asset,
    preview: Asset,
    effect_assets: Vec<Asset>,
    key_id: String,
    signature: [u8; 64],
}
#[derive(Debug, serde::Serialize)]
#[serde(rename_all = "camelCase")]
pub struct ImportResult {
    pub id: String,
    pub name: String,
    pub version: String,
    pub package_sha256: String,
    pub install_path: String,
    pub already_installed: bool,
}
#[derive(Clone, Debug)]
pub struct Expected {
    pub sha256: String,
    pub size: u64,
    pub id: Option<String>,
    pub version: Option<String>,
}

pub fn import_local(input: &str) -> Result<ImportResult> {
    import_inner(input, None, None, None)
}
pub fn import_expected(input: &str, expected: Expected) -> Result<ImportResult> {
    import_inner(input, Some(expected), None, None)
}
pub fn verify_for_platform(input: &str, platform: &str) -> Result<ImportResult> {
    if !matches!(platform, "windows" | "macos") {
        return fail("DSI_UNSUPPORTED_PLATFORM", "只支持 Windows 或 macOS。");
    }
    let temporary = tempfile::tempdir()?;
    import_inner(input, None, Some(platform), Some(temporary.path()))
}
fn import_inner(
    input: &str,
    expected: Option<Expected>,
    platform: Option<&str>,
    library_override: Option<&Path>,
) -> Result<ImportResult> {
    let package = PathBuf::from(input.trim().trim_matches('"')).canonicalize()?;
    if package
        .extension()
        .and_then(|v| v.to_str())
        .map(|v| v.eq_ignore_ascii_case("dreamskin"))
        != Some(true)
    {
        return fail("DSI_PACKAGE_INVALID", "本地导入只接受 .dreamskin 文件。");
    }
    let metadata = fs::metadata(&package)?;
    if metadata.len() == 0 || metadata.len() > MAX_PACKAGE {
        return fail("DSI_SIZE_LIMIT", "主题包大小必须在 1 到 28 MiB 之间。");
    }
    let package_hash = hash_file(&package)?;
    if let Some(expected) = &expected {
        if metadata.len() != expected.size {
            return fail("DSI_SIZE_MISMATCH", "主题包大小与深链接声明不一致。");
        }
        if package_hash != expected.sha256 {
            return fail("DSI_HASH_MISMATCH", "主题包 SHA-256 与深链接声明不一致。");
        }
    }
    let mut archive = zip::ZipArchive::new(File::open(&package)?)?;
    validate_entries(&mut archive)?;
    let manifest_bytes = read_entry(&mut archive, "theme.json", MAX_MANIFEST)?;
    if manifest_bytes.starts_with(&[0xef, 0xbb, 0xbf]) {
        return fail("DSI_MANIFEST_INVALID", "theme.json 必须是无 BOM 的 UTF-8。");
    }
    let manifest_text = std::str::from_utf8(&manifest_bytes).map_err(|_| {
        AppError::Message("DSI_MANIFEST_INVALID: theme.json 不是严格 UTF-8。".into())
    })?;
    let root = parse_strict(manifest_text)?;
    let platform = platform.unwrap_or(if cfg!(windows) {
        "windows"
    } else if cfg!(target_os = "macos") {
        "macos"
    } else {
        "unsupported"
    });
    let manifest = validate_manifest(&root, platform)?;
    if let Some(expected) = &expected {
        if expected.id.as_deref().is_some_and(|v| v != manifest.id) {
            return fail("DSI_MANIFEST_INVALID", "下载包主题 ID 与深链接提示不一致。");
        }
        if expected
            .version
            .as_deref()
            .is_some_and(|v| v != manifest.version)
        {
            return fail("DSI_MANIFEST_INVALID", "下载包版本与深链接提示不一致。");
        }
    }
    verify_signature(&root, &manifest)?;
    let mut expected: HashSet<&str> = [
        "theme.json",
        manifest.background.path.as_str(),
        manifest.preview.path.as_str(),
    ]
    .into_iter()
    .collect();
    expected.extend(
        manifest
            .effect_assets
            .iter()
            .map(|asset| asset.path.as_str()),
    );
    let actual: HashSet<_> = (0..archive.len())
        .map(|index| archive.by_index(index).unwrap().name().to_owned())
        .collect();
    if actual.iter().map(String::as_str).collect::<HashSet<_>>() != expected {
        return fail("DSI_PACKAGE_INVALID", "ZIP 文件名与清单资源声明不一致。");
    }
    let background = read_entry(&mut archive, &manifest.background.path, MAX_BACKGROUND)?;
    validate_asset(&background, &manifest.background, 40_000_000)?;
    let preview = read_entry(&mut archive, &manifest.preview.path, MAX_PREVIEW)?;
    validate_asset(&preview, &manifest.preview, 8_000_000)?;
    let mut effect_bytes = Vec::new();
    for effect in &manifest.effect_assets {
        let bytes = read_entry(&mut archive, &effect.path, MAX_EFFECT)?;
        validate_asset(&bytes, effect, 16_000_000)?;
        effect_bytes.push((effect, bytes));
    }
    let expanded = manifest_bytes.len() as u64
        + background.len() as u64
        + preview.len() as u64
        + effect_bytes
            .iter()
            .map(|(_, bytes)| bytes.len() as u64)
            .sum::<u64>();
    if expanded > MAX_PACKAGE {
        return fail("DSI_SIZE_LIMIT", "主题包解压后超过 28 MiB。");
    }
    let library = library_override
        .map(Path::to_owned)
        .map(Ok)
        .unwrap_or_else(paths::installed_root)?;
    fs::create_dir_all(&library)?;
    let target = library.join(&manifest.id).join(&manifest.version);
    if target.exists() {
        let matches = fs::read(target.join("theme.json")).ok().as_deref() == Some(&manifest_bytes)
            && hash_file(&target.join(&manifest.background.path))
                .ok()
                .as_deref()
                == Some(&manifest.background.sha256)
            && hash_file(&target.join(&manifest.preview.path))
                .ok()
                .as_deref()
                == Some(&manifest.preview.sha256)
            && manifest.effect_assets.iter().all(|asset| {
                hash_file(&target.join(&asset.path)).ok().as_deref() == Some(&asset.sha256)
            });
        if !matches {
            return fail("DSI_INSTALL_CONFLICT", "同一 ID 和版本已存在，但内容不同。");
        }
        return Ok(ImportResult {
            id: manifest.id,
            name: manifest.name,
            version: manifest.version,
            package_sha256: package_hash,
            install_path: target.to_string_lossy().into_owned(),
            already_installed: true,
        });
    }
    let staging = tempfile::Builder::new()
        .prefix(".install-")
        .tempdir_in(&library)?;
    fs::write(staging.path().join("theme.json"), &manifest_bytes)?;
    fs::write(staging.path().join(&manifest.background.path), background)?;
    fs::write(staging.path().join(&manifest.preview.path), preview)?;
    for (effect, bytes) in effect_bytes {
        fs::write(staging.path().join(&effect.path), bytes)?;
    }
    fs::create_dir_all(target.parent().unwrap())?;
    let staging_path = staging.keep();
    fs::rename(&staging_path, &target).inspect_err(|_| {
        let _ = fs::remove_dir_all(&staging_path);
    })?;
    Ok(ImportResult {
        id: manifest.id,
        name: manifest.name,
        version: manifest.version,
        package_sha256: package_hash,
        install_path: target.to_string_lossy().into_owned(),
        already_installed: false,
    })
}

fn validate_entries(archive: &mut zip::ZipArchive<File>) -> Result<()> {
    if !(3..=5).contains(&archive.len()) {
        return fail("DSI_PACKAGE_INVALID", "主题包必须包含 3–5 个根目录文件。");
    }
    let mut names = HashSet::new();
    for index in 0..archive.len() {
        let entry = archive.by_index(index)?;
        let name = entry.name();
        let unix_mode = entry.unix_mode().unwrap_or(0) & 0o170000;
        if name.is_empty()
            || Path::new(name).file_name().and_then(|v| v.to_str()) != Some(name)
            || name.contains(['/', '\\', ':', '\0'])
            || unix_mode == 0o120000
            || entry.is_dir()
        {
            return fail("DSI_ZIP_TRAVERSAL", "ZIP 只能包含根目录普通文件。");
        }
        if !names.insert(name.to_ascii_lowercase()) {
            return fail("DSI_PACKAGE_INVALID", "ZIP 包含大小写冲突或重复文件名。");
        }
    }
    Ok(())
}
fn read_entry(archive: &mut zip::ZipArchive<File>, name: &str, limit: u64) -> Result<Vec<u8>> {
    let entry = archive
        .by_name(name)
        .map_err(|_| AppError::Message(format!("DSI_PACKAGE_INVALID: 缺少资源 {name}。")))?;
    if entry.size() > limit {
        return fail("DSI_SIZE_LIMIT", "ZIP 条目解压后超过允许大小。");
    }
    let mut output = Vec::with_capacity(entry.size() as usize);
    entry.take(limit + 1).read_to_end(&mut output)?;
    if output.len() as u64 > limit {
        return fail("DSI_SIZE_LIMIT", "ZIP 条目解压后超过允许大小。");
    }
    Ok(output)
}

fn validate_manifest(root: &Value, current_platform: &str) -> Result<Manifest> {
    closed(root, ROOT_ALLOWED, ROOT_REQUIRED, "theme.json")?;
    if integer(root, "schemaVersion")? != 1 || integer(root, "packageVersion")? != 1 {
        return fail(
            "DSI_MANIFEST_INVALID",
            "只支持 schemaVersion=1 和 packageVersion=1。",
        );
    }
    if let Some(schema) = root.get("$schema")
        && string(schema, "$schema")?
            != "https://raw.githubusercontent.com/lixiaobaivv/Codex-Skin-Store/main/spec/theme-package.schema.json"
    {
        return fail("DSI_MANIFEST_INVALID", "$schema 不是受支持的规范地址。");
    }
    let id = text(root, "id", 128)?;
    if id.len() < 3 || !valid_id(&id) {
        return fail("DSI_MANIFEST_INVALID", "主题 ID 格式无效。");
    }
    let name = text(root, "name", 80)?;
    text(root, "description", 500)?;
    let version = semver(root, "version")?;
    let author = property(root, "author")?;
    closed(author, &["name", "homepage"], &["name"], "author")?;
    text(author, "name", 80)?;
    if let Some(homepage) = author.get("homepage") {
        let url = url::Url::parse(string(homepage, "author.homepage")?).map_err(|_| {
            AppError::Message("DSI_MANIFEST_INVALID: author.homepage 必须是 HTTPS URL。".into())
        })?;
        if url.scheme() != "https" {
            return fail("DSI_MANIFEST_INVALID", "author.homepage 必须是 HTTPS URL。");
        }
    }
    let engine = property(root, "engineVersion")?;
    closed(
        engine,
        &["min", "maxExclusive"],
        &["min", "maxExclusive"],
        "engineVersion",
    )?;
    let min = semver(engine, "min")?;
    let max = semver(engine, "maxExclusive")?;
    let current = semver::Version::parse("1.0.0").unwrap();
    if current < semver::Version::parse(&min).unwrap()
        || current >= semver::Version::parse(&max).unwrap()
    {
        return fail("DSI_UNSUPPORTED_ENGINE", "主题与当前引擎版本不兼容。");
    }
    let platforms = property(root, "platforms")?
        .as_array()
        .ok_or_else(|| AppError::Message("DSI_MANIFEST_INVALID: platforms 必须是数组。".into()))?;
    if platforms.is_empty() || platforms.len() > 2 {
        return fail("DSI_MANIFEST_INVALID", "platforms 必须包含 1 到 2 个平台。");
    }
    let values: Vec<_> = platforms
        .iter()
        .map(|v| string(v, "platforms[]").map(str::to_owned))
        .collect::<Result<_>>()?;
    if values.iter().collect::<HashSet<_>>().len() != values.len()
        || values.iter().any(|v| v != "windows" && v != "macos")
    {
        return fail("DSI_MANIFEST_INVALID", "platforms 包含重复或未知平台。");
    }
    if !values.iter().any(|v| v == current_platform) {
        return fail("DSI_UNSUPPORTED_PLATFORM", "该主题未声明支持当前平台。");
    }
    for key in [
        "brandSubtitle",
        "projectPrefix",
        "projectLabel",
        "statusText",
        "quote",
    ] {
        if let Some(value) = root.get(key)
            && string(value, key)?.chars().count() > 80
        {
            return fail("DSI_MANIFEST_INVALID", &format!("{key} 文本过长。"));
        }
    }
    if let Some(value) = root.get("tagline")
        && string(value, "tagline")?.chars().count() > 160
    {
        return fail("DSI_MANIFEST_INVALID", "tagline 文本过长。");
    }
    let colors = property(root, "colors")?;
    closed(colors, COLORS, COLORS, "colors")?;
    for key in COLORS {
        let value = text(colors, key, 32)?;
        if !valid_color(&value) {
            return fail(
                "DSI_MANIFEST_INVALID",
                &format!("colors.{key} 不是允许的颜色格式。"),
            );
        }
    }
    let assets = property(root, "assets")?;
    closed(
        assets,
        &["background", "preview", "effectOverlay", "composerAccent"],
        &["background", "preview"],
        "assets",
    )?;
    let background = asset(property(assets, "background")?, "background")?;
    let preview = asset(property(assets, "preview")?, "preview")?;
    let mut effect_assets = Vec::new();
    for role in ["effectOverlay", "composerAccent"] {
        if let Some(value) = assets.get(role) {
            effect_assets.push(asset(value, role)?);
        }
    }
    if text(root, "image", 32)? != background.path {
        return fail(
            "DSI_MANIFEST_INVALID",
            "image 必须等于 assets.background.path。",
        );
    }
    validate_package_effects(root.get("effects"), assets)?;
    let signature = property(root, "signature")?;
    closed(
        signature,
        &[
            "algorithm",
            "canonicalization",
            "keyId",
            "signedAt",
            "value",
        ],
        &[
            "algorithm",
            "canonicalization",
            "keyId",
            "signedAt",
            "value",
        ],
        "signature",
    )?;
    if text(signature, "algorithm", 16)? != "Ed25519"
        || text(signature, "canonicalization", 16)? != "RFC8785"
    {
        return fail("DSI_MANIFEST_INVALID", "签名算法必须是 Ed25519/RFC8785。");
    }
    let key_id = text(signature, "keyId", 64)?;
    if !key_id
        .chars()
        .all(|c| c.is_ascii_alphanumeric() || "._-".contains(c))
    {
        return fail("DSI_MANIFEST_INVALID", "signature.keyId 格式无效。");
    }
    let signed_at = text(signature, "signedAt", 64)?;
    if chrono::DateTime::parse_from_rfc3339(&signed_at).is_err() {
        return fail("DSI_MANIFEST_INVALID", "signature.signedAt 不是有效时间。");
    }
    let value = text(signature, "value", 86)?;
    let bytes = base64::engine::general_purpose::URL_SAFE_NO_PAD
        .decode(&value)
        .map_err(|_| {
            AppError::Message("DSI_SIGNATURE_INVALID: 签名不是有效的无填充 base64url。".into())
        })?;
    let signature: [u8; 64] = bytes.try_into().map_err(|_| {
        AppError::Message("DSI_SIGNATURE_INVALID: Ed25519 签名必须是 64 字节。".into())
    })?;
    Ok(Manifest {
        id,
        name,
        version,
        background,
        preview,
        effect_assets,
        key_id,
        signature,
    })
}

fn validate_package_effects(effects: Option<&Value>, assets: &Value) -> Result<()> {
    let has_overlay_asset = assets.get("effectOverlay").is_some();
    let has_composer_asset = assets.get("composerAccent").is_some();
    let Some(effects) = effects else {
        if has_overlay_asset || has_composer_asset {
            return fail("DSI_MANIFEST_INVALID", "特效资源缺少 effects 声明。");
        }
        return Ok(());
    };
    closed(
        effects,
        &["ambient", "intensity", "overlay", "composerAccent"],
        &[],
        "effects",
    )?;
    if let Some(value) = effects.get("ambient")
        && !["none", "rain", "particles", "storm"].contains(&string(value, "effects.ambient")?)
    {
        return fail("DSI_MANIFEST_INVALID", "effects.ambient 无效。");
    }
    if let Some(value) = effects.get("intensity")
        && !["subtle", "balanced", "vivid"].contains(&string(value, "effects.intensity")?)
    {
        return fail("DSI_MANIFEST_INVALID", "effects.intensity 无效。");
    }
    if let Some(overlay) = effects.get("overlay") {
        closed(
            overlay,
            &["image", "triggers", "position", "widthPercent"],
            &["image", "triggers"],
            "effects.overlay",
        )?;
        if text(overlay, "image", 32)? != "effect-overlay.png" || !has_overlay_asset {
            return fail("DSI_MANIFEST_INVALID", "瞬时叠加素材声明与资源不一致。");
        }
        validate_package_triggers(property(overlay, "triggers")?)?;
        if let Some(position) = overlay.get("position") {
            closed(
                position,
                &["x", "y"],
                &["x", "y"],
                "effects.overlay.position",
            )?;
            for axis in ["x", "y"] {
                if !(0..=100).contains(&integer(position, axis)?) {
                    return fail("DSI_MANIFEST_INVALID", "特效位置越界。");
                }
            }
        }
        if overlay.get("widthPercent").is_some()
            && !(10..=80).contains(&integer(overlay, "widthPercent")?)
        {
            return fail("DSI_MANIFEST_INVALID", "瞬时叠加素材宽度越界。");
        }
    } else if has_overlay_asset {
        return fail("DSI_MANIFEST_INVALID", "瞬时叠加资源缺少声明。");
    }
    if let Some(accent) = effects.get("composerAccent") {
        closed(
            accent,
            &["image", "triggers", "widthPx"],
            &["image", "triggers"],
            "effects.composerAccent",
        )?;
        if text(accent, "image", 32)? != "composer-accent.png" || !has_composer_asset {
            return fail("DSI_MANIFEST_INVALID", "输入框装饰声明与资源不一致。");
        }
        validate_package_triggers(property(accent, "triggers")?)?;
        if accent.get("widthPx").is_some() && !(48..=240).contains(&integer(accent, "widthPx")?) {
            return fail("DSI_MANIFEST_INVALID", "输入框装饰宽度越界。");
        }
    } else if has_composer_asset {
        return fail("DSI_MANIFEST_INVALID", "输入框装饰资源缺少声明。");
    }
    Ok(())
}

fn validate_package_triggers(value: &Value) -> Result<()> {
    let values = value
        .as_array()
        .ok_or_else(|| AppError::Message("DSI_MANIFEST_INVALID: 特效触发器必须是数组。".into()))?;
    if values.is_empty() || values.len() > 2 {
        return fail("DSI_MANIFEST_INVALID", "特效触发器数量无效。");
    }
    let mut seen = HashSet::new();
    for value in values {
        let value = string(value, "effects.triggers[]")?;
        if !["task-start", "message-send"].contains(&value) || !seen.insert(value) {
            return fail("DSI_MANIFEST_INVALID", "特效触发器无效或重复。");
        }
    }
    Ok(())
}

fn asset(value: &Value, role: &str) -> Result<Asset> {
    let keys = ["path", "mediaType", "bytes", "width", "height", "sha256"];
    closed(value, &keys, &keys, &format!("assets.{role}"))?;
    let path = text(value, "path", 32)?;
    let media_type = text(value, "mediaType", 32)?;
    let bytes = positive(value, "bytes")?;
    let width = positive(value, "width")? as u32;
    let height = positive(value, "height")? as u32;
    let sha256 = text(value, "sha256", 64)?;
    let valid_path = match (role, media_type.as_str()) {
        ("background", "image/png") => path == "background.png",
        ("background", "image/jpeg") => path == "background.jpg" || path == "background.jpeg",
        ("background", "image/webp") => path == "background.webp",
        ("preview", "image/png") => path == "preview.png",
        ("preview", "image/jpeg") => path == "preview.jpg" || path == "preview.jpeg",
        ("preview", "image/webp") => path == "preview.webp",
        ("effectOverlay", "image/png") => path == "effect-overlay.png",
        ("composerAccent", "image/png") => path == "composer-accent.png",
        _ => false,
    };
    if !valid_path {
        return fail(
            "DSI_MANIFEST_INVALID",
            &format!("assets.{role} 的路径与媒体类型不匹配。"),
        );
    }
    let (max_bytes, max_dimension) = match role {
        "background" => (MAX_BACKGROUND, 8192),
        "preview" => (MAX_PREVIEW, 2400),
        _ => (MAX_EFFECT, 4096),
    };
    if bytes > max_bytes || width > max_dimension || height > max_dimension || !hash_valid(&sha256)
    {
        return fail(
            "DSI_SIZE_LIMIT",
            &format!("assets.{role} 超出大小或尺寸限制。"),
        );
    }
    Ok(Asset {
        path,
        media_type,
        bytes,
        width,
        height,
        sha256,
    })
}

fn verify_signature(root: &Value, manifest: &Manifest) -> Result<()> {
    let public = match manifest.key_id.as_str() {
        #[cfg(test)]
        "codex-skin.sample.2026-01" => "kuf25VngYoeAC2TDJ2kPGRfKGJZvQhZrdVnQhGvQ3fM",
        "codex-skin.official.2026-01" => "PjtdEbIyuynRaE30OrFEB-k6jakgnL3Mzl6cyqZ-8xM",
        _ => {
            return fail(
                "DSI_SIGNATURE_UNTRUSTED",
                &format!("签名密钥不受信任：{}", manifest.key_id),
            );
        }
    };
    let key_bytes: [u8; 32] = base64::engine::general_purpose::URL_SAFE_NO_PAD
        .decode(public)
        .unwrap()
        .try_into()
        .unwrap();
    let key = VerifyingKey::from_bytes(&key_bytes)
        .map_err(|_| AppError::Message("DSI_SIGNATURE_INVALID: Ed25519 公钥无效。".into()))?;
    let mut unsigned = root.clone();
    unsigned
        .get_mut("signature")
        .and_then(Value::as_object_mut)
        .unwrap()
        .remove("value");
    let canonical = serde_jcs::to_vec(&unsigned)
        .map_err(|e| AppError::Message(format!("DSI_MANIFEST_INVALID: 无法规范化 JSON：{e}")))?;
    key.verify(&canonical, &Signature::from_bytes(&manifest.signature))
        .map_err(|_| AppError::Message("DSI_SIGNATURE_INVALID: Ed25519 签名验证失败。".into()))
}

fn validate_asset(bytes: &[u8], asset: &Asset, max_pixels: u64) -> Result<()> {
    if bytes.len() as u64 != asset.bytes || hex::encode(Sha256::digest(bytes)) != asset.sha256 {
        return fail(
            "DSI_HASH_MISMATCH",
            &format!("资源 {} 的大小或 SHA-256 不匹配。", asset.path),
        );
    }
    let magic = match asset.media_type.as_str() {
        "image/png" => bytes.starts_with(&[137, 80, 78, 71, 13, 10, 26, 10]),
        "image/jpeg" => bytes.starts_with(&[0xff, 0xd8, 0xff]),
        "image/webp" => bytes.len() >= 12 && &bytes[..4] == b"RIFF" && &bytes[8..12] == b"WEBP",
        _ => false,
    };
    if !magic {
        return fail("DSI_ASSET_INVALID", "图片文件魔数与声明类型不符。");
    }
    if asset.media_type == "image/jpeg" && (!bytes.ends_with(&[0xff, 0xd9])) {
        return fail("DSI_ASSET_INVALID", "JPEG 缺少结束标记或包含尾随载荷。");
    }
    if asset.media_type == "image/png" {
        validate_png(bytes)?;
    }
    if asset.media_type == "image/webp" && bytes.windows(4).any(|v| v == b"ANIM" || v == b"ANMF") {
        return fail("DSI_ASSET_INVALID", "不允许动画 WebP。");
    }
    let image = image::load_from_memory(bytes)
        .map_err(|e| AppError::Message(format!("DSI_ASSET_INVALID: 图片无法完整解码：{e}")))?;
    if image.width() != asset.width
        || image.height() != asset.height
        || (image.width() as u64) * (image.height() as u64) > max_pixels
    {
        return fail("DSI_ASSET_INVALID", "图片像素尺寸与清单不一致或超过限制。");
    }
    Ok(())
}
fn validate_png(bytes: &[u8]) -> Result<()> {
    let mut offset = 8;
    let mut header = false;
    let mut end = false;
    while offset + 12 <= bytes.len() {
        let length = u32::from_be_bytes(bytes[offset..offset + 4].try_into().unwrap()) as usize;
        if offset + 12 + length > bytes.len() {
            return fail("DSI_ASSET_INVALID", "PNG 包含截断区块。");
        }
        let kind = &bytes[offset + 4..offset + 8];
        if !header && kind != b"IHDR" {
            return fail("DSI_ASSET_INVALID", "PNG 首个区块不是 IHDR。");
        }
        if kind == b"IHDR" {
            if header || length != 13 {
                return fail("DSI_ASSET_INVALID", "PNG IHDR 无效。");
            }
            header = true;
        }
        if kind == b"acTL" {
            return fail("DSI_ASSET_INVALID", "不允许动画 PNG。");
        }
        offset += 12 + length;
        if kind == b"IEND" {
            if length != 0 || offset != bytes.len() {
                return fail("DSI_ASSET_INVALID", "PNG IEND 或尾随数据无效。");
            }
            end = true;
            break;
        }
    }
    if !header || !end {
        return fail("DSI_ASSET_INVALID", "PNG 缺少必要区块。");
    }
    Ok(())
}

fn closed(value: &Value, allowed: &[&str], required: &[&str], path: &str) -> Result<()> {
    let object = value
        .as_object()
        .ok_or_else(|| AppError::Message(format!("DSI_MANIFEST_INVALID: {path} 必须是对象。")))?;
    if let Some(key) = object.keys().find(|key| !allowed.contains(&key.as_str())) {
        return fail(
            "DSI_MANIFEST_INVALID",
            &format!("{path} 包含未知字段：{key}"),
        );
    }
    if let Some(key) = required.iter().find(|key| !object.contains_key(**key)) {
        return fail("DSI_MANIFEST_INVALID", &format!("{path} 缺少字段：{key}"));
    }
    Ok(())
}
fn property<'a>(value: &'a Value, key: &str) -> Result<&'a Value> {
    value
        .get(key)
        .ok_or_else(|| AppError::Message(format!("DSI_MANIFEST_INVALID: 缺少字段 {key}。")))
}
fn string<'a>(value: &'a Value, path: &str) -> Result<&'a str> {
    value
        .as_str()
        .ok_or_else(|| AppError::Message(format!("DSI_MANIFEST_INVALID: {path} 必须是字符串。")))
}
fn text(value: &Value, key: &str, max: usize) -> Result<String> {
    let value = string(property(value, key)?, key)?;
    if value.is_empty()
        || value.chars().count() > max
        || value
            .chars()
            .any(|c| c == '\0' || c.is_control() && c != '\n' && c != '\r' && c != '\t')
    {
        return fail("DSI_MANIFEST_INVALID", &format!("{key} 文本无效。"));
    }
    Ok(value.to_owned())
}
fn integer(value: &Value, key: &str) -> Result<i64> {
    property(value, key)?
        .as_i64()
        .ok_or_else(|| AppError::Message(format!("DSI_MANIFEST_INVALID: {key} 必须是整数。")))
}
fn positive(value: &Value, key: &str) -> Result<u64> {
    property(value, key)?
        .as_u64()
        .filter(|v| *v > 0)
        .ok_or_else(|| AppError::Message(format!("DSI_MANIFEST_INVALID: {key} 必须是正整数。")))
}
fn semver(value: &Value, key: &str) -> Result<String> {
    let value = text(value, key, 64)?;
    semver::Version::parse(&value)
        .map_err(|_| AppError::Message(format!("DSI_MANIFEST_INVALID: {key} 不是有效 SemVer。")))?;
    Ok(value)
}
fn valid_id(v: &str) -> bool {
    v.chars()
        .all(|c| c.is_ascii_lowercase() || c.is_ascii_digit() || c == '.' || c == '-')
        && !v.starts_with(['.', '-'])
        && !v.ends_with(['.', '-'])
        && !v.contains("..")
        && !v.contains("--")
        && !v.contains(".-")
        && !v.contains("-.")
}
fn valid_color(v: &str) -> bool {
    (v.len() == 7 && v.starts_with('#') && v[1..].chars().all(|c| c.is_ascii_hexdigit()))
        || regex::Regex::new(
            r"^rgba\((25[0-5]|2[0-4][0-9]|1[0-9]{2}|[1-9]?[0-9]),[ ]*(25[0-5]|2[0-4][0-9]|1[0-9]{2}|[1-9]?[0-9]),[ ]*(25[0-5]|2[0-4][0-9]|1[0-9]{2}|[1-9]?[0-9]),[ ]*(0|1|0?\.[0-9]{1,3}|1\.0{1,3})\)$",
        )
        .unwrap()
        .is_match(v)
}
fn hash_valid(v: &str) -> bool {
    v.len() == 64
        && v.chars()
            .all(|c| c.is_ascii_hexdigit() && !c.is_ascii_uppercase())
}
fn hash_file(path: &Path) -> Result<String> {
    let mut file = File::open(path)?;
    let mut hasher = Sha256::new();
    let mut buffer = [0u8; 81920];
    loop {
        let count = file.read(&mut buffer)?;
        if count == 0 {
            break;
        }
        hasher.update(&buffer[..count]);
    }
    Ok(hex::encode(hasher.finalize()))
}
fn fail<T>(code: &str, message: &str) -> Result<T> {
    Err(AppError::Message(format!("{code}: {message}")))
}

#[derive(Debug)]
struct StrictValue(Value);
impl<'de> Deserialize<'de> for StrictValue {
    fn deserialize<D: Deserializer<'de>>(deserializer: D) -> std::result::Result<Self, D::Error> {
        deserializer.deserialize_any(StrictVisitor).map(StrictValue)
    }
}
struct StrictVisitor;
impl<'de> Visitor<'de> for StrictVisitor {
    type Value = Value;
    fn expecting(&self, f: &mut std::fmt::Formatter) -> std::fmt::Result {
        write!(f, "valid JSON")
    }
    fn visit_bool<E: serde::de::Error>(self, v: bool) -> std::result::Result<Value, E> {
        Ok(Value::Bool(v))
    }
    fn visit_i64<E: serde::de::Error>(self, v: i64) -> std::result::Result<Value, E> {
        Ok(v.into())
    }
    fn visit_u64<E: serde::de::Error>(self, v: u64) -> std::result::Result<Value, E> {
        Ok(v.into())
    }
    fn visit_f64<E: serde::de::Error>(self, _: f64) -> std::result::Result<Value, E> {
        Err(E::custom("协议 v1 只允许整数"))
    }
    fn visit_str<E: serde::de::Error>(self, v: &str) -> std::result::Result<Value, E> {
        Ok(v.into())
    }
    fn visit_string<E: serde::de::Error>(self, v: String) -> std::result::Result<Value, E> {
        Ok(v.into())
    }
    fn visit_none<E: serde::de::Error>(self) -> std::result::Result<Value, E> {
        Ok(Value::Null)
    }
    fn visit_unit<E: serde::de::Error>(self) -> std::result::Result<Value, E> {
        Ok(Value::Null)
    }
    fn visit_seq<A: SeqAccess<'de>>(self, mut seq: A) -> std::result::Result<Value, A::Error> {
        let mut values = Vec::new();
        while let Some(value) = seq.next_element::<StrictValue>()? {
            values.push(value.0);
        }
        Ok(Value::Array(values))
    }
    fn visit_map<A: MapAccess<'de>>(self, mut map: A) -> std::result::Result<Value, A::Error> {
        let mut values = BTreeMap::new();
        while let Some((key, value)) = map.next_entry::<String, StrictValue>()? {
            if values.insert(key.clone(), value.0).is_some() {
                return Err(serde::de::Error::custom(format!("重复键：{key}")));
            }
        }
        Ok(Value::Object(values.into_iter().collect::<Map<_, _>>()))
    }
}
fn parse_strict(text: &str) -> Result<Value> {
    let mut deserializer = serde_json::Deserializer::from_str(text);
    let value = StrictValue::deserialize(&mut deserializer)
        .map_err(|e| AppError::Message(format!("DSI_MANIFEST_INVALID: theme.json 解析失败：{e}")))?
        .0;
    deserializer.end().map_err(|e| {
        AppError::Message(format!(
            "DSI_MANIFEST_INVALID: theme.json 包含尾随内容：{e}"
        ))
    })?;
    if depth(&value) > 32 {
        return fail("DSI_MANIFEST_INVALID", "theme.json 嵌套超过 32 层。");
    }
    Ok(value)
}
fn depth(v: &Value) -> usize {
    match v {
        Value::Array(v) => 1 + v.iter().map(depth).max().unwrap_or(0),
        Value::Object(v) => 1 + v.values().map(depth).max().unwrap_or(0),
        _ => 1,
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    use std::io::Write;

    fn fixture_path() -> PathBuf {
        PathBuf::from(env!("CARGO_MANIFEST_DIR"))
            .parent()
            .unwrap()
            .join("samples/dreamskin/codex-skin-sample-1.0.0.dreamskin")
    }

    fn write_package(entries: &[(String, Vec<u8>)]) -> tempfile::NamedTempFile {
        let output = tempfile::Builder::new()
            .suffix(".dreamskin")
            .tempfile()
            .unwrap();
        let mut zip = zip::ZipWriter::new(output.reopen().unwrap());
        let options = zip::write::SimpleFileOptions::default()
            .compression_method(zip::CompressionMethod::Deflated)
            .unix_permissions(0o644);
        for (name, bytes) in entries {
            zip.start_file(name, options).unwrap();
            zip.write_all(bytes).unwrap();
        }
        zip.finish().unwrap();
        output
    }

    fn fixture_entries() -> Vec<(String, Vec<u8>)> {
        let mut archive = zip::ZipArchive::new(File::open(fixture_path()).unwrap()).unwrap();
        (0..archive.len())
            .map(|index| {
                let mut entry = archive.by_index(index).unwrap();
                let mut bytes = Vec::new();
                entry.read_to_end(&mut bytes).unwrap();
                (entry.name().to_owned(), bytes)
            })
            .collect()
    }

    #[test]
    fn rejects_duplicate_json_keys_and_invalid_colors() {
        assert!(parse_strict(r#"{"a":1,"a":2}"#).is_err());
        assert!(!valid_color("rgba(999, 0, 0, 1)"));
        assert!(valid_color("rgba(255, 0, 42, 0.125)"));
    }

    #[test]
    fn accepts_only_declarative_effect_triggers() {
        let assets = serde_json::json!({"effectOverlay": {}});
        let effects = serde_json::json!({
            "ambient": "storm",
            "intensity": "balanced",
            "overlay": {
                "image": "effect-overlay.png",
                "triggers": ["task-start", "message-send"],
                "position": {"x": 72, "y": 28},
                "widthPercent": 42
            }
        });
        validate_package_effects(Some(&effects), &assets).unwrap();
        let unsafe_effects = serde_json::json!({
            "overlay": {"image": "effect-overlay.png", "triggers": ["click-anywhere"]}
        });
        assert!(validate_package_effects(Some(&unsafe_effects), &assets).is_err());
    }

    #[test]
    fn fixture_import_verifies_signature_and_assets() {
        let package = fixture_path();
        let temp = tempfile::tempdir().unwrap();
        unsafe {
            std::env::set_var("CODEX_THEME_LIBRARY_DIR", temp.path());
            std::env::set_var("CODEX_THEME_STORE_DATA_DIR", temp.path());
        }
        let result = import_local(package.to_str().unwrap()).unwrap();
        assert_eq!(result.id, "codex-skin.jackson-sage-sample");
        assert!(Path::new(&result.install_path).join("theme.json").is_file());
        let catalog = crate::catalog::load().unwrap();
        assert!(catalog.iter().any(|theme| theme.id == result.id));
        let payload = crate::compiler::compile(&result.id).unwrap();
        assert!(payload.css.contains("--codex-theme-id"));
        assert!(payload.javascript.contains("codex-theme-native-home"));
        assert!(!payload.javascript.contains("id='codex-theme-home'"));
        let state = temp.path().join("State");
        assert_eq!(
            fs::read_to_string(state.join("codex-theme.css")).unwrap(),
            payload.css
        );
        assert_eq!(
            fs::read_to_string(state.join("codex-theme.js")).unwrap(),
            payload.javascript
        );
        assert!(
            fs::read_to_string(state.join("current-theme.json"))
                .unwrap()
                .contains("codex-skin.jackson-sage-sample")
        );
        unsafe {
            std::env::remove_var("CODEX_THEME_LIBRARY_DIR");
            std::env::remove_var("CODEX_THEME_STORE_DATA_DIR");
        }
    }

    #[test]
    fn rejects_tampered_signed_manifest() {
        let mut entries = fixture_entries();
        let manifest = entries
            .iter_mut()
            .find(|(name, _)| name == "theme.json")
            .unwrap();
        let mut root: Value = serde_json::from_slice(&manifest.1).unwrap();
        root["name"] = Value::String("Tampered theme".into());
        manifest.1 = serde_json::to_vec(&root).unwrap();
        let package = write_package(&entries);
        let error = verify_for_platform(package.path().to_str().unwrap(), "windows").unwrap_err();
        assert!(error.to_string().contains("DSI_SIGNATURE_INVALID"));
    }

    #[test]
    fn rejects_zip_traversal_and_duplicate_names() {
        for entries in [
            vec![
                ("../theme.json".into(), vec![1]),
                ("background.png".into(), vec![2]),
                ("preview.png".into(), vec![3]),
            ],
            vec![
                ("theme.json".into(), vec![1]),
                ("THEME.JSON".into(), vec![2]),
                ("preview.png".into(), vec![3]),
            ],
        ] {
            let package = write_package(&entries);
            let mut archive = zip::ZipArchive::new(File::open(package.path()).unwrap()).unwrap();
            assert!(validate_entries(&mut archive).is_err());
        }
    }
}
