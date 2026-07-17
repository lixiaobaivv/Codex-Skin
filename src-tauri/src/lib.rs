mod catalog;
mod error;
mod models;
mod paths;
mod repository;

use base64::Engine;
use models::{AppState, SyncResult, SOURCES};
use tauri::{Emitter, Manager};

#[tauri::command]
fn get_app_state() -> error::Result<AppState> {
    let settings = repository::load_settings();
    Ok(AppState { themes: catalog::load()?, sources: SOURCES.to_vec(), selected_source_id: settings.source_id })
}

#[tauri::command]
async fn sync_catalog(source_id: String) -> error::Result<SyncResult> { repository::sync(&source_id).await }

#[tauri::command]
fn read_preview(path: String) -> error::Result<String> {
    let requested = std::path::PathBuf::from(&path).canonicalize()?;
    let allowed = [paths::cache_root()?, paths::installed_root()?].into_iter().filter_map(|root| root.canonicalize().ok()).any(|root| requested.starts_with(root));
    if !allowed { return Err(error::AppError::Message("拒绝读取主题目录之外的图片。".into())); }
    let mime = match requested.extension().and_then(|value| value.to_str()).unwrap_or("").to_ascii_lowercase().as_str() { "png" => "image/png", "jpg" | "jpeg" => "image/jpeg", "webp" => "image/webp", "avif" => "image/avif", _ => return Err(error::AppError::Message("不支持的预览图片格式。".into())) };
    let bytes = std::fs::read(requested)?;
    if bytes.len() > 20 * 1024 * 1024 { return Err(error::AppError::Message("预览图片超过 20 MB 限制。".into())); }
    Ok(format!("data:{mime};base64,{}", base64::engine::general_purpose::STANDARD.encode(bytes)))
}

#[tauri::command]
fn apply_theme(theme_id: String) -> error::Result<String> { let theme = catalog::find(&theme_id)?; Err(error::AppError::Message(format!("主题编译与 CDP 注入正在迁移：{}", theme.name))) }
#[tauri::command]
fn restart_and_apply(theme_id: String) -> error::Result<String> { let _ = catalog::find(&theme_id)?; Err(error::AppError::Message("Codex 平台适配器正在迁移。".into())) }
#[tauri::command]
fn rollback_theme() -> error::Result<String> { Err(error::AppError::Message("CDP 回滚正在迁移。".into())) }

pub fn run() {
    tauri::Builder::default()
        .plugin(tauri_plugin_deep_link::init())
        .plugin(tauri_plugin_single_instance::init(|app, argv, _| {
            if let Some(window) = app.get_webview_window("main") { let _ = window.show(); let _ = window.set_focus(); }
            let _ = app.emit("external-activation", argv);
        }))
        .invoke_handler(tauri::generate_handler![get_app_state, sync_catalog, read_preview, apply_theme, restart_and_apply, rollback_theme])
        .run(tauri::generate_context!()).expect("failed to run Codex-Skin");
}
