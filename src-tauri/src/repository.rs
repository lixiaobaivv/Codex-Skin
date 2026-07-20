use crate::{
    catalog,
    error::{AppError, Result},
    models::{RepositorySettings, SOURCES, SyncResult},
    paths,
};
use futures_util::StreamExt;
use serde_json::Value;
use std::{
    collections::HashSet,
    fs::{self, File},
    io::Write,
    path::Path,
};
use tempfile::TempDir;

const MAX_ARCHIVE: u64 = 200 * 1024 * 1024;
const MAX_FILE: u64 = 20 * 1024 * 1024;
const MAX_FILES: usize = 2000;

pub fn load_settings() -> RepositorySettings {
    paths::settings_path()
        .ok()
        .and_then(|path| fs::read(path).ok())
        .and_then(|bytes| serde_json::from_slice(&bytes).ok())
        .unwrap_or_default()
}

pub async fn sync(preferred: &str) -> Result<SyncResult> {
    let preferred = SOURCES
        .iter()
        .find(|source| source.id == preferred)
        .unwrap_or(&SOURCES[0]);
    let mut candidates = vec![preferred];
    for source in &SOURCES {
        if !candidates.iter().any(|item| item.id == source.id) {
            candidates.push(source);
        }
    }
    let upstream = "https://github.com/lixiaobaivv/Codex-Skin-Store/archive/refs/heads/main.zip";
    let mut last_error = None;
    for source in candidates {
        match sync_from(&format!("{}{upstream}", source.prefix)).await {
            Ok(count) => {
                let settings = serde_json::json!({"sourceId": source.id});
                let settings_path = paths::settings_path()?;
                fs::create_dir_all(settings_path.parent().unwrap())?;
                fs::write(settings_path, serde_json::to_vec_pretty(&settings)?)?;
                return Ok(SyncResult {
                    theme_count: count,
                    source_id: source.id.into(),
                    source_name: source.name.into(),
                });
            }
            Err(error) => last_error = Some(error),
        }
    }
    Err(AppError::Message(format!(
        "所有主题同步线路均失败：{}",
        last_error.map(|e| e.to_string()).unwrap_or_default()
    )))
}

async fn sync_from(url: &str) -> Result<usize> {
    let temporary = TempDir::new()?;
    let archive_path = temporary.path().join("catalog.zip");
    let response = reqwest::Client::builder()
        .timeout(std::time::Duration::from_secs(45))
        .build()?
        .get(url)
        .header("User-Agent", "Codex-Skin/2")
        .send()
        .await?
        .error_for_status()?;
    if response
        .content_length()
        .is_some_and(|value| value > MAX_ARCHIVE)
    {
        return Err(AppError::Message("主题仓库归档超过 200 MB 限制。".into()));
    }
    let mut file = File::create(&archive_path)?;
    let mut total = 0u64;
    let mut stream = response.bytes_stream();
    while let Some(chunk) = stream.next().await {
        let chunk = chunk?;
        total += chunk.len() as u64;
        if total > MAX_ARCHIVE {
            return Err(AppError::Message("主题仓库归档超过 200 MB 限制。".into()));
        }
        file.write_all(&chunk)?;
    }
    let extracted = temporary.path().join("extracted");
    fs::create_dir(&extracted)?;
    extract(&archive_path, &extracted)?;
    validate_repository(&extracted)?;
    let themes_dir = extracted.join("themes");
    let count = fs::read_dir(&themes_dir)?
        .filter_map(|item| item.ok())
        .filter(|item| item.path().extension().is_some_and(|ext| ext == "json"))
        .count();
    replace_cache(&extracted)?;
    Ok(count)
}

pub(crate) fn allowed(relative: &str) -> bool {
    if relative == "theme-repository.json" {
        return true;
    }
    let Some((directory, name)) = relative.split_once('/') else {
        return false;
    };
    if name.contains('/') {
        return false;
    }
    let ext = Path::new(name)
        .extension()
        .and_then(|v| v.to_str())
        .unwrap_or("")
        .to_ascii_lowercase();
    match directory {
        "themes" | "schemas" => ext == "json",
        "backgrounds" | "logos" | "previews" | "pets" => {
            ["jpg", "jpeg", "png", "webp", "avif"].contains(&ext.as_str())
        }
        _ => false,
    }
}

