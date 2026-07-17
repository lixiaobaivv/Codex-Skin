use crate::{
    dreamskin::{self, Expected, ImportResult},
    error::{AppError, Result},
    models::SOURCES,
    paths,
};
use futures_util::StreamExt;
use sha2::{Digest, Sha256};
use std::{
    collections::{HashMap, HashSet},
    net::{IpAddr, Ipv4Addr, Ipv6Addr, SocketAddr},
    path::Path,
};
use tokio::io::AsyncWriteExt;
use url::Url;

const MAX_URI: usize = 4096;
const MAX_PACKAGE: u64 = 20 * 1024 * 1024;
#[derive(Clone, Debug)]
struct Request {
    url: Url,
    sha256: String,
    size: u64,
    id: Option<String>,
    version: Option<String>,
}

pub async fn install_uri(input: &str, preferred: Option<&str>) -> Result<ImportResult> {
    let request = parse(input)?;
    let root = paths::store_root()?.join("downloads");
    std::fs::create_dir_all(&root)?;
    let temporary = tempfile::tempdir_in(root)?;
    let archive = temporary.path().join("archive.dreamskin");
    let mut last = None;
    for candidate in candidates(&request.url, preferred)? {
        match download(&candidate, &request, &archive).await {
            Ok(()) => {
                return dreamskin::import_expected(
                    archive.to_str().unwrap(),
                    Expected {
                        sha256: request.sha256,
                        size: request.size,
                        id: request.id,
                        version: request.version,
                    },
                );
            }
            Err(error) => last = Some(error),
        }
    }
    Err(last.unwrap_or_else(|| {
        AppError::Message("DSI_DOWNLOAD_FAILED: 没有可用的主题下载线路。".into())
    }))
}

fn parse(input: &str) -> Result<Request> {
    if input.is_empty() || input.as_bytes().len() > MAX_URI {
        return fail("DSI_URI_INVALID", "dreamskin URI 为空或超过 4096 字节。");
    }
    let uri = Url::parse(input)
        .map_err(|_| AppError::Message("DSI_URI_INVALID: URI 格式无效。".into()))?;
    if uri.scheme() != "dreamskin"
        || uri.host_str() != Some("install")
        || !uri.username().is_empty()
        || uri.password().is_some()
        || uri.fragment().is_some()
        || !(uri.path().is_empty() || uri.path() == "/")
    {
        return fail("DSI_URI_INVALID", "只接受 dreamskin://install 深链接。");
    }
    let query = input
        .split_once('?')
        .map(|v| v.1)
        .filter(|v| !v.is_empty())
        .ok_or_else(|| AppError::Message("DSI_URI_INVALID: 深链接缺少查询参数。".into()))?;
    if query.contains('#') {
        return fail("DSI_URI_INVALID", "深链接不得包含 fragment。");
    }
    let allowed: HashSet<_> = ["url", "sha256", "size", "id", "version"]
        .into_iter()
        .collect();
    let mut values = HashMap::new();
    for pair in query.split('&') {
        let mut pieces = pair.split('=');
        let raw_name = pieces.next().unwrap_or("");
        let raw_value = pieces.next().ok_or_else(|| {
            AppError::Message("DSI_URI_INVALID: 查询参数必须恰好包含一个等号。".into())
        })?;
        if raw_name.is_empty() || pieces.next().is_some() {
            return fail("DSI_URI_INVALID", "查询参数必须恰好包含一个等号。");
        }
        let name = decode(raw_name)?;
        if name == "url" && (raw_value.contains(':') || raw_value.contains('/')) {
            return fail("DSI_URI_INVALID", "url 参数必须完整进行百分号编码。");
        }
        let value = decode(raw_value)?;
        if !allowed.contains(name.as_str()) {
            return fail("DSI_URI_INVALID", &format!("未知查询参数：{name}"));
        }
        if values.insert(name.clone(), value).is_some() {
            return fail("DSI_URI_INVALID", &format!("重复查询参数：{name}"));
        }
    }
    for key in ["url", "sha256", "size"] {
        if !values.get(key).is_some_and(|v| !v.is_empty()) {
            return fail("DSI_URI_INVALID", &format!("缺少查询参数：{key}"));
        }
    }
    let url = validate_url(&values["url"])?;
    let sha256 = values.remove("sha256").unwrap();
    if sha256.len() != 64
        || !sha256
            .chars()
            .all(|c| c.is_ascii_hexdigit() && !c.is_ascii_uppercase())
    {
        return fail("DSI_URI_INVALID", "sha256 必须是 64 位小写十六进制。");
    }
    let size = values
        .remove("size")
        .unwrap()
        .parse::<u64>()
        .ok()
        .filter(|v| *v > 0 && *v <= MAX_PACKAGE)
        .ok_or_else(|| {
            AppError::Message("DSI_URI_INVALID: size 必须是 1 到 20971520 的十进制整数。".into())
        })?;
    let id = values.remove("id");
    if id
        .as_ref()
        .is_some_and(|v| v.len() < 3 || v.len() > 128 || !valid_id(v))
    {
        return fail("DSI_URI_INVALID", "id 提示格式无效。");
    }
    let version = values.remove("version");
    if version
        .as_ref()
        .is_some_and(|v| v.len() > 64 || semver::Version::parse(v).is_err())
    {
        return fail("DSI_URI_INVALID", "version 提示不是有效 SemVer。");
    }
    Ok(Request {
        url,
        sha256,
        size,
        id,
        version,
    })
}

