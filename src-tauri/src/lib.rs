mod authoring;
mod catalog;
mod cdp;
mod compiler;
mod dreamskin;
mod error;
mod models;
mod paths;
mod platform;
mod protocol;
mod repository;

pub fn verify_dreamskin(path: &str, platform: &str) -> Result<(), String> {
    dreamskin::verify_for_platform(path, platform)
        .map(|_| ())
        .map_err(|error| error.to_string())
}

pub fn catalog_index(root: &str, name: Option<&str>) -> Result<usize, String> {
    authoring::index(root, name).map_err(|error| error.to_string())
}
pub fn catalog_validate(root: &str) -> Result<usize, String> {
    authoring::validate(root).map_err(|error| error.to_string())
}
pub fn catalog_pack(root: &str, output: &str) -> Result<usize, String> {
    authoring::pack(root, output).map_err(|error| error.to_string())
}

use base64::Engine;
use models::{AppState, SOURCES, SyncResult};
use tauri::{Emitter, Manager};
use tauri_plugin_deep_link::DeepLinkExt;

struct ActivationQueue(std::sync::Mutex<Vec<String>>);

#[tauri::command]
fn get_app_state() -> error::Result<AppState> {
    let settings = repository::load_settings();
    Ok(AppState {
        themes: catalog::load()?,
        sources: SOURCES.to_vec(),
        selected_source_id: settings.source_id,
    })
}

#[tauri::command]
async fn sync_catalog(source_id: String) -> error::Result<SyncResult> {
    repository::sync(&source_id).await
}

#[tauri::command]
async fn import_local(path: String) -> error::Result<dreamskin::ImportResult> {
    tauri::async_runtime::spawn_blocking(move || dreamskin::import_local(&path))
        .await
        .map_err(|error| error::AppError::Message(format!("主题导入任务失败：{error}")))?
}

#[tauri::command]
async fn install_uri(
    uri: String,
    source_id: Option<String>,
) -> error::Result<dreamskin::ImportResult> {
    protocol::install_uri(&uri, source_id.as_deref()).await
}

#[tauri::command]
fn pending_activations(queue: tauri::State<'_, ActivationQueue>) -> Vec<String> {
    std::mem::take(&mut *queue.0.lock().unwrap())
}

#[tauri::command]
fn read_preview(path: String) -> error::Result<String> {
    let requested = std::path::PathBuf::from(&path).canonicalize()?;
    let allowed = [paths::cache_root()?, paths::installed_root()?]
        .into_iter()
        .filter_map(|root| root.canonicalize().ok())
        .any(|root| requested.starts_with(root));
    if !allowed {
        return Err(error::AppError::Message(
            "拒绝读取主题目录之外的图片。".into(),
        ));
    }
    let mime = match requested
        .extension()
        .and_then(|value| value.to_str())
        .unwrap_or("")
        .to_ascii_lowercase()
        .as_str()
    {
        "png" => "image/png",
        "jpg" | "jpeg" => "image/jpeg",
        "webp" => "image/webp",
        "avif" => "image/avif",
        _ => return Err(error::AppError::Message("不支持的预览图片格式。".into())),
    };
    let bytes = std::fs::read(requested)?;
    if bytes.len() > 20 * 1024 * 1024 {
        return Err(error::AppError::Message("预览图片超过 20 MB 限制。".into()));
    }
    Ok(format!(
        "data:{mime};base64,{}",
        base64::engine::general_purpose::STANDARD.encode(bytes)
    ))
}

#[tauri::command]
async fn theme_runtime_ready() -> bool {
    cdp::is_ready().await
}

#[tauri::command]
async fn apply_theme(theme_id: String) -> error::Result<String> {
    let payload = compiler::compile(&theme_id)?;
    if cdp::inject(&payload, std::time::Duration::from_secs(15)).await? == 0 {
        return Err(error::AppError::Message(
            "Codex 未以主题模式启动，请使用“应用并重启 Codex”。".into(),
        ));
    }
    Ok(format!("{} 已应用", catalog::find(&theme_id)?.name))
}
#[tauri::command]
async fn restart_and_apply(theme_id: String) -> error::Result<String> {
    let payload = compiler::compile(&theme_id)?;
    platform::restart_and_inject(&payload, std::time::Duration::from_secs(90)).await?;
    Ok(format!("{} 已应用", catalog::find(&theme_id)?.name))
}
#[tauri::command]
async fn rollback_theme() -> error::Result<String> {
    if cdp::remove(std::time::Duration::from_secs(15)).await? == 0 {
        return Err(error::AppError::Message("未连接到 Codex 主题端口。".into()));
    }
    Ok("已恢复默认主题".into())
}

pub fn run() {
    tauri::Builder::default()
        .manage(ActivationQueue(std::sync::Mutex::new(Vec::new())))
        .plugin(tauri_plugin_deep_link::init())
        .plugin(tauri_plugin_single_instance::init(|app, argv, _| {
            if let Some(window) = app.get_webview_window("main") {
                let _ = window.show();
                let _ = window.set_focus();
            }
            app.state::<ActivationQueue>().0.lock().unwrap().extend(
                argv.iter()
                    .filter(|value| {
                        value.starts_with("dreamskin:")
                            || value.to_ascii_lowercase().ends_with(".dreamskin")
                    })
                    .cloned(),
            );
            let _ = app.emit("external-activation", argv);
        }))
        .setup(|app| {
            let initial: Vec<_> = std::env::args()
                .skip(1)
                .filter(|value| {
                    value.starts_with("dreamskin:")
                        || value.to_ascii_lowercase().ends_with(".dreamskin")
                })
                .collect();
            app.state::<ActivationQueue>()
                .0
                .lock()
                .unwrap()
                .extend(initial);
            let handle = app.handle().clone();
            app.deep_link().on_open_url(move |event| {
                let values: Vec<_> = event.urls().iter().map(ToString::to_string).collect();
                handle
                    .state::<ActivationQueue>()
                    .0
                    .lock()
                    .unwrap()
                    .extend(values.clone());
                let _ = handle.emit("external-activation", values);
            });
            Ok(())
        })
        .invoke_handler(tauri::generate_handler![
            get_app_state,
            sync_catalog,
            import_local,
            install_uri,
            pending_activations,
            read_preview,
            theme_runtime_ready,
            apply_theme,
            restart_and_apply,
            rollback_theme
        ])
        .run(tauri::generate_context!())
        .expect("failed to run Codex-Skin");
}