fn extract(archive_path: &Path, destination: &Path) -> Result<()> {
    let mut archive = zip::ZipArchive::new(File::open(archive_path)?)?;
    if archive.len() > MAX_FILES {
        return Err(AppError::Message("主题仓库文件数超过限制。".into()));
    }
    let root_name = (0..archive.len())
        .find_map(|index| {
            let name = archive.by_index(index).ok()?.name().replace('\\', "/");
            name.strip_suffix("theme-repository.json")
                .map(str::to_owned)
        })
        .ok_or_else(|| AppError::Message("归档缺少 theme-repository.json。".into()))?;
    let mut seen = HashSet::new();
    let mut extracted_total = 0u64;
    let mut extracted_count = 0usize;
    for index in 0..archive.len() {
        let mut entry = archive.by_index(index)?;
        let name = entry.name().replace('\\', "/");
        let Some(relative) = name.strip_prefix(&root_name) else {
            continue;
        };
        if !allowed(relative) {
            continue;
        }
        if entry.is_dir() || entry.size() > MAX_FILE {
            continue;
        }
        extracted_count += 1;
        extracted_total += entry.size();
        if extracted_count > MAX_FILES || extracted_total > MAX_ARCHIVE {
            return Err(AppError::Message("解压后的主题资源超过安全限制。".into()));
        }
        let safe = entry
            .enclosed_name()
            .ok_or_else(|| AppError::Message(format!("归档包含不安全路径：{relative}")))?;
        let prefix_path = Path::new(&root_name);
        let relative_path = safe
            .strip_prefix(prefix_path)
            .map_err(|_| AppError::Message("归档根目录无效。".into()))?;
        let output = destination.join(relative_path);
        if !seen.insert(output.clone()) {
            return Err(AppError::Message(format!("归档包含重复路径：{relative}")));
        }
        fs::create_dir_all(output.parent().unwrap())?;
        let mut target = File::create(output)?;
        std::io::copy(&mut entry, &mut target)?;
    }
    Ok(())
}

pub(crate) fn validate_repository(root: &Path) -> Result<()> {
    for schema in ["theme-v1.schema.json", "theme-repository-v1.schema.json"] {
        if !root.join("schemas").join(schema).is_file() {
            return Err(AppError::Message(format!("主题仓库缺少 Schema：{schema}")));
        }
    }
    let index: Value = serde_json::from_slice(&fs::read(root.join("theme-repository.json"))?)?;
    closed_object(
        &index,
        &["$schema", "schemaVersion", "name", "updatedAt", "themes"],
        &["schemaVersion", "name", "updatedAt", "themes"],
        "theme-repository.json",
    )?;
    if index.get("schemaVersion").and_then(Value::as_u64) != Some(1) {
        return Err(AppError::Message("只支持主题仓库标准 v1。".into()));
    }
    let entries = index
        .get("themes")
        .and_then(Value::as_array)
        .ok_or_else(|| AppError::Message("主题仓库 themes 无效。".into()))?;
    if entries.is_empty() || entries.len() > 500 {
        return Err(AppError::Message("主题数量必须在 1 到 500 之间。".into()));
    }
    let mut ids = HashSet::new();
    let mut manifests = HashSet::new();
    for entry in entries {
        closed_object(entry, &["id", "manifest"], &["id", "manifest"], "themes[]")?;
        let id = entry.get("id").and_then(Value::as_str).unwrap_or("");
        let manifest = entry.get("manifest").and_then(Value::as_str).unwrap_or("");
        if !valid_catalog_id(id)
            || !ids.insert(id)
            || !manifests.insert(manifest.to_owned())
            || manifest != format!("themes/{id}.json")
            || !root.join(manifest).is_file()
        {
            return Err(AppError::Message(format!("主题索引条目无效：{id}")));
        }
        validate_theme(&root.join(manifest), id, root)?;
    }
    let actual: HashSet<_> = fs::read_dir(root.join("themes"))?
        .filter_map(|entry| entry.ok())
        .filter(|entry| {
            entry
                .path()
                .extension()
                .is_some_and(|ext| ext.eq_ignore_ascii_case("json"))
        })
        .map(|entry| format!("themes/{}", entry.file_name().to_string_lossy()))
        .collect();
    if actual != manifests {
        return Err(AppError::Message("主题索引与 themes 目录不一致。".into()));
    }
    Ok(())
}