fn decode(value: &str) -> Result<String> {
    let bytes = value.as_bytes();
    let mut output = Vec::with_capacity(bytes.len());
    let mut i = 0;
    while i < bytes.len() {
        if bytes[i] == b'%' {
            if i + 2 >= bytes.len() {
                return fail("DSI_URI_INVALID", "查询参数包含畸形百分号编码。");
            }
            let high = hex(bytes[i + 1])?;
            let low = hex(bytes[i + 2])?;
            output.push((high << 4) | low);
            i += 3;
        } else {
            if !bytes[i].is_ascii() {
                return fail("DSI_URI_INVALID", "非 ASCII 字符必须进行百分号编码。");
            }
            output.push(bytes[i]);
            i += 1;
        }
    }
    String::from_utf8(output)
        .map_err(|_| AppError::Message("DSI_URI_INVALID: 查询参数不是严格 UTF-8。".into()))
}
fn hex(v: u8) -> Result<u8> {
    match v {
        b'0'..=b'9' => Ok(v - b'0'),
        b'a'..=b'f' => Ok(v - b'a' + 10),
        b'A'..=b'F' => Ok(v - b'A' + 10),
        _ => fail("DSI_URI_INVALID", "查询参数包含畸形百分号编码。"),
    }
}
fn validate_url(value: &str) -> Result<Url> {
    let url =
        Url::parse(value).map_err(|_| AppError::Message("DSI_URI_INVALID: 包地址无效。".into()))?;
    if url.scheme() != "https"
        || !url.username().is_empty()
        || url.password().is_some()
        || url.fragment().is_some()
        || url.port_or_known_default() != Some(443)
        || url.host_str().is_none()
    {
        return fail(
            "DSI_URI_INVALID",
            "包地址必须是无凭据、无 fragment、端口 443 的绝对 HTTPS URL。",
        );
    }
    let host = url.host_str().unwrap();
    if host.eq_ignore_ascii_case("localhost")
        || host.to_ascii_lowercase().ends_with(".localhost")
        || host.to_ascii_lowercase().ends_with(".local")
    {
        return fail("DSI_NETWORK_BLOCKED", "包地址不能使用本机或本地域名。");
    }
    if host.parse::<IpAddr>().ok().is_some_and(|ip| !public_ip(ip)) {
        return fail(
            "DSI_NETWORK_BLOCKED",
            "包地址不能使用私网、链路本地或保留 IP。",
        );
    }
    Ok(url)
}

