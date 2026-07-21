use crate::error::{AppError, Result};
use futures_util::StreamExt;
use semver::Version;
use serde::{Deserialize, Serialize};
use sha2::{Digest, Sha256};
use std::{
    fs,
    io::Write,
    path::Path,
    process::{Command, Stdio},
    time::Duration,
};
use tauri::{AppHandle, Emitter};

const RELEASE_API: &str =
    "https://api.github.com/repos/lixiaobaivv/Codex-Skin/releases?per_page=100";
const CHECKSUM_ASSET: &str = "Codex-Skin-installers-SHA256SUMS.txt";
const MAX_INSTALLER_SIZE: u64 = 256 * 1024 * 1024;
const MAX_CHECKSUM_SIZE: u64 = 64 * 1024;

#[derive(Clone, Debug, Deserialize)]
struct GithubAsset {
    name: String,
    browser_download_url: String,
    size: u64,
}

#[derive(Clone, Debug, Deserialize)]
struct GithubRelease {
    tag_name: String,
    body: Option<String>,
    assets: Vec<GithubAsset>,
    #[serde(default)]
    draft: bool,
    #[serde(default)]
    prerelease: bool,
}

#[derive(Clone, Debug, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct UpdateInfo {
    version: String,
    notes: String,
    size: u64,
}

#[derive(Clone, Debug, Serialize)]
#[serde(rename_all = "camelCase")]
struct UpdateProgress {
    downloaded: u64,
    total: u64,
}

fn installer_asset_name() -> Result<&'static str> {
    #[cfg(windows)]
    {
        return Ok("Codex-Skin-Setup-win-x64.exe");
    }
    #[cfg(all(target_os = "macos", target_arch = "aarch64"))]
    {
        return Ok("Codex-Skin-osx-arm64.pkg");
    }
    #[cfg(all(target_os = "macos", target_arch = "x86_64"))]
    {
        return Ok("Codex-Skin-osx-x64.pkg");
    }
    #[allow(unreachable_code)]
    Err(AppError::Message("当前平台暂不支持客户端在线更新。".into()))
}

fn http_client() -> Result<reqwest::Client> {
    Ok(reqwest::Client::builder()
        .timeout(Duration::from_secs(60))
        .build()?)
}

async fn latest_release(client: &reqwest::Client) -> Result<GithubRelease> {
    let releases: Vec<GithubRelease> = client
        .get(RELEASE_API)
        .header("Accept", "application/vnd.github+json")
        .header(
            "User-Agent",
            concat!("Codex-Skin/", env!("CARGO_PKG_VERSION")),
        )
        .send()
        .await?
        .error_for_status()?
        .json()
        .await?;
    select_latest_release(releases)
}

fn release_version(release: &GithubRelease) -> Result<Version> {
    let value = release
        .tag_name
        .strip_prefix('v')
        .ok_or_else(|| AppError::Message(format!("不是客户端 Release：{}", release.tag_name)))?;
    Version::parse(value)
        .map_err(|_| AppError::Message(format!("GitHub Release 版本号无效：{}", release.tag_name)))
}

fn select_latest_release(releases: Vec<GithubRelease>) -> Result<GithubRelease> {
    releases
        .into_iter()
        .filter(|release| !release.draft && !release.prerelease)
        .filter_map(|release| {
            let version = release_version(&release).ok()?;
            version.pre.is_empty().then_some((version, release))
        })
        .max_by(|left, right| left.0.cmp(&right.0))
        .map(|(_, release)| release)
        .ok_or_else(|| AppError::Message("没有找到稳定的 Codex-Skin 客户端 Release。".into()))
}

fn current_version() -> Version {
    Version::parse(env!("CARGO_PKG_VERSION")).expect("package version must be valid semver")
}

fn asset<'a>(release: &'a GithubRelease, name: &str) -> Result<&'a GithubAsset> {
    release
        .assets
        .iter()
        .find(|asset| asset.name == name)
        .ok_or_else(|| AppError::Message(format!("最新 Release 缺少更新文件：{name}")))
}