fn validate_theme(path: &Path, expected_id: &str, repository: &Path) -> Result<()> {
    let root: Value = serde_json::from_slice(&fs::read(path)?)?;
    closed_object(
        &root,
        &[
            "$schema",
            "schemaVersion",
            "version",
            "displayName",
            "codeThemeId",
            "category",
            "description",
            "author",
            "variant",
            "previewImage",
            "theme",
            "home",
            "copy",
        ],
        &[
            "schemaVersion",
            "version",
            "displayName",
            "codeThemeId",
            "category",
            "description",
            "author",
            "variant",
            "previewImage",
            "theme",
            "home",
        ],
        "theme",
    )?;
    if root.get("schemaVersion").and_then(Value::as_u64) != Some(1) {
        return Err(AppError::Message(format!(
            "远程主题必须使用 schemaVersion 1：{expected_id}"
        )));
    }
    let id = required_text(&root, "codeThemeId", 64)?;
    if id != expected_id || !valid_catalog_id(id) {
        return Err(AppError::Message(format!(
            "主题 ID 与索引不一致：{expected_id}"
        )));
    }
    let version = required_text(&root, "version", 64)?;
    if semver::Version::parse(version).is_err() {
        return Err(AppError::Message(format!("主题版本无效：{expected_id}")));
    }
    required_text(&root, "displayName", 60)?;
    required_text(&root, "description", 120)?;
    required_text(&root, "author", 60)?;
    if !["人物", "动漫", "游戏", "风景", "极简", "节日", "其他"]
        .contains(&required_text(&root, "category", 8)?)
    {
        return Err(AppError::Message(format!("主题分类无效：{expected_id}")));
    }
    if !["light", "dark"].contains(&required_text(&root, "variant", 8)?) {
        return Err(AppError::Message(format!("主题模式无效：{expected_id}")));
    }
    let theme = root
        .get("theme")
        .ok_or_else(|| AppError::Message("主题缺少 theme。".into()))?;
    closed_object(
        theme,
        &[
            "accent",
            "contrast",
            "fonts",
            "ink",
            "opaqueWindows",
            "semanticColors",
            "surface",
            "backgroundImage",
            "logoImage",
            "backgroundImageOpacity",
            "backgroundImageBlur",
        ],
        &["accent", "ink", "surface"],
        "theme.theme",
    )?;
    for key in ["accent", "ink", "surface"] {
        validate_color(required_text(theme, key, 7)?, key)?;
    }
    if let Some(fonts) = theme.get("fonts") {
        closed_object(fonts, &["ui", "code", "display"], &[], "theme.fonts")?;
        for key in ["ui", "code", "display"] {
            if let Some(value) = fonts.get(key) {
                validate_font_stack(
                    value
                        .as_str()
                        .ok_or_else(|| AppError::Message(format!("theme.fonts.{key} 必须是字符串。")))?,
                )?;
            }
        }
    }
    if let Some(semantic) = theme.get("semanticColors") {
        closed_object(
            semantic,
            &["diffAdded", "diffRemoved", "skill"],
            &[],
            "semanticColors",
        )?;
        for key in ["diffAdded", "diffRemoved", "skill"] {
            if let Some(value) = semantic.get(key).and_then(Value::as_str) {
                validate_color(value, key)?;
            }
        }
    }
    if let Some(value) = theme.get("contrast").and_then(Value::as_f64)
        && !(0.0..=100.0).contains(&value)
    {
        return Err(AppError::Message("theme.contrast 越界。".into()));
    }
    if let Some(value) = theme.get("backgroundImageOpacity").and_then(Value::as_f64)
        && !(0.0..=1.0).contains(&value)
    {
        return Err(AppError::Message("背景透明度越界。".into()));
    }
    if let Some(value) = theme.get("backgroundImageBlur").and_then(Value::as_f64)
        && !(0.0..=24.0).contains(&value)
    {
        return Err(AppError::Message("背景模糊度越界。".into()));
    }
    validate_asset_path(
        repository,
        path,
        required_text(&root, "previewImage", 180)?,
        "previews",
    )?;
    for (key, directory) in [("backgroundImage", "backgrounds"), ("logoImage", "logos")] {
        if let Some(value) = theme.get(key).and_then(Value::as_str) {
            validate_asset_path(repository, path, value, directory)?;
        }
    }
    let home = root.get("home").unwrap();
    closed_object(
        home,
        &[
            "brand",
            "eyebrow",
            "badge",
            "title",
            "subtitle",
            "footerNote",
            "composerHint",
            "tags",
            "sidebarLabels",
            "quickActions",
            "pet",
        ],
        &["brand", "title", "quickActions"],
        "home",
    )?;
    required_text(home, "brand", 200)?;
    required_text(home, "title", 200)?;
    let actions = home
        .get("quickActions")
        .and_then(Value::as_array)
        .ok_or_else(|| AppError::Message("quickActions 必须是数组。".into()))?;
    if actions.len() != 4 {
        return Err(AppError::Message("quickActions 必须包含 4 项。".into()));
    }
    for action in actions {
        closed_object(
            action,
            &["icon", "title", "description", "prompt"],
            &["title", "prompt"],
            "quickActions[]",
        )?;
        required_text(action, "title", 200)?;
        required_text(action, "prompt", 1000)?;
    }
    if let Some(pet) = home.get("pet") {
        closed_object(pet, &["image", "alt", "size"], &["image"], "pet")?;
        validate_asset_path(repository, path, required_text(pet, "image", 180)?, "pets")?;
    }
    Ok(())
}