fn candidates(url: &Url, preferred: Option<&str>) -> Result<Vec<Url>> {
    if !url
        .host_str()
        .is_some_and(|v| v.eq_ignore_ascii_case("github.com"))
        || !url.path().contains("/releases/download/")
    {
        return Ok(vec![url.clone()]);
    }
    let preferred = SOURCES
        .iter()
        .find(|s| Some(s.id) == preferred)
        .unwrap_or(&SOURCES[0]);
    let mut sources = vec![preferred];
    for source in &SOURCES {
        if !sources.iter().any(|v| v.id == source.id) {
            sources.push(source);
        }
    }
    sources
        .into_iter()
        .map(|source| validate_url(&format!("{}{}", source.prefix, url)))
        .collect()
}

async fn download(initial: &Url, request: &Request, destination: &Path) -> Result<()> {
    let mut current = initial.clone();
    for redirects in 0..=3 {
        current = validate_url(current.as_str())?;
        let host = current.host_str().unwrap().to_owned();
        let addresses = resolve_public(&host).await?;
        let client = reqwest::Client::builder()
            .no_proxy()
            .redirect(reqwest::redirect::Policy::none())
            .timeout(std::time::Duration::from_secs(45))
            .resolve_to_addrs(&host, &addresses)
            .build()?;
        let response = client
            .get(current.clone())
            .header("User-Agent", "Codex-Skin/2")
            .header("Accept", "application/vnd.codex-dream-skin+zip")
            .send()
            .await?;
        if response.status().is_redirection() {
            if redirects == 3 {
                return fail("DSI_REDIRECT_LIMIT", "主题包下载重定向超过 3 次。");
            }
            let location = response
                .headers()
                .get(reqwest::header::LOCATION)
                .and_then(|v| v.to_str().ok())
                .ok_or_else(|| {
                    AppError::Message("DSI_DOWNLOAD_FAILED: 下载重定向缺少 Location。".into())
                })?;
            current = current
                .join(location)
                .map_err(|_| AppError::Message("DSI_DOWNLOAD_FAILED: 重定向地址无效。".into()))?;
            continue;
        }
        if !response.status().is_success() {
            return fail(
                "DSI_DOWNLOAD_FAILED",
                &format!("主题包服务器返回 HTTP {}。", response.status().as_u16()),
            );
        }
        if let Some(length) = response.content_length() {
            if length == 0 || length > MAX_PACKAGE {
                return fail(
                    "DSI_SIZE_LIMIT",
                    "服务器声明的主题包大小无效或超过 20 MiB。",
                );
            }
            if length != request.size {
                return fail(
                    "DSI_SIZE_MISMATCH",
                    "服务器 Content-Length 与深链接声明不一致。",
                );
            }
        }
        let mut output = tokio::fs::File::create(destination).await?;
        let mut total = 0u64;
        let mut hash = Sha256::new();
        let mut stream = response.bytes_stream();
        while let Some(chunk) = stream.next().await {
            let chunk = chunk?;
            total += chunk.len() as u64;
            if total > MAX_PACKAGE || total > request.size {
                return fail("DSI_SIZE_LIMIT", "主题包响应体超过声明大小或 20 MiB 限制。");
            }
            hash.update(&chunk);
            output.write_all(&chunk).await?;
        }
        output.flush().await?;
        if total != request.size {
            return fail(
                "DSI_SIZE_MISMATCH",
                "下载完成后的字节数与深链接声明不一致。",
            );
        }
        if hex::encode(hash.finalize()) != request.sha256 {
            return fail(
                "DSI_HASH_MISMATCH",
                "下载主题包的 SHA-256 与深链接声明不一致。",
            );
        }
        return Ok(());
    }
    fail("DSI_DOWNLOAD_FAILED", "主题包下载失败。")
}

