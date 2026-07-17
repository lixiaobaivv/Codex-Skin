use crate::error::{AppError, Result};
use std::path::PathBuf;

pub fn store_root() -> Result<PathBuf> {
    if let Some(value) = std::env::var_os("CODEX_THEME_STORE_DATA_DIR") {
        return Ok(PathBuf::from(value));
    }
    dirs::data_local_dir()
        .map(|path| path.join("CodexThemeStore"))
        .ok_or_else(|| AppError::Message("无法确定当前用户的数据目录。".into()))
}
pub fn cache_root() -> Result<PathBuf> {
    Ok(store_root()?.join("ThemeCatalog"))
}
pub fn cache_themes() -> Result<PathBuf> {
    Ok(cache_root()?.join("themes"))
}
pub fn remote_catalog_path() -> Result<PathBuf> {
    Ok(cache_root()?.join("desktop-catalog-v2.json"))
}
pub fn catalog_sync_state_path() -> Result<PathBuf> {
    Ok(store_root()?.join("catalog-sync-state.json"))
}
pub fn theme_library_state_path() -> Result<PathBuf> {
    Ok(store_root()?.join("theme-library-state.json"))
}
pub fn settings_path() -> Result<PathBuf> {
    Ok(store_root()?.join("repository-settings.json"))
}
pub fn installed_root() -> Result<PathBuf> {
    if let Some(value) = std::env::var_os("CODEX_THEME_LIBRARY_DIR") {
        return Ok(PathBuf::from(value));
    }
    Ok(store_root()?.join("InstalledThemes"))
}
pub fn state_root() -> Result<PathBuf> {
    Ok(store_root()?.join("State"))
}