fn closed_object(value: &Value, allowed: &[&str], required: &[&str], path: &str) -> Result<()> {
    let object = value
        .as_object()
        .ok_or_else(|| AppError::Message(format!("{path} 必须是对象。")))?;
    if let Some(key) = object.keys().find(|key| !allowed.contains(&key.as_str())) {
        return Err(AppError::Message(format!("{path} 包含未知字段：{key}")));
    }
    if let Some(key) = required.iter().find(|key| !object.contains_key(**key)) {
        return Err(AppError::Message(format!("{path} 缺少字段：{key}")));
    }
    Ok(())
}
fn required_text<'a>(value: &'a Value, key: &str, max: usize) -> Result<&'a str> {
    let text = value
        .get(key)
        .and_then(Value::as_str)
        .ok_or_else(|| AppError::Message(format!("{key} 必须是字符串。")))?;
    if text.is_empty() || text.chars().count() > max {
        return Err(AppError::Message(format!("{key} 长度无效。")));
    }
    Ok(text)
}
fn valid_catalog_id(value: &str) -> bool {
    value.len() >= 2
        && value.len() <= 64
        && value
            .chars()
            .next()
            .is_some_and(|c| c.is_ascii_lowercase() || c.is_ascii_digit())
        && value
            .chars()
            .all(|c| c.is_ascii_lowercase() || c.is_ascii_digit() || c == '-')
        && !value.ends_with('-')
}
fn validate_color(value: &str, key: &str) -> Result<()> {
    if value.len() != 7
        || !value.starts_with('#')
        || !value[1..].chars().all(|c| c.is_ascii_hexdigit())
    {
        return Err(AppError::Message(format!("{key} 颜色无效。")));
    }
    Ok(())
}

