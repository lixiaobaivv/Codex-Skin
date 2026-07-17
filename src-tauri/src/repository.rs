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

fn allowed(relative: &str) -> bool {
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

fn validate_repository(root: &Path) -> Result<()> {
    for schema in ["theme-v1.schema.json", "theme-repository-v1.schema.json"] {
        if !root.join("schemas").join(schema).is_file() {
            return Err(AppError::Message(format!("主题仓库缺少 Schema：{schema}")));
        }
    }
    let index: Value = serde_json::from_slice(&fs::read(root.join("theme-repository.json"))?)?;
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
    for entry in entries {
        let id = entry.get("id").and_then(Value::as_str).unwrap_or("");
        let manifest = entry.get("manifest").and_then(Value::as_str).unwrap_or("");
        if !ids.insert(id)
            || manifest != format!("themes/{id}.json")
            || !root.join(manifest).is_file()
        {
            return Err(AppError::Message(format!("主题索引条目无效：{id}")));
        }
    }
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
