use crate::{
    error::{AppError, Result},
    models::ThemeSummary,
    paths,
};
use serde_json::Value;
use std::{
    collections::HashMap,
    fs,
    path::{Path, PathBuf},
};

const CATEGORIES: [&str; 7] = ["人物", "动漫", "游戏", "风景", "极简", "节日", "其他"];

pub fn load() -> Result<Vec<ThemeSummary>> {
    let mut newest = HashMap::<String, ThemeSummary>::new();
    if let Ok(directory) = paths::cache_themes() {
        load_directory(&directory, &mut newest)?;
    }
    if let Ok(root) = paths::installed_root()
        && root.exists()
    {
        for entry in walkdir::WalkDir::new(root)
            .min_depth(3)
            .max_depth(3)
            .into_iter()
            .filter_map(|item| item.ok())
        {
            if entry.file_type().is_file() && entry.file_name() == "theme.json" {
                let _ = add_manifest(entry.path(), &mut newest);
            }
        }
    }
    let mut themes: Vec<_> = newest.into_values().collect();
    themes.sort_by(|a, b| {
        a.category
            .cmp(&b.category)
            .then_with(|| a.name.cmp(&b.name))
    });
    Ok(themes)
}

fn load_directory(directory: &Path, themes: &mut HashMap<String, ThemeSummary>) -> Result<()> {
    if !directory.exists() {
        return Ok(());
    }
    for item in fs::read_dir(directory)? {
        let path = item?.path();
        if path
            .extension()
            .is_some_and(|ext| ext.eq_ignore_ascii_case("json"))
        {
            add_manifest(&path, themes)?;
        }
    }
    Ok(())
}

fn add_manifest(path: &Path, themes: &mut HashMap<String, ThemeSummary>) -> Result<()> {
    let root: Value = serde_json::from_slice(&fs::read(path)?)?;
    let text = |key: &str| {
        root.get(key)
            .and_then(Value::as_str)
            .unwrap_or_default()
            .to_owned()
    };
    if root.get("schemaVersion").and_then(Value::as_u64) != Some(1) {
        return Err(AppError::Message(format!(
            "只支持主题标准 v1：{}",
            path.display()
        )));
    }
    let package = root.get("packageVersion").and_then(Value::as_u64) == Some(1);
    let id = if package {
        text("id")
    } else {
        text("codeThemeId")
    };
    let version = text("version");
    let parsed_version = semver::Version::parse(&version)
        .map_err(|_| AppError::Message(format!("主题版本无效：{version}")))?;
    let category = if package {
        "其他".to_owned()
    } else {
        text("category")
    };
    if id.len() < 2
        || !id.chars().all(|c| {
            c.is_ascii_lowercase() || c.is_ascii_digit() || c == '-' || (package && c == '.')
        })
    {
        return Err(AppError::Message(format!("主题 ID 无效：{id}")));
    }
    if !CATEGORIES.contains(&category.as_str()) {
        return Err(AppError::Message(format!("主题分类无效：{category}")));
    }
    let preview = if package {
        root.get("assets")
            .and_then(|v| v.get("preview"))
            .and_then(|v| v.get("path"))
            .and_then(Value::as_str)
            .unwrap_or_default()
            .to_owned()
    } else {
        text("previewImage")
    };
    let preview_path = if package {
        canonical_package_asset(path, &preview)?
    } else {
        canonical_asset(path, &preview, "previews")?
    };
    let name = if package {
        text("name")
    } else {
        text("displayName")
    };
    let candidate = ThemeSummary {
        id: id.clone(),
        version: version.clone(),
        name,
        description: text("description"),
        category,
        preview_path: preview_path.to_string_lossy().into_owned(),
        manifest_path: path.to_owned(),
    };
    let replace = themes
        .get(&id)
        .map(|old| {
            parsed_version
                > semver::Version::parse(&old.version)
                    .expect("catalog only stores validated semantic versions")
        })
        .unwrap_or(true);
    if replace {
        themes.insert(id, candidate);
    }
    Ok(())
}

fn canonical_package_asset(manifest: &Path, relative: &str) -> Result<PathBuf> {
    if relative.is_empty()
        || relative.contains('/')
        || relative.contains('\\')
        || Path::new(relative).file_name().and_then(|v| v.to_str()) != Some(relative)
    {
        return Err(AppError::Message(format!(
            "签名主题资源路径无效：{relative}"
        )));
    }
    let path = manifest
        .parent()
        .unwrap_or(Path::new("."))
        .join(relative)
        .canonicalize()?;
    if !path.is_file() {
        return Err(AppError::Message(format!(
            "签名主题资源不存在：{}",
            path.display()
        )));
    }
    Ok(path)
}

fn canonical_asset(manifest: &Path, relative: &str, expected_dir: &str) -> Result<PathBuf> {
    let normalized = relative.replace('\\', "/");
    if !normalized.starts_with(&format!("../{expected_dir}/")) || normalized.contains("/../") {
        return Err(AppError::Message(format!("主题资源路径无效：{relative}")));
    }
    let path = manifest
        .parent()
        .unwrap_or(Path::new("."))
        .join(relative)
        .canonicalize()?;
    if !path.is_file() {
        return Err(AppError::Message(format!(
            "主题资源不存在：{}",
            path.display()
        )));
    }
    Ok(path)
}

pub fn find(id: &str) -> Result<ThemeSummary> {
    load()?
        .into_iter()
        .find(|theme| theme.id == id)
        .ok_or_else(|| AppError::Message(format!("找不到主题：{id}")))
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn selects_newest_semantic_version() {
        let temp = tempfile::tempdir().unwrap();
        let mut themes = HashMap::new();
        for version in ["1.9.0", "1.10.0", "1.2.0"] {
            let directory = temp.path().join(version);
            fs::create_dir_all(&directory).unwrap();
            fs::write(directory.join("preview.png"), b"preview").unwrap();
            let manifest = directory.join("theme.json");
            fs::write(
                &manifest,
                serde_json::to_vec(&serde_json::json!({
                    "schemaVersion": 1,
                    "packageVersion": 1,
                    "id": "codex-skin.version-test",
                    "version": version,
                    "name": "Version test",
                    "description": "test",
                    "assets": { "preview": { "path": "preview.png" } }
                }))
                .unwrap(),
            )
            .unwrap();
            add_manifest(&manifest, &mut themes).unwrap();
        }
        assert_eq!(themes["codex-skin.version-test"].version, "1.10.0");
    }
}
