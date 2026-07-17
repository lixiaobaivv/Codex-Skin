use crate::{
    catalog,
    cdp::Payload,
    error::{AppError, Result},
    paths,
};
use atomicwrites::{AllowOverwrite, AtomicFile};
use base64::Engine;
use serde_json::{Value, json};
use std::{
    fs,
    io::Write,
    path::{Path, PathBuf},
};

pub fn compile(theme_id: &str) -> Result<Payload> {
    let summary = catalog::find(theme_id)?;
    let raw = fs::read_to_string(&summary.manifest_path)?;
    let root: Value = serde_json::from_str(&raw)?;
    let package = root.get("packageVersion").and_then(Value::as_u64) == Some(1);
    let theme_value = if package {
        let colors = root.get("colors").cloned().unwrap_or_else(|| json!({}));
        json!({"accent":colors.get("accent"),"ink":colors.get("text"),"surface":colors.get("panel"),"semanticColors":{"diffAdded":colors.get("highlight"),"diffRemoved":colors.get("accentAlt"),"skill":colors.get("secondary")},"backgroundImageOpacity":0.22})
    } else {
        root.get("theme").cloned().unwrap_or(Value::Null)
    };
    let theme = theme_value
        .as_object()
        .ok_or_else(|| AppError::Message("主题缺少 theme 对象。".into()))?;
    let home = if package {
        json!({
        "brand":root.get("projectLabel").or_else(||root.get("name")),
        "eyebrow":root.get("brandSubtitle"),"title":root.get("tagline").or_else(||root.get("name")),
        "subtitle":root.get("quote").or_else(||root.get("description")),
        "quickActions":[
            {"icon":"01","title":"解释代码","description":"梳理模块与关键流程","prompt":"请解释当前项目的核心架构与关键代码。"},
            {"icon":"02","title":"修复问题","description":"定位并修复一个明确问题","prompt":"请检查当前项目并修复最重要的问题。"},
            {"icon":"03","title":"添加测试","description":"补充可靠的自动化验证","prompt":"请为当前改动补充自动化测试。"},
            {"icon":"04","title":"审查改动","description":"评估风险和回归点","prompt":"请审查当前改动并指出风险。"}
        ]})
    } else {
        root.get("home").cloned().unwrap_or_else(|| json!({}))
    };
    let copy = root.get("copy").cloned().unwrap_or_else(|| json!({}));
    let manifest_dir = summary
        .manifest_path
        .parent()
        .ok_or_else(|| AppError::Message("主题清单目录无效。".into()))?;
    let accent = color(theme.get("accent"), "#d96f4d")?;
    let ink = color(theme.get("ink"), "#20231f")?;
    let surface = color(theme.get("surface"), "#f5f6f3")?;
    let variant = root
        .get("variant")
        .and_then(Value::as_str)
        .unwrap_or("light");
    let dark = variant == "dark";
    let semantic = theme.get("semanticColors").and_then(Value::as_object);
    let added = color(semantic.and_then(|v| v.get("diffAdded")), "#00c853")?;
    let removed = color(semantic.and_then(|v| v.get("diffRemoved")), "#ff5f38")?;
    let skill = color(semantic.and_then(|v| v.get("skill")), "#cc7d5e")?;
    let under = mix(
        &surface,
        if dark { "#000000" } else { "#ffffff" },
        if dark { 0.18 } else { 0.34 },
    )?;
    let elevated = mix(
        &surface,
        if dark { "#ffffff" } else { "#000000" },
        if dark { 0.08 } else { 0.04 },
    )?;
    let secondary = alpha(&ink, if dark { 0.72 } else { 0.68 })?;
    let tertiary = alpha(&ink, if dark { 0.52 } else { 0.48 })?;
    let border = alpha(&accent, if dark { 0.34 } else { 0.22 })?;
    let shadow = if dark { "#000000" } else { ink.as_str() };
    let (accent_r, accent_g, accent_b) = rgb(&accent)?;
    let accent_luma =
        0.2126 * f64::from(accent_r) + 0.7152 * f64::from(accent_g) + 0.0722 * f64::from(accent_b);
    let button_text = if accent_luma > 150.0 {
        "#111111"
    } else {
        "#ffffff"
    };
    let background_path = if package {
        package_asset(root.get("image"), manifest_dir)?
    } else {
        asset(theme.get("backgroundImage"), manifest_dir, "backgrounds")?
    };
    let background = data_url(background_path)?;
    let logo = if package {
        None
    } else {
        data_url(asset(theme.get("logoImage"), manifest_dir, "logos")?)?
    };
    let pet = if package {
        None
    } else {
        data_url(asset(
            home.get("pet").and_then(|v| v.get("image")),
            manifest_dir,
            "pets",
        )?)?
    };
    let ui_font = theme
        .get("fonts")
        .and_then(|v| v.get("ui"))
        .and_then(Value::as_str)
        .unwrap_or("Inter, system-ui, sans-serif");
    let code_font = theme
        .get("fonts")
        .and_then(|v| v.get("code"))
        .and_then(Value::as_str)
        .unwrap_or("ui-monospace, monospace");
    let display_font = theme
        .get("fonts")
        .and_then(|v| v.get("display"))
        .and_then(Value::as_str)
        .unwrap_or(ui_font);
    let opacity = theme
        .get("backgroundImageOpacity")
        .and_then(Value::as_f64)
        .unwrap_or(0.18)
        .clamp(0.0, 1.0);
    let blur = theme
        .get("backgroundImageBlur")
        .and_then(Value::as_f64)
        .unwrap_or(0.0)
        .clamp(0.0, 24.0);
    let default_background_position = match summary.id.as_str() {
        "dilraba-star" => "center top",
        "enfp-pop" => "center 20%",
        "zhu-xudan-racing" => "center 30%",
        _ => "center",
    };
    let background_position = match theme.get("backgroundPosition").and_then(Value::as_str) {
        Some(value)
            if [
                "center",
                "center top",
                "center 20%",
                "center 30%",
                "center 40%",
                "center bottom",
                "left center",
                "right center",
            ]
            .contains(&value) =>
        {
            value
        }
        _ => default_background_position,
    };
    let curated_cover_theme = matches!(
        summary.id.as_str(),
        "dilraba-star" | "enfp-pop" | "jackson-sage" | "kun-stage" | "zhu-xudan-racing"
    );
    let background_fit = match theme.get("backgroundFit").and_then(Value::as_str) {
        Some("cover") => "cover",
        Some("contain") => "contain",
        Some("smart") => "smart",
        _ if curated_cover_theme => "cover",
        _ => "smart",
    };

    let mut css = CSS_TEMPLATE.to_owned();
    for (key, value) in [
        ("ID", summary.id.as_str()),
        ("VARIANT", variant),
        ("ACCENT", &accent),
        ("INK", &ink),
        ("SURFACE", &surface),
        ("UNDER", &under),
        ("ELEVATED", &elevated),
        ("SECONDARY", &secondary),
        ("TERTIARY", &tertiary),
        ("BORDER", &border),
        ("SHADOW", shadow),
        ("BUTTON_TEXT", button_text),
        ("ADDED", &added),
        ("REMOVED", &removed),
        ("SKILL", &skill),
        ("UI_FONT", ui_font),
        ("CODE_FONT", code_font),
        ("DISPLAY_FONT", display_font),
        ("BACKGROUND_POSITION", background_position),
    ] {
        css = css.replace(&format!("{{{{{key}}}}}"), value);
    }
    css = css
        .replace("{{COLOR_SCHEME}}", variant)
        .replace("{{BACKGROUND}}", background.as_deref().unwrap_or("none"))
        .replace("{{BACKGROUND_OPACITY}}", &opacity.to_string())
        .replace("{{BACKGROUND_BLUR}}", &format!("{blur}px"));

    let config = json!({
        "themeId": summary.id,
        "home": home,
        "copy": copy,
        "background": background,
        "backgroundFit": background_fit,
        "logo": logo,
        "pet": pet
    });
    let javascript = format!(
        "const __codexSkinConfig={};\n{}",
        serde_json::to_string(&config)?,
        JS_TEMPLATE
    );
    let payload = Payload {
        css,
        javascript,
        theme_id: Some(summary.id),
    };
    write_state(&raw, &payload)?;
    Ok(payload)
}

