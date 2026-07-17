use serde::{Deserialize, Serialize};

#[derive(Clone, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct Source {
    pub id: &'static str,
    pub name: &'static str,
    #[serde(skip)]
    pub prefix: &'static str,
}

pub const SOURCES: [Source; 3] = [
    Source {
        id: "github",
        name: "GitHub 官方",
        prefix: "",
    },
    Source {
        id: "gh-proxy",
        name: "GH Proxy",
        prefix: "https://gh-proxy.com/",
    },
    Source {
        id: "ghfast",
        name: "GHFast",
        prefix: "https://ghfast.top/",
    },
];

#[derive(Debug, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct RepositorySettings {
    pub source_id: String,
}
impl Default for RepositorySettings {
    fn default() -> Self {
        Self {
            source_id: "github".into(),
        }
    }
}

#[derive(Clone, Debug, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct ThemeSummary {
    pub id: String,
    pub version: String,
    pub name: String,
    pub description: String,
    pub category: String,
    pub preview_path: String,
    pub remote_version: Option<String>,
    pub installed_version: Option<String>,
    pub subscribed: bool,
    pub update_available: bool,
    #[serde(skip)]
    pub manifest_path: std::path::PathBuf,
}

#[derive(Serialize)]
#[serde(rename_all = "camelCase")]
pub struct AppState {
    pub themes: Vec<ThemeSummary>,
    pub sources: Vec<Source>,
    pub selected_source_id: String,
}

#[derive(Serialize)]
#[serde(rename_all = "camelCase")]
pub struct SyncResult {
    pub theme_count: usize,
    pub source_id: String,
    pub source_name: String,
}
