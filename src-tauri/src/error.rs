#[derive(Debug, thiserror::Error)]
pub enum AppError {
    #[error("{0}")]
    Message(String),
    #[error("文件操作失败：{0}")]
    Io(#[from] std::io::Error),
    #[error("JSON 格式无效：{0}")]
    Json(#[from] serde_json::Error),
    #[error("网络请求失败：{0}")]
    Http(#[from] reqwest::Error),
    #[error("ZIP 归档无效：{0}")]
    Zip(#[from] zip::result::ZipError),
}

pub type Result<T> = std::result::Result<T, AppError>;

impl serde::Serialize for AppError {
    fn serialize<S>(&self, serializer: S) -> std::result::Result<S::Ok, S::Error>
    where S: serde::Serializer { serializer.serialize_str(&self.to_string()) }
}