fn color(value: Option<&Value>, fallback: &str) -> Result<String> {
    let value = value.and_then(Value::as_str).unwrap_or(fallback);
    if value.len() == 7
        && value.starts_with('#')
        && value[1..].chars().all(|c| c.is_ascii_hexdigit())
    {
        Ok(value.to_ascii_lowercase())
    } else {
        Err(AppError::Message(format!("主题颜色无效：{value}")))
    }
}
fn rgb(value: &str) -> Result<(u8, u8, u8)> {
    Ok((
        u8::from_str_radix(&value[1..3], 16).map_err(|_| AppError::Message("颜色无效。".into()))?,
        u8::from_str_radix(&value[3..5], 16).map_err(|_| AppError::Message("颜色无效。".into()))?,
        u8::from_str_radix(&value[5..7], 16).map_err(|_| AppError::Message("颜色无效。".into()))?,
    ))
}
fn mix(left: &str, right: &str, amount: f64) -> Result<String> {
    let (lr, lg, lb) = rgb(left)?;
    let (rr, rg, rb) = rgb(right)?;
    let blend = |a: u8, b: u8| ((a as f64) * (1.0 - amount) + (b as f64) * amount).round() as u8;
    Ok(format!(
        "#{:02x}{:02x}{:02x}",
        blend(lr, rr),
        blend(lg, rg),
        blend(lb, rb)
    ))
}
fn alpha(value: &str, amount: f64) -> Result<String> {
    let (r, g, b) = rgb(value)?;
    Ok(format!("rgb({r} {g} {b} / {amount:.3})"))
}