async fn resolve_public(host: &str) -> Result<Vec<SocketAddr>> {
    let values = tokio::net::lookup_host((host, 443))
        .await
        .map_err(|e| AppError::Message(format!("DSI_NETWORK_BLOCKED: 域名解析失败：{e}")))?;
    let result: Vec<_> = values.filter(|value| public_ip(value.ip())).collect();
    if result.is_empty() {
        return fail("DSI_NETWORK_BLOCKED", "下载域名未解析到允许的公网地址。");
    }
    Ok(result)
}
fn public_ip(ip: IpAddr) -> bool {
    match ip {
        IpAddr::V4(v) => public_v4(v),
        IpAddr::V6(v) => public_v6(v),
    }
}
fn public_v4(v: Ipv4Addr) -> bool {
    let b = v.octets();
    if b[0] == 0 || b[0] == 10 || b[0] == 127 || b[0] >= 224 {
        return false;
    }
    if b[0] == 100 && (64..=127).contains(&b[1]) {
        return false;
    }
    if b[0] == 169 && b[1] == 254 {
        return false;
    }
    if b[0] == 172 && (16..=31).contains(&b[1]) {
        return false;
    }
    if b[0] == 192 && (b[1] == 168 || b[1] == 0) {
        return false;
    }
    if b[0] == 192 && b[1] == 88 && b[2] == 99 {
        return false;
    }
    if b[0] == 198 && (b[1] == 18 || b[1] == 19) {
        return false;
    }
    if b[0] == 198 && b[1] == 51 && b[2] == 100 {
        return false;
    }
    if b[0] == 203 && b[1] == 0 && b[2] == 113 {
        return false;
    }
    true
}
fn public_v6(v: Ipv6Addr) -> bool {
    if v.is_loopback() || v.is_unspecified() || v.is_multicast() {
        return false;
    }
    let b = v.octets();
    if b[0] & 0xfe == 0xfc || b[0] == 0xfe && (b[1] & 0xc0) == 0x80 {
        return false;
    }
    if b[..2] == [0x20, 0x02]
        || b[..4] == [0x20, 0x01, 0, 0]
        || b[..4] == [0x20, 0x01, 0x0d, 0xb8]
        || b[..12] == [0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0]
        || b[..12] == [0, 0x64, 0xff, 0x9b, 0, 0, 0, 0, 0, 0, 0, 0]
    {
        return false;
    }
    true
}
fn valid_id(v: &str) -> bool {
    v.chars()
        .all(|c| c.is_ascii_lowercase() || c.is_ascii_digit() || c == '.' || c == '-')
        && !v.starts_with(['.', '-'])
        && !v.ends_with(['.', '-'])
        && !v.contains("..")
        && !v.contains("--")
        && !v.contains(".-")
        && !v.contains("-.")
}
fn fail<T>(code: &str, message: &str) -> Result<T> {
    Err(AppError::Message(format!("{code}: {message}")))
}

#[cfg(test)]
mod tests {
    use super::*;
    #[test]
    fn parser_accepts_canonical_uri() {
        let uri = "dreamskin://install?url=https%3A%2F%2Fgithub.com%2Flixiaobaivv%2FCodex-Skin%2Freleases%2Fdownload%2Fx%2Fx.dreamskin&sha256=aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa&size=42&id=codex-skin.test&version=1.0.0";
        let request = parse(uri).unwrap();
        assert_eq!(request.size, 42);
        assert_eq!(request.id.as_deref(), Some("codex-skin.test"));
    }
    #[test]
    fn private_networks_are_blocked() {
        for value in [
            "10.0.0.1",
            "169.254.1.1",
            "172.16.0.1",
            "192.168.1.1",
            "::1",
            "fc00::1",
            "fe80::1",
        ] {
            assert!(!public_ip(value.parse().unwrap()));
        }
    }
}