fn validate_asset_url(url: &str, release: &GithubRelease, name: &str) -> Result<()> {
    let url =
        url::Url::parse(url).map_err(|_| AppError::Message("更新文件下载地址无效。".into()))?;
    let expected_suffix = format!("/{}/{name}", release.tag_name);
    if url.scheme() != "https"
        || url.host_str() != Some("github.com")
        || !url
            .path()
            .starts_with("/lixiaobaivv/Codex-Skin/releases/download/")
        || !url.path().ends_with(&expected_suffix)
    {
        return Err(AppError::Message(
            "更新文件不是受信任的 Codex-Skin Release 资产。".into(),
        ));
    }
    Ok(())
}

pub async fn check() -> Result<Option<UpdateInfo>> {
    let client = http_client()?;
    let release = latest_release(&client).await?;
    let version = release_version(&release)?;
    if version <= current_version() {
        return Ok(None);
    }
    let installer = asset(&release, installer_asset_name()?)?;
    validate_asset_url(&installer.browser_download_url, &release, &installer.name)?;
    Ok(Some(UpdateInfo {
        version: version.to_string(),
        notes: release.body.clone().unwrap_or_default(),
        size: installer.size,
    }))
}

async fn download_bytes(
    client: &reqwest::Client,
    asset: &GithubAsset,
    release: &GithubRelease,
    limit: u64,
) -> Result<Vec<u8>> {
    validate_asset_url(&asset.browser_download_url, release, &asset.name)?;
    if asset.size > limit {
        return Err(AppError::Message(format!("更新文件过大：{}", asset.name)));
    }
    let response = client
        .get(&asset.browser_download_url)
        .header(
            "User-Agent",
            concat!("Codex-Skin/", env!("CARGO_PKG_VERSION")),
        )
        .send()
        .await?
        .error_for_status()?;
    let bytes = response.bytes().await?;
    if bytes.len() as u64 > limit || (asset.size > 0 && bytes.len() as u64 != asset.size) {
        return Err(AppError::Message(format!(
            "更新文件大小不匹配：{}",
            asset.name
        )));
    }
    Ok(bytes.to_vec())
}

fn expected_checksum(contents: &[u8], name: &str) -> Result<String> {
    let contents = std::str::from_utf8(contents)
        .map_err(|_| AppError::Message("更新校验清单不是有效的 UTF-8 文本。".into()))?;
    contents
        .lines()
        .filter_map(|line| {
            let mut fields = line.split_whitespace();
            Some((fields.next()?, fields.next()?))
        })
        .find(|(_, filename)| *filename == name)
        .map(|(checksum, _)| checksum.to_ascii_lowercase())
        .filter(|checksum| checksum.len() == 64 && checksum.chars().all(|c| c.is_ascii_hexdigit()))
        .ok_or_else(|| AppError::Message(format!("更新校验清单缺少有效记录：{name}")))
}

async fn download_installer(
    app: &AppHandle,
    client: &reqwest::Client,
    asset: &GithubAsset,
    release: &GithubRelease,
    expected: &str,
    destination: &Path,
) -> Result<()> {
    validate_asset_url(&asset.browser_download_url, release, &asset.name)?;
    if asset.size == 0 || asset.size > MAX_INSTALLER_SIZE {
        return Err(AppError::Message("更新安装包大小无效。".into()));
    }
    let response = client
        .get(&asset.browser_download_url)
        .header(
            "User-Agent",
            concat!("Codex-Skin/", env!("CARGO_PKG_VERSION")),
        )
        .send()
        .await?
        .error_for_status()?;
    if response
        .content_length()
        .is_some_and(|length| length != asset.size)
    {
        return Err(AppError::Message("更新安装包响应大小不匹配。".into()));
    }
    let temporary = destination.with_extension("download");
    let mut output = fs::OpenOptions::new()
        .write(true)
        .create_new(true)
        .open(&temporary)?;
    let mut digest = Sha256::new();
    let mut downloaded = 0_u64;
    let mut stream = response.bytes_stream();
    while let Some(chunk) = stream.next().await {
        let chunk = chunk?;
        downloaded = downloaded.saturating_add(chunk.len() as u64);
        if downloaded > asset.size || downloaded > MAX_INSTALLER_SIZE {
            let _ = fs::remove_file(&temporary);
            return Err(AppError::Message("更新安装包超过允许大小。".into()));
        }
        output.write_all(&chunk)?;
        digest.update(&chunk);
        let _ = app.emit(
            "app-update-progress",
            UpdateProgress {
                downloaded,
                total: asset.size,
            },
        );
    }
    output.sync_all()?;
    drop(output);
    if downloaded != asset.size || hex::encode(digest.finalize()) != expected {
        let _ = fs::remove_file(&temporary);
        return Err(AppError::Message(
            "更新安装包 SHA-256 校验失败，已拒绝安装。".into(),
        ));
    }
    if destination.exists() {
        fs::remove_file(destination)?;
    }
    fs::rename(temporary, destination)?;
    Ok(())
}