fn validate_font_stack(value: &str) -> Result<()> {
    let valid = !value.is_empty()
        && value.chars().count() <= 300
        && value.split(',').all(|family| {
            let family = family.trim();
            if family.is_empty() {
                return false;
            }
            let quoted = family.len() >= 2
                && ((family.starts_with('"') && family.ends_with('"'))
                    || (family.starts_with('\'') && family.ends_with('\'')));
            let unquoted = if quoted {
                &family[1..family.len() - 1]
            } else {
                family
            };
            !unquoted.is_empty()
                && !unquoted.contains(['"', '\''])
                && unquoted
                    .chars()
                    .all(|c| c.is_alphanumeric() || c == ' ' || "._-".contains(c))
        });
    if valid {
        Ok(())
    } else {
        Err(AppError::Message("主题字体配置无效。".into()))
    }
}
fn validate_asset_path(
    repository: &Path,
    manifest: &Path,
    relative: &str,
    directory: &str,
) -> Result<()> {
    let normalized = relative.replace('\\', "/");
    if !normalized.starts_with(&format!("../{directory}/"))
        || normalized[4 + directory.len()..].contains('/')
        || normalized.contains("/../")
    {
        return Err(AppError::Message(format!("主题资源路径无效：{relative}")));
    }
    let path = manifest.parent().unwrap().join(relative).canonicalize()?;
    let root = repository.canonicalize()?;
    if !path.starts_with(&root) || !path.is_file() {
        return Err(AppError::Message(format!(
            "主题资源越界或不存在：{relative}"
        )));
    }
    let bytes = fs::read(&path)?;
    image::load_from_memory(&bytes)
        .map_err(|error| AppError::Message(format!("主题图片无法解码：{error}")))?;
    Ok(())
}

fn replace_cache(source: &Path) -> Result<()> {
    let target = paths::cache_root()?;
    let backup = target.with_extension("previous");
    if backup.exists() {
        fs::remove_dir_all(&backup)?;
    }
    if target.exists() {
        fs::rename(&target, &backup)?;
    }
    if let Err(error) = copy_tree(source, &target) {
        let _ = fs::remove_dir_all(&target);
        if backup.exists() {
            let _ = fs::rename(&backup, &target);
        }
        return Err(error);
    }
    if backup.exists() {
        fs::remove_dir_all(backup)?;
    }
    let _ = catalog::load()?;
    Ok(())
}

fn copy_tree(source: &Path, target: &Path) -> Result<()> {
    fs::create_dir_all(target)?;
    for entry in walkdir::WalkDir::new(source) {
        let entry = entry.map_err(|e| AppError::Message(e.to_string()))?;
        let relative = entry.path().strip_prefix(source).unwrap();
        let output = target.join(relative);
        if entry.file_type().is_dir() {
            fs::create_dir_all(output)?;
        } else {
            fs::copy(entry.path(), output)?;
        }
    }
    Ok(())
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn rejects_unsafe_font_stacks() {
        assert!(validate_font_stack("Inter, system-ui, sans-serif").is_ok());
        assert!(validate_font_stack("\"Microsoft YaHei UI\", sans-serif").is_ok());
        assert!(validate_font_stack("sans-serif; } body { display: none").is_err());
        assert!(validate_font_stack("url(https://example.com/font.woff)").is_err());
        assert!(validate_font_stack("\"").is_err());
    }

    #[tokio::test]
    #[ignore = "requires the public Codex-Skin-Store repository"]
    async fn syncs_and_validates_official_catalog() {
        let temporary = tempfile::tempdir().unwrap();
        unsafe {
            std::env::set_var("CODEX_THEME_STORE_DATA_DIR", temporary.path());
        }
        let result = sync("github").await.unwrap();
        assert!(result.theme_count >= 5);
        assert_eq!(catalog::load().unwrap().len(), result.theme_count);
        unsafe {
            std::env::remove_var("CODEX_THEME_STORE_DATA_DIR");
        }
    }
}
