use crate::{
    error::{AppError, Result},
    repository,
};
use serde_json::{Value, json};
use std::{
    fs,
    io::{Read, Write},
    path::Path,
};

pub fn validate(root: &str) -> Result<usize> {
    let root = Path::new(root).canonicalize()?;
    repository::validate_repository(&root)?;
    let index: Value = serde_json::from_slice(&fs::read(root.join("theme-repository.json"))?)?;
    Ok(index
        .get("themes")
        .and_then(Value::as_array)
        .map(Vec::len)
        .unwrap_or(0))
}

pub fn index(root: &str, name: Option<&str>) -> Result<usize> {
    let root = Path::new(root).canonicalize()?;
    let themes = root.join("themes");
    if !themes.is_dir() {
        return Err(AppError::Message("缺少 themes 目录。".into()));
    }
    let mut entries = Vec::new();
    for item in fs::read_dir(themes)? {
        let path = item?.path();
        if path.extension().and_then(|v| v.to_str()) != Some("json") {
            continue;
        }
        let value: Value = serde_json::from_slice(&fs::read(&path)?)?;
        let id = value
            .get("codeThemeId")
            .and_then(Value::as_str)
            .ok_or_else(|| {
                AppError::Message(format!("主题缺少 codeThemeId：{}", path.display()))
            })?;
        if path.file_stem().and_then(|v| v.to_str()) != Some(id) {
            return Err(AppError::Message(format!("主题文件名必须与 ID 一致：{id}")));
        }
        entries.push(json!({"id":id,"manifest":format!("themes/{id}.json")}));
    }
    entries.sort_by(|a, b| a["id"].as_str().cmp(&b["id"].as_str()));
    if entries.is_empty() {
        return Err(AppError::Message("themes 目录没有主题。".into()));
    }
    let index = json!({"$schema":"./schemas/theme-repository-v1.schema.json","schemaVersion":1,"name":name.unwrap_or("Codex-Skin Theme Repository"),"updatedAt":chrono::Utc::now().format("%Y-%m-%dT%H:%M:%SZ").to_string(),"themes":entries});
    atomicwrites::AtomicFile::new(
        root.join("theme-repository.json"),
        atomicwrites::AllowOverwrite,
    )
    .write(|file| file.write_all(serde_json::to_string_pretty(&index).unwrap().as_bytes()))
    .map_err(|error| AppError::Message(format!("写入主题索引失败：{error}")))?;
    repository::validate_repository(&root)?;
    Ok(index["themes"].as_array().unwrap().len())
}

pub fn pack(root: &str, output: &str) -> Result<usize> {
    let root = Path::new(root).canonicalize()?;
    repository::validate_repository(&root)?;
    let output = Path::new(output);
    if let Some(parent) = output.parent() {
        fs::create_dir_all(parent)?;
    }
    let temporary = output.with_extension("tmp");
    let result = (|| {
        let file = fs::File::create(&temporary)?;
        let mut zip = zip::ZipWriter::new(file);
        let options = zip::write::SimpleFileOptions::default()
            .compression_method(zip::CompressionMethod::Deflated)
            .unix_permissions(0o644);
        let mut files = Vec::new();
        for entry in walkdir::WalkDir::new(&root) {
            let entry = entry.map_err(|e| AppError::Message(e.to_string()))?;
            if !entry.file_type().is_file() {
                continue;
            }
            let relative = entry
                .path()
                .strip_prefix(&root)
                .unwrap()
                .to_string_lossy()
                .replace('\\', "/");
            if repository::allowed(&relative) {
                files.push((relative, entry.path().to_owned()));
            }
        }
        files.sort_by(|a, b| a.0.cmp(&b.0));
        for (relative, path) in &files {
            zip.start_file(relative, options)?;
            let mut source = fs::File::open(path)?;
            let mut buffer = [0u8; 81920];
            loop {
                let count = source.read(&mut buffer)?;
                if count == 0 {
                    break;
                }
                zip.write_all(&buffer[..count])?;
            }
        }
        zip.finish()?;
        Ok::<usize, AppError>(files.len())
    })();
    match result {
        Ok(count) => {
            if output.exists() {
                fs::remove_file(output)?;
            }
            fs::rename(temporary, output)?;
            Ok(count)
        }
        Err(error) => {
            let _ = fs::remove_file(temporary);
            Err(error)
        }
    }
}