fn asset(value: Option<&Value>, manifest_dir: &Path, expected: &str) -> Result<Option<PathBuf>> {
    let Some(relative) = value
        .and_then(Value::as_str)
        .filter(|value| !value.is_empty())
    else {
        return Ok(None);
    };
    let normalized = relative.replace('\\', "/");
    if !normalized.starts_with(&format!("../{expected}/")) || normalized.contains("/../") {
        return Err(AppError::Message(format!("主题资源路径无效：{relative}")));
    }
    let path = manifest_dir.join(relative).canonicalize()?;
    if !path.is_file() {
        return Err(AppError::Message(format!(
            "主题资源不存在：{}",
            path.display()
        )));
    }
    Ok(Some(path))
}

fn package_asset(value: Option<&Value>, manifest_dir: &Path) -> Result<Option<PathBuf>> {
    let Some(name) = value.and_then(Value::as_str) else {
        return Ok(None);
    };
    if name.is_empty()
        || name.contains('/')
        || name.contains('\\')
        || Path::new(name).file_name().and_then(|v| v.to_str()) != Some(name)
    {
        return Err(AppError::Message(format!("签名主题资源路径无效：{name}")));
    }
    let path = manifest_dir.join(name).canonicalize()?;
    if !path.is_file() {
        return Err(AppError::Message(format!(
            "签名主题资源不存在：{}",
            path.display()
        )));
    }
    Ok(Some(path))
}

fn data_url(path: Option<PathBuf>) -> Result<Option<String>> {
    let Some(path) = path else {
        return Ok(None);
    };
    let extension = path
        .extension()
        .and_then(|v| v.to_str())
        .unwrap_or("")
        .to_ascii_lowercase();
    let mime = match extension.as_str() {
        "png" => "image/png",
        "jpg" | "jpeg" => "image/jpeg",
        "webp" => "image/webp",
        "avif" => "image/avif",
        _ => return Err(AppError::Message("不支持的主题图片格式。".into())),
    };
    let bytes = fs::read(&path)?;
    if bytes.len() > 20 * 1024 * 1024 {
        return Err(AppError::Message("主题图片超过 20 MB 限制。".into()));
    }
    image::load_from_memory(&bytes)
        .map_err(|error| AppError::Message(format!("主题图片无法解码：{error}")))?;
    Ok(Some(format!(
        "url(\"data:{mime};base64,{}\")",
        base64::engine::general_purpose::STANDARD.encode(bytes)
    )))
}

fn write_state(raw: &str, payload: &Payload) -> Result<()> {
    let root = paths::state_root()?;
    fs::create_dir_all(&root)?;
    for (name, content) in [
        ("codex-theme.css", payload.css.as_str()),
        ("codex-theme.js", payload.javascript.as_str()),
        ("current-theme.json", raw),
    ] {
        let file = AtomicFile::new(root.join(name), AllowOverwrite);
        file.write(|handle| handle.write_all(content.as_bytes()))
            .map_err(|error| AppError::Message(format!("写入主题状态失败：{error}")))?;
    }
    Ok(())
}