fn launch_installer(app: &AppHandle, installer: &Path) -> Result<String> {
    #[cfg(windows)]
    {
        Command::new(installer)
            .args([
                "/SP-",
                "/SILENT",
                "/CLOSEAPPLICATIONS",
                "/RESTARTAPPLICATIONS",
            ])
            .stdin(Stdio::null())
            .stdout(Stdio::null())
            .stderr(Stdio::null())
            .spawn()
            .map_err(|error| AppError::Message(format!("启动更新安装程序失败：{error}")))?;
        app.exit(0);
        return Ok("更新安装程序已启动。".into());
    }
    #[cfg(target_os = "macos")]
    {
        let _ = app;
        Command::new("open")
            .arg(installer)
            .stdin(Stdio::null())
            .stdout(Stdio::null())
            .stderr(Stdio::null())
            .spawn()
            .map_err(|error| AppError::Message(format!("打开更新安装包失败：{error}")))?;
        return Ok("更新安装包已打开，请按系统提示完成安装。".into());
    }
    #[allow(unreachable_code)]
    Err(AppError::Message("当前平台暂不支持客户端在线更新。".into()))
}

pub async fn install(app: AppHandle, requested_version: String) -> Result<String> {
    let requested = Version::parse(&requested_version)
        .map_err(|_| AppError::Message("请求安装的版本号无效。".into()))?;
    let client = http_client()?;
    let release = latest_release(&client).await?;
    let version = release_version(&release)?;
    if version != requested || version <= current_version() {
        return Err(AppError::Message("可用更新已经变化，请重新检查。".into()));
    }
    let installer_name = installer_asset_name()?;
    let installer = asset(&release, installer_name)?;
    let checksums = asset(&release, CHECKSUM_ASSET)?;
    let checksum_contents = download_bytes(&client, checksums, &release, MAX_CHECKSUM_SIZE).await?;
    let checksum = expected_checksum(&checksum_contents, installer_name)?;
    let directory = std::env::temp_dir()
        .join("Codex-Skin-Updates")
        .join(format!("{}-{}", version, uuid::Uuid::new_v4().simple()));
    fs::create_dir_all(&directory)?;
    let destination = directory.join(installer_name);
    if let Err(error) =
        download_installer(&app, &client, installer, &release, &checksum, &destination).await
    {
        let _ = fs::remove_dir_all(&directory);
        return Err(error);
    }
    launch_installer(&app, &destination)
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn parses_exact_checksum_entry() {
        let hash = "a".repeat(64);
        let contents = format!("{hash}  other.exe\n{hash}  wanted.exe\n");
        assert_eq!(
            expected_checksum(contents.as_bytes(), "wanted.exe").unwrap(),
            hash
        );
        assert!(expected_checksum(contents.as_bytes(), "missing.exe").is_err());
    }

    #[test]
    fn rejects_untrusted_release_asset_url() {
        let release = GithubRelease {
            tag_name: "v1.2.3".into(),
            body: None,
            assets: Vec::new(),
            draft: false,
            prerelease: false,
        };
        assert!(
            validate_asset_url("https://example.com/update.exe", &release, "update.exe").is_err()
        );
        assert!(
            validate_asset_url(
                "https://github.com/lixiaobaivv/Codex-Skin/releases/download/v1.2.3/update.exe",
                &release,
                "update.exe"
            )
            .is_ok()
        );
    }

    #[test]
    fn selects_stable_client_release_instead_of_theme_release() {
        let release = |tag: &str, draft: bool, prerelease: bool| GithubRelease {
            tag_name: tag.into(),
            body: None,
            assets: Vec::new(),
            draft,
            prerelease,
        };
        let selected = select_latest_release(vec![
            release("theme-example-v9.0.0", false, false),
            release("v0.6.2", false, false),
            release("v0.6.4", true, false),
            release("v0.7.0-beta.1", false, true),
            release("v0.6.3", false, false),
        ])
        .unwrap();
        assert_eq!(selected.tag_name, "v0.6.3");
    }
}
