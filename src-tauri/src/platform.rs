use crate::{
    cdp::{self, Payload},
    error::{AppError, Result},
};
use std::{
    path::{Path, PathBuf},
    time::{Duration, Instant},
};
use sysinfo::{ProcessesToUpdate, Signal, System};

#[derive(Clone, Debug)]
#[cfg_attr(windows, allow(dead_code))]
pub struct Installation {
    pub app_path: PathBuf,
    pub executable: PathBuf,
}

pub fn discover() -> Result<Installation> {
    #[cfg(windows)]
    {
        return windows_platform::discover();
    }
    #[cfg(target_os = "macos")]
    {
        return macos_platform::discover();
    }
    #[allow(unreachable_code)]
    Err(AppError::Message(
        "当前系统不支持 Codex 平台适配器。".into(),
    ))
}

pub async fn restart_and_inject(payload: &Payload, timeout: Duration) -> Result<()> {
    let installation = discover()?;
    stop(&installation).await?;
    start(&installation, true)?;
    let deadline = Instant::now() + timeout;
    while Instant::now() < deadline {
        if cdp::is_ready().await {
            let remaining = deadline
                .saturating_duration_since(Instant::now())
                .min(Duration::from_secs(10));
            if remaining > Duration::ZERO && cdp::inject(payload, remaining).await.unwrap_or(0) > 0
            {
                return Ok(());
            }
        }
        tokio::time::sleep(Duration::from_millis(500)).await;
    }
    Err(AppError::Message(
        "Codex 已启动，但主题未能在 90 秒内完成 CDP 注入。".into(),
    ))
}

async fn stop(installation: &Installation) -> Result<()> {
    let deadline = Instant::now() + Duration::from_secs(15);
    while Instant::now() < deadline {
        let mut system = System::new_all();
        system.refresh_processes(ProcessesToUpdate::All, true);
        let mut found = false;
        for process in system.processes().values() {
            let Some(path) = process.exe() else {
                continue;
            };
            if belongs_to(path, installation) {
                found = true;
                let _ = process
                    .kill_with(Signal::Kill)
                    .or_else(|| Some(process.kill()));
            }
        }
        if !found {
            return Ok(());
        }
        tokio::time::sleep(Duration::from_millis(250)).await;
    }
    Err(AppError::Message("Codex 未能完全退出，请重试。".into()))
}

fn belongs_to(path: &Path, _installation: &Installation) -> bool {
    #[cfg(windows)]
    {
        return path
            .to_string_lossy()
            .contains("\\WindowsApps\\OpenAI.Codex_");
    }
    #[cfg(target_os = "macos")]
    {
        return path == _installation.executable || path.starts_with(&_installation.app_path);
    }
    #[allow(unreachable_code)]
    false
}

fn start(_installation: &Installation, enable_cdp: bool) -> Result<()> {
    #[cfg(windows)]
    {
        return windows_platform::start(enable_cdp);
    }
    #[cfg(target_os = "macos")]
    {
        return macos_platform::start(_installation, enable_cdp);
    }
    #[allow(unreachable_code)]
    Err(AppError::Message("当前系统不支持 Codex 启动。".into()))
}

#[cfg(windows)]
mod windows_platform {
    use super::*;
    use windows::{
        Win32::{
            System::Com::{
                CLSCTX_LOCAL_SERVER, COINIT_APARTMENTTHREADED, CoCreateInstance, CoInitializeEx,
                CoUninitialize,
            },
            UI::Shell::{
                ACTIVATEOPTIONS, ApplicationActivationManager, IApplicationActivationManager,
            },
        },
        core::HSTRING,
    };
    use winreg::{RegKey, enums::HKEY_CURRENT_USER};