const CSS_TEMPLATE: &str = r#"
:root,html[data-codex-window-type="electron"],html[data-codex-window-type="electron"] body {
  color-scheme:{{COLOR_SCHEME}}!important; --codex-theme-id:"{{ID}}"; --codex-theme-accent:{{ACCENT}};
  --color-background-surface:{{SURFACE}}!important; --color-background-surface-under:{{UNDER}}!important; --color-background-panel:{{SURFACE}}!important; --color-background-editor-opaque:{{SURFACE}}!important;
  --color-background-elevated-primary:{{ELEVATED}}!important; --color-background-elevated-secondary:{{ELEVATED}}!important;
  --color-background-elevated-primary-opaque:{{ELEVATED}}!important; --color-background-elevated-secondary-opaque:{{ELEVATED}}!important;
  --color-background-control:{{ELEVATED}}!important; --color-background-control-opaque:{{ELEVATED}}!important;
  --color-background-button-secondary:{{ELEVATED}}!important; --color-background-button-tertiary-hover:rgb(from {{ACCENT}} r g b / .14)!important;
  --color-background-button-primary:{{ACCENT}}!important; --color-background-accent:rgb(from {{ACCENT}} r g b / .14)!important;
  --color-background-accent-hover:rgb(from {{ACCENT}} r g b / .20)!important; --color-background-accent-active:rgb(from {{ACCENT}} r g b / .26)!important;
  --color-text-foreground:{{INK}}!important; --color-text-foreground-secondary:{{SECONDARY}}!important; --color-text-foreground-tertiary:{{TERTIARY}}!important; --color-text-accent:{{ACCENT}}!important;
  --color-text-button-primary:{{BUTTON_TEXT}}!important; --color-text-button-secondary:{{INK}}!important; --color-text-button-tertiary:{{INK}}!important; --color-text-on-accent:{{BUTTON_TEXT}}!important;
  --color-icon-primary:{{INK}}!important; --color-icon-secondary:{{SECONDARY}}!important; --color-icon-tertiary:{{TERTIARY}}!important; --color-icon-accent:{{ACCENT}}!important;
  --color-border:{{BORDER}}!important; --color-border-light:rgb(from {{INK}} r g b / .12)!important; --color-border-heavy:rgb(from {{INK}} r g b / .20)!important; --color-border-focus:{{ACCENT}}!important; --color-accent-blue:{{ACCENT}}!important;
  --color-accent-green:{{ADDED}}!important; --color-accent-orange:{{REMOVED}}!important; --color-accent-purple:{{SKILL}}!important;
  --color-token-main-surface-primary:{{SURFACE}}!important; --color-token-side-bar-background:{{UNDER}}!important;
  --color-token-input-background:{{ELEVATED}}!important; --color-token-input-foreground:{{INK}}!important; --color-token-input-placeholder-foreground:{{TERTIARY}}!important; --color-token-input-border:{{BORDER}}!important;
  --color-token-dropdown-background:{{ELEVATED}}!important; --color-token-dropdown-foreground:{{INK}}!important; --color-token-menu-background:{{ELEVATED}}!important; --color-token-menu-foreground:{{INK}}!important; --color-token-menu-border:{{BORDER}}!important;
  --color-token-editor-widget-background:{{ELEVATED}}!important; --color-token-diff-surface:color-mix(in srgb,{{SURFACE}} 92%,{{INK}})!important;
  --color-token-text-primary:{{INK}}!important; --color-token-text-secondary:{{SECONDARY}}!important; --color-token-text-tertiary:{{TERTIARY}}!important; --color-token-conversation-body:{{SECONDARY}}!important;
  --color-token-text-preformat-foreground:{{INK}}!important; --color-token-text-preformat-background:rgb(from {{INK}} r g b / .08)!important; --color-token-text-code-block-background:rgb(from {{INK}} r g b / .08)!important;
  --color-token-border:{{BORDER}}!important; --color-token-border-default:{{BORDER}}!important; --color-token-border-light:rgb(from {{INK}} r g b / .10)!important; --color-token-border-heavy:rgb(from {{INK}} r g b / .18)!important;
  --vscode-font-family:{{UI_FONT}}!important; --font-sans-default:{{UI_FONT}}!important; --font-mono-default:{{CODE_FONT}}!important;
  --vscode-foreground:{{INK}}!important; --vscode-descriptionForeground:{{SECONDARY}}!important; --vscode-focusBorder:{{ACCENT}}!important;
  --vscode-editor-background:{{SURFACE}}!important; --vscode-editor-foreground:{{INK}}!important; --vscode-sideBar-background:{{UNDER}}!important;
  --vscode-input-background:{{ELEVATED}}!important; --vscode-input-foreground:{{INK}}!important; --vscode-input-placeholderForeground:{{TERTIARY}}!important;
  --vscode-dropdown-background:{{ELEVATED}}!important; --vscode-dropdown-foreground:{{INK}}!important; --vscode-menu-background:{{ELEVATED}}!important; --vscode-menu-foreground:{{INK}}!important; --vscode-menu-border:{{BORDER}}!important;
  --vscode-widget-background:{{ELEVATED}}!important; --vscode-widget-foreground:{{INK}}!important; --vscode-widget-border:{{BORDER}}!important; --vscode-widget-shadow:rgb(from {{SHADOW}} r g b / .38)!important;
  --vscode-chat-editedFileForeground:{{INK}}!important; --vscode-chat-requestBackground:{{ELEVATED}}!important; --vscode-chat-requestBorder:{{BORDER}}!important;
  --vscode-button-background:{{ACCENT}}!important; --vscode-button-foreground:{{BUTTON_TEXT}}!important; --vscode-button-secondaryBackground:{{ELEVATED}}!important; --vscode-button-secondaryForeground:{{INK}}!important;
}
html[data-codex-window-type="electron"].electron-opaque,html[data-codex-window-type="electron"].electron-opaque body { background:{{UNDER}} !important; }
html[data-codex-window-type="electron"] .main-surface,html[data-codex-window-type="electron"] .browser-main-surface { background:rgb(from {{SURFACE}} r g b / .72)!important; box-shadow:0 0 0 1px {{BORDER}},0 18px 48px rgb(from {{SHADOW}} r g b / .18)!important; }
html[data-codex-window-type="electron"] .app-shell-left-panel { background:rgb(from {{UNDER}} r g b / .90)!important; border-right:1px solid {{BORDER}}!important; }
html[data-codex-window-type="electron"] .app-shell-left-panel :is(button,a):hover { color:{{ACCENT}}!important; background:rgb(from {{ACCENT}} r g b / .12)!important; }
html[data-codex-window-type="electron"] .composer-surface-chrome { color:{{INK}}!important; background:rgb(from {{ELEVATED}} r g b / .94)!important; border:1px solid {{BORDER}}!important; border-radius:20px!important; box-shadow:0 12px 34px rgb(from {{SHADOW}} r g b / .28)!important; }
html[data-codex-window-type="electron"] .composer-surface-chrome .ProseMirror { color:{{INK}}!important; caret-color:{{ACCENT}}!important; }
html[data-codex-window-type="electron"] :is([role="dialog"],[role="menu"],[role="listbox"],[data-radix-popper-content-wrapper] > *) { color:{{INK}}!important; background:rgb(from {{ELEVATED}} r g b / .98)!important; border-color:{{BORDER}}!important; box-shadow:0 20px 54px rgb(from {{SHADOW}} r g b / .38)!important; }
html[data-codex-window-type="electron"] :is([role="dialog"],[role="menu"],[role="listbox"],[data-radix-popper-content-wrapper] > *) :is(button,a,[role="menuitem"],[role="option"]) { color:inherit; }
html[data-codex-window-type="electron"] [role="tooltip"] { color:{{INK}}!important; background:{{ELEVATED}}!important; border:1px solid {{BORDER}}!important; }
html[data-codex-window-type="electron"] [data-content-search-unit-key$=":assistant"],html[data-codex-window-type="electron"] [data-message-author-role="assistant"] { color:{{INK}}!important; }
html[data-codex-window-type="electron"] [data-user-message-bubble="true"] { background:rgb(from {{ACCENT}} r g b / .15)!important; border:1px solid {{BORDER}}!important; }
html[data-codex-window-type="electron"] #codex-theme-home { display:none; position:absolute!important; inset:44px 0 clamp(118px,15vh,145px); z-index:6!important; container-type:inline-size; overflow:auto; overscroll-behavior:contain; padding:clamp(14px,2.4vw,24px) clamp(16px,2.8vw,30px); color:{{INK}}; background:{{SURFACE}}; font-family:{{UI_FONT}}; }
html[data-codex-window-type="electron"] .codex-theme-home-active #codex-theme-home { display:block; }
.codex-theme-sidebar-logo { position:absolute; top:10px; left:16px; z-index:20; width:min(112px,55%); height:30px; padding-right:6px; object-fit:contain; object-position:left center; pointer-events:none; background:{{UNDER}}; }
.codex-theme-home-shell { width:min(1440px,100%); min-width:0; margin:auto; }
.codex-theme-home-hero { position:relative; min-height:340px; height:clamp(340px,42vh,480px); display:block; overflow:hidden; isolation:isolate; padding:0; border:1px solid {{BORDER}}; border-radius:clamp(18px,2.2cqw,28px); background:{{SURFACE}}; box-shadow:0 18px 44px rgb(from {{SHADOW}} r g b / .18); }
.codex-theme-home-hero::before { content:""; position:absolute; inset:0; background-image:{{BACKGROUND}}; background-size:cover; background-position:{{BACKGROUND_POSITION}}; opacity:{{BACKGROUND_OPACITY}}; filter:blur({{BACKGROUND_BLUR}}); transform:scale(1.02); }
.codex-theme-home-image { position:absolute; inset:0; z-index:1; width:100%; height:100%; display:none; object-fit:contain; object-position:{{BACKGROUND_POSITION}}; opacity:{{BACKGROUND_OPACITY}}; pointer-events:none; user-select:none; }
.codex-theme-home-hero[data-image-fit="contain"]::before { opacity:.48; filter:blur(18px); transform:scale(1.1); }
.codex-theme-home-hero[data-image-fit="contain"] .codex-theme-home-image { display:block; }
.codex-theme-home-copy { position:absolute; inset:46% auto auto clamp(24px,3.2cqw,42px); width:min(46%,600px); min-width:0; z-index:2; transform:translateY(-14%); color:{{INK}}; text-shadow:0 1px 10px rgb(from {{SURFACE}} r g b / .72); }
.codex-theme-home-brand { display:none; }
.codex-theme-home-eyebrow { display:inline-flex; margin:0 0 12px; padding:6px 10px; color:{{INK}}; background:rgb(from {{SURFACE}} r g b / .76); border:1px solid {{BORDER}}; border-radius:999px; backdrop-filter:blur(10px); font-size:11px; letter-spacing:.08em; }
.codex-theme-home-title { margin:0 0 10px; font-family:{{DISPLAY_FONT}}; font-size:clamp(28px,3.2cqw,44px); line-height:1.16; font-weight:900; letter-spacing:-.035em; overflow-wrap:anywhere; }
.codex-theme-home-subtitle { max-width:520px; margin:0; color:{{SECONDARY}}; font-size:clamp(14px,1.35cqw,18px); line-height:1.55; font-weight:650; overflow-wrap:anywhere; }
.codex-theme-home-badge { position:absolute; top:clamp(16px,2cqw,26px); right:clamp(16px,2cqw,26px); z-index:2; max-width:36%; padding:7px 12px; overflow:hidden; color:{{ACCENT}}; background:rgb(from {{SURFACE}} r g b / .82); border:1px dashed {{ACCENT}}; border-radius:999px; backdrop-filter:blur(10px); font-size:11px; font-weight:800; text-overflow:ellipsis; white-space:nowrap; }
.codex-theme-home-tags { display:flex; flex-wrap:wrap; gap:7px; margin-top:15px; }
.codex-theme-home-tag { padding:5px 9px; color:{{INK}}; background:rgb(from {{SURFACE}} r g b / .78); border:1px solid {{BORDER}}; border-radius:999px; backdrop-filter:blur(9px); font-size:11px; font-weight:750; }
.codex-theme-home-actions { display:grid; grid-template-columns:repeat(4,minmax(0,1fr)); gap:clamp(10px,1.2cqw,14px); margin-top:clamp(12px,1.5cqw,18px); }
.codex-theme-home-action { min-width:0; min-height:clamp(138px,13cqw,154px); display:grid; grid-template-rows:42px auto 1fr; justify-items:center; gap:8px; padding:12px; text-align:center; overflow-wrap:anywhere; color:{{INK}}; background:rgb(from {{ELEVATED}} r g b / .96); border:1px solid {{BORDER}}; border-radius:clamp(18px,2cqw,24px); box-shadow:0 12px 28px rgb(from {{SHADOW}} r g b / .18); cursor:pointer; transition:transform .16s ease,border-color .16s ease,box-shadow .16s ease; }
.codex-theme-home-action:nth-child(1) { background:color-mix(in srgb,{{ELEVATED}} 88%,{{REMOVED}}); }
.codex-theme-home-action:nth-child(2) { background:color-mix(in srgb,{{ELEVATED}} 88%,{{ADDED}}); }
.codex-theme-home-action:nth-child(3) { background:color-mix(in srgb,{{ELEVATED}} 88%,{{ACCENT}}); }
.codex-theme-home-action:nth-child(4) { background:color-mix(in srgb,{{ELEVATED}} 88%,{{SKILL}}); }
.codex-theme-home-action:focus-visible { outline:2px solid {{ACCENT}}; outline-offset:2px; }
@media(hover:hover){.codex-theme-home-action:hover { transform:translateY(-4px); border-color:{{ACCENT}}; box-shadow:0 18px 34px rgb(from {{SHADOW}} r g b / .26); }}
.codex-theme-home-action-icon { width:42px; height:42px; display:grid; place-items:center; color:{{ACCENT}}; background:rgb(from {{SURFACE}} r g b / .84); border:1px solid {{BORDER}}; border-radius:50%; font-family:{{CODE_FONT}}; font-size:18px; }
.codex-theme-home-action-title { align-self:center; font-size:15px; line-height:1.35; font-weight:850; }
.codex-theme-home-action-description { margin-top:6px; color:{{SECONDARY}}; font-size:12px; line-height:1.5; }
.codex-theme-home-pet { position:absolute; right:clamp(12px,2cqw,24px); bottom:12px; max-width:clamp(112px,16cqw,180px); max-height:clamp(112px,16cqw,180px); object-fit:contain; z-index:1; }
.codex-theme-home-footer { margin:14px 4px 0; color:{{SECONDARY}}; font-size:12px; text-align:right; }
@container(max-width:820px){.codex-theme-home-actions{grid-template-columns:repeat(2,minmax(0,1fr))}.codex-theme-home-copy{width:60%}.codex-theme-home-hero{min-height:310px;height:clamp(310px,40vh,400px)}}
@container(max-width:520px){.codex-theme-home-hero{min-height:300px}.codex-theme-home-copy{inset-inline:20px;width:auto}.codex-theme-home-badge,.codex-theme-home-tags{display:none}.codex-theme-home-actions{grid-template-columns:1fr}.codex-theme-home-action{min-height:118px}.codex-theme-home-pet{opacity:.58}.codex-theme-home-footer{text-align:left}}
@media(max-height:760px){html[data-codex-window-type="electron"] #codex-theme-home{inset-bottom:112px;padding-block:12px}.codex-theme-home-hero{min-height:280px;height:280px}.codex-theme-home-action{min-height:124px;padding-block:10px}.codex-theme-home-actions{margin-top:12px}}
@media(prefers-reduced-motion:reduce){.codex-theme-home-action{transition:none}}
"#;

const JS_TEMPLATE: &str = r#"
(() => {
  globalThis.__codexThemeStore?.dispose?.();
  const cfg=__codexSkinConfig, make=(tag,cls,text)=>{const el=document.createElement(tag);if(cls)el.className=cls;if(text!=null)el.textContent=String(text);return el;},assetUrl=value=>value?.startsWith('url("')?value.slice(5,-2):'';
  let home=document.getElementById('codex-theme-home'); if(home)home.remove(); home=make('section');home.id='codex-theme-home';home.dataset.themeId=cfg.themeId;
  const shell=make('div','codex-theme-home-shell'),hero=make('div','codex-theme-home-hero'),copy=make('div','codex-theme-home-copy');
  let backgroundImage=null;if(cfg.background){backgroundImage=make('img','codex-theme-home-image');backgroundImage.src=assetUrl(cfg.background);backgroundImage.alt='';backgroundImage.decoding='async';backgroundImage.draggable=false;hero.append(backgroundImage);}
  for(const [tag,cls,key] of [['h1','codex-theme-home-brand','brand'],['p','codex-theme-home-eyebrow','eyebrow'],['h2','codex-theme-home-title','title'],['p','codex-theme-home-subtitle','subtitle']]){if(cfg.home?.[key])copy.append(make(tag,cls,cfg.home[key]));}
  if(cfg.home?.tags?.length){const tags=make('div','codex-theme-home-tags');for(const value of cfg.home.tags)tags.append(make('span','codex-theme-home-tag',value));copy.append(tags);}
  hero.append(copy); if(cfg.pet){const pet=make('img','codex-theme-home-pet');pet.src=cfg.pet.slice(5,-2);pet.alt=cfg.home?.pet?.alt||'';hero.append(pet);} shell.append(hero);
  if(cfg.home?.badge){hero.append(make('div','codex-theme-home-badge',`${cfg.home.badge} ${String(cfg.themeId).toUpperCase()}`));}
  const actions=make('div','codex-theme-home-actions'); for(const [index,action] of (cfg.home?.quickActions||[]).entries()){const button=make('button','codex-theme-home-action');button.type='button';button.append(make('span','codex-theme-home-action-icon',action.icon||String(index+1)),make('div','codex-theme-home-action-title',action.title),make('div','codex-theme-home-action-description',action.description||''));button.addEventListener('click',()=>{const input=document.querySelector('textarea,[contenteditable="true"]');if(!input)return;if('value' in input)input.value=action.prompt;else input.textContent=action.prompt;input.dispatchEvent(new InputEvent('input',{bubbles:true,inputType:'insertText',data:action.prompt}));input.focus();});actions.append(button);} shell.append(actions);home.append(shell);
  if(cfg.home?.footerNote)shell.append(make('div','codex-theme-home-footer',cfg.home.footerNote));
  const main=document.querySelector('.main-surface,.browser-main-surface,main.main-surface')||document.body;main.append(home);document.documentElement.dataset.codexThemeId=cfg.themeId;
  let sidebarLogo=null;
  const updateHeroFit=()=>{if(!backgroundImage)return;const requested=cfg.backgroundFit||'smart';let fit=requested;if(requested==='smart'&&backgroundImage.naturalWidth&&backgroundImage.naturalHeight){const rect=hero.getBoundingClientRect(),imageRatio=backgroundImage.naturalWidth/backgroundImage.naturalHeight,boxRatio=rect.width/Math.max(1,rect.height),visibleFraction=Math.min(imageRatio/boxRatio,boxRatio/imageRatio);fit=1-visibleFraction>.34?'contain':'cover';}hero.dataset.imageFit=fit==='contain'?'contain':'cover';};
  const applyCopy=()=>{if(cfg.copy?.title)document.title=cfg.copy.title;const replacements=cfg.copy?.replacePlaceholders||{};for(const node of document.querySelectorAll('[placeholder],[data-placeholder]'))for(const attr of ['placeholder','data-placeholder']){const value=node.getAttribute(attr);if(value&&replacements[value])node.setAttribute(attr,replacements[value]);}const editor=document.querySelector('.ProseMirror,[contenteditable="true"][role="textbox"]');if(editor&&cfg.home?.composerHint){editor.setAttribute('aria-label',cfg.home.composerHint);editor.dataset.placeholder=cfg.home.composerHint;}};
  const ensureSidebarLogo=()=>{if(sidebarLogo?.isConnected||!cfg.logo)return;const sidebar=document.querySelector('.app-shell-left-panel');if(!sidebar)return;sidebarLogo=make('img','codex-theme-sidebar-logo');sidebarLogo.src=assetUrl(cfg.logo);sidebarLogo.alt=cfg.home?.brand||'';sidebarLogo.decoding='async';sidebar.append(sidebarLogo);};
  const composerRegionTop=(composer,mainRect)=>{const composerRect=composer.getBoundingClientRect();let top=composerRect.top;const maxRegionHeight=Math.min(480,mainRect.height*.55);for(let node=composer.parentElement;node&&node!==main;node=node.parentElement){const rect=node.getBoundingClientRect();const hugsBottom=rect.bottom>=composerRect.bottom-24&&rect.bottom<=mainRect.bottom+24;const isComposerRegion=rect.height>0&&rect.height<=maxRegionHeight&&rect.top>=mainRect.top+44;if(hugsBottom&&isComposerRegion)top=Math.min(top,rect.top);}return top;};
  const update=()=>{const hasConversation=!!main.querySelector('[data-message-author-role],[data-user-message-bubble="true"],[data-content-search-unit-key$=":assistant"]');main.classList.toggle('codex-theme-home-active',!hasConversation);const composer=document.querySelector('.composer-surface-chrome');if(composer){const mainRect=main.getBoundingClientRect(),regionTop=composerRegionTop(composer,mainRect),reserve=Math.min(500,Math.max(148,Math.ceil(mainRect.bottom-regionTop+28)));home.style.bottom=`${reserve}px`;}else home.style.removeProperty('bottom');updateHeroFit();applyCopy();ensureSidebarLogo();};
  const observer=new MutationObserver(update);observer.observe(main,{childList:true,subtree:true});update();
  backgroundImage?.addEventListener('load',updateHeroFit);
  const heroResizeObserver=new ResizeObserver(updateHeroFit);heroResizeObserver.observe(hero);
  const resizeHandler=()=>update();window.addEventListener('resize',resizeHandler);
  globalThis.__codexThemeStore={observer,resizeHandler,dispose(){observer.disconnect();heroResizeObserver.disconnect();window.removeEventListener('resize',resizeHandler);home.remove();sidebarLogo?.remove();main.classList.remove('codex-theme-home-active');}};
  return true;
})();
"#;