    pub fn discover() -> Result<Installation> {
        if let Some(configured) = std::env::var_os("CODEX_APP_PATH") {
            let executable = PathBuf::from(configured);
            if executable.is_file() {
                return Ok(Installation {
                    app_path: executable.parent().unwrap().to_owned(),
                    executable,
                });
            }
        }
        let current_user = RegKey::predef(HKEY_CURRENT_USER);
        let repository=current_user.open_subkey(r"Software\Classes\Local Settings\Software\Microsoft\Windows\CurrentVersion\AppModel\Repository\Packages").map_err(|error| AppError::Message(format!("无法读取 Codex 安装信息：{error}")))?;
        let mut candidates = Vec::new();
        for name in repository
            .enum_keys()
            .filter_map(|item| item.ok())
            .filter(|name| name.starts_with("OpenAI.Codex_"))
        {
            let Ok(package) = repository.open_subkey(name) else {
                continue;
            };
            let Ok(root): std::result::Result<String, _> = package.get_value("PackageRootFolder")
            else {
                continue;
            };
            let executable = PathBuf::from(root).join("app").join("ChatGPT.exe");
            if executable.is_file() {
                candidates.push(executable);
            }
        }
        candidates.sort_by_key(|path| std::fs::metadata(path).and_then(|m| m.modified()).ok());
        let executable = candidates
            .pop()
            .ok_or_else(|| AppError::Message("未找到 Windows Codex 安装。".into()))?;
        Ok(Installation {
            app_path: executable.parent().unwrap().to_owned(),
            executable,
        })
    }

    pub fn start(enable_cdp: bool) -> Result<()> {
        let arguments = if enable_cdp {
            "--remote-debugging-address=127.0.0.1 --remote-debugging-port=9229"
        } else {
            ""
        };
        unsafe {
            CoInitializeEx(None, COINIT_APARTMENTTHREADED)
                .ok()
                .map_err(|error| AppError::Message(format!("初始化 Windows COM 失败：{error}")))?;
            let manager: IApplicationActivationManager =
                CoCreateInstance(&ApplicationActivationManager, None, CLSCTX_LOCAL_SERVER)
                    .map_err(|error| {
                        AppError::Message(format!("创建 Codex 激活器失败：{error}"))
                    })?;
            let result = manager.ActivateApplication(
                &HSTRING::from("OpenAI.Codex_2p2nqsd0c76g0!App"),
                &HSTRING::from(arguments),
                ACTIVATEOPTIONS(0),
            );
            CoUninitialize();
            result.map_err(|error| AppError::Message(format!("启动 Codex 失败：{error}")))?;
        }
        Ok(())
    }
}

#[cfg(target_os = "macos")]
mod macos_platform {
    use super::*;
    pub fn discover() -> Result<Installation> {
        let home = dirs::home_dir().unwrap_or_default();
        let candidates = [
            std::env::var_os("CODEX_APP_PATH").map(PathBuf::from),
            Some(PathBuf::from("/Applications/Codex.app")),
            Some(home.join("Applications/Codex.app")),
            Some(PathBuf::from("/Applications/ChatGPT.app")),
            Some(home.join("Applications/ChatGPT.app")),
        ];
        for app_path in candidates.into_iter().flatten() {
            let directory = app_path.join("Contents/MacOS");
            for name in ["Codex", "ChatGPT", "OpenAI Codex"] {
                let executable = directory.join(name);
                if executable.is_file() {
                    return Ok(Installation {
                        app_path,
                        executable,
                    });
                }
            }
            if let Ok(mut files) = std::fs::read_dir(&directory) {
                if let Some(Ok(entry)) = files.next() {
                    return Ok(Installation {
                        app_path,
                        executable: entry.path(),
                    });
                }
            }
        }
        Err(AppError::Message(
            "未找到 macOS Codex 安装，可通过 CODEX_APP_PATH 指定位置。".into(),
        ))
    }
    pub fn start(installation: &Installation, enable_cdp: bool) -> Result<()> {
        let mut command = std::process::Command::new("/usr/bin/open");
        command.arg("-n").arg(&installation.app_path);
        if enable_cdp {
            command
                .arg("--args")
                .arg("--remote-debugging-address=127.0.0.1")
                .arg("--remote-debugging-port=9229");
        }
        let status = command.status()?;
        if !status.success() {
            return Err(AppError::Message(format!(
                "LaunchServices 启动 Codex 失败：{status}"
            )));
        }
        Ok(())
    }
}
