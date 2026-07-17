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
    let border = alpha(&accent, if dark { 0.34 } else { 0.22 })?;
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
        ("BORDER", &border),
        ("ADDED", &added),
        ("REMOVED", &removed),
        ("SKILL", &skill),
        ("UI_FONT", ui_font),
        ("CODE_FONT", code_font),
    ] {
        css = css.replace(&format!("{{{{{key}}}}}"), value);
    }
    css = css
        .replace("{{COLOR_SCHEME}}", variant)
        .replace("{{BACKGROUND}}", background.as_deref().unwrap_or("none"))
        .replace("{{BACKGROUND_OPACITY}}", &opacity.to_string())
        .replace("{{BACKGROUND_BLUR}}", &format!("{blur}px"));

    let config =
        json!({ "themeId": summary.id, "home": home, "copy": copy, "logo": logo, "pet": pet });
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
  color-scheme: {{COLOR_SCHEME}}; --codex-theme-id:"{{ID}}"; --codex-theme-accent:{{ACCENT}};
  --color-background-surface:{{SURFACE}}; --color-background-surface-under:{{UNDER}};
  --color-background-elevated-primary:{{ELEVATED}}; --color-background-elevated-secondary:{{ELEVATED}};
  --color-background-button-primary:{{ACCENT}}; --color-background-accent:rgb(from {{ACCENT}} r g b / .14);
  --color-text-foreground:{{INK}}; --color-text-foreground-secondary:{{SECONDARY}}; --color-text-accent:{{ACCENT}};
  --color-border:{{BORDER}}; --color-border-focus:{{ACCENT}}; --color-accent-blue:{{ACCENT}};
  --color-accent-green:{{ADDED}}; --color-accent-orange:{{REMOVED}}; --color-accent-purple:{{SKILL}};
  --vscode-font-family:{{UI_FONT}}; --font-sans-default:{{UI_FONT}}; --font-mono-default:{{CODE_FONT}};
  --vscode-foreground:{{INK}}; --vscode-descriptionForeground:{{SECONDARY}}; --vscode-focusBorder:{{ACCENT}};
  --vscode-editor-background:{{SURFACE}}; --vscode-editor-foreground:{{INK}}; --vscode-sideBar-background:{{UNDER}};
  --vscode-input-background:{{ELEVATED}}; --vscode-input-foreground:{{INK}}; --vscode-button-background:{{ACCENT}};
}
html[data-codex-window-type="electron"].electron-opaque,html[data-codex-window-type="electron"].electron-opaque body { background:{{UNDER}} !important; }
html[data-codex-window-type="electron"] .main-surface,html[data-codex-window-type="electron"] .browser-main-surface { background:rgb(from {{SURFACE}} r g b / .72)!important; box-shadow:0 0 0 1px {{BORDER}},0 18px 48px rgb(from {{INK}} r g b / .12)!important; }
html[data-codex-window-type="electron"] .app-shell-left-panel { background:rgb(from {{UNDER}} r g b / .90)!important; border-right:1px solid {{BORDER}}!important; }
html[data-codex-window-type="electron"] .app-shell-left-panel :is(button,a):hover { color:{{ACCENT}}!important; background:rgb(from {{ACCENT}} r g b / .12)!important; }
html[data-codex-window-type="electron"] .composer-surface-chrome { background:rgb(from {{ELEVATED}} r g b / .92)!important; border:1px solid {{ACCENT}}!important; border-radius:20px!important; box-shadow:0 12px 34px rgb(from {{INK}} r g b / .16)!important; }
html[data-codex-window-type="electron"] [data-content-search-unit-key$=":assistant"],html[data-codex-window-type="electron"] [data-message-author-role="assistant"] { margin-block:4px 10px!important; padding:14px 16px!important; background:rgb(from {{SURFACE}} r g b / .78)!important; border:1px solid {{BORDER}}!important; border-radius:16px!important; }
html[data-codex-window-type="electron"] [data-user-message-bubble="true"] { background:rgb(from {{ACCENT}} r g b / .15)!important; border:1px solid {{BORDER}}!important; }
html[data-codex-window-type="electron"] #codex-theme-home { display:none; position:absolute!important; inset:44px 0 145px; z-index:6!important; overflow:auto; padding:22px 30px; color:{{INK}}; background:{{SURFACE}}; font-family:{{UI_FONT}}; }
html[data-codex-window-type="electron"] .codex-theme-home-active #codex-theme-home { display:block; }
.codex-theme-home-shell { width:min(1440px,100%); margin:auto; }.codex-theme-home-hero { position:relative; min-height:300px; display:flex; align-items:center; overflow:hidden; padding:40px; border:1px solid {{BORDER}}; border-radius:28px; background:{{SURFACE}}; box-shadow:0 18px 44px rgb(from {{INK}} r g b / .12); }
.codex-theme-home-hero::before { content:""; position:absolute; inset:0; background-image:{{BACKGROUND}}; background-size:cover; background-position:center; opacity:{{BACKGROUND_OPACITY}}; filter:blur({{BACKGROUND_BLUR}}); transform:scale(1.04); }.codex-theme-home-copy { position:relative; width:min(600px,62%); z-index:1; }.codex-theme-home-logo { max-width:180px; max-height:64px; object-fit:contain; }.codex-theme-home-brand { margin:10px 0 4px; color:{{ACCENT}}; font-size:38px; }.codex-theme-home-title { margin:14px 0 8px; font-size:32px; }.codex-theme-home-subtitle { color:{{SECONDARY}}; line-height:1.6; }
.codex-theme-home-actions { display:grid; grid-template-columns:repeat(4,minmax(0,1fr)); gap:14px; margin-top:18px; }.codex-theme-home-action { min-height:142px; padding:18px; text-align:left; color:{{INK}}; background:{{ELEVATED}}; border:1px solid {{BORDER}}; border-radius:22px; cursor:pointer; }.codex-theme-home-action:hover { transform:translateY(-2px); border-color:{{ACCENT}}; }.codex-theme-home-action-icon { width:42px; height:42px; display:grid; place-items:center; margin-bottom:12px; color:white; background:{{ACCENT}}; border-radius:50%; font-family:{{CODE_FONT}}; }.codex-theme-home-action-title { font-weight:700; }.codex-theme-home-action-description { margin-top:6px; color:{{SECONDARY}}; font-size:12px; line-height:1.5; }.codex-theme-home-pet { position:absolute; right:24px; bottom:12px; max-width:180px; max-height:180px; object-fit:contain; z-index:1; }
@media(max-width:1000px){.codex-theme-home-actions{grid-template-columns:repeat(2,1fr)}.codex-theme-home-copy{width:75%}}@media(max-width:720px){html[data-codex-window-type="electron"] #codex-theme-home{padding-inline:16px}.codex-theme-home-hero{padding:25px}.codex-theme-home-actions{grid-template-columns:1fr}}
"#;

const JS_TEMPLATE: &str = r#"
(() => {
  globalThis.__codexThemeStore?.dispose?.();
  const cfg=__codexSkinConfig, make=(tag,cls,text)=>{const el=document.createElement(tag);if(cls)el.className=cls;if(text!=null)el.textContent=String(text);return el;};
  let home=document.getElementById('codex-theme-home'); if(home)home.remove(); home=make('section');home.id='codex-theme-home';home.dataset.themeId=cfg.themeId;
  const shell=make('div','codex-theme-home-shell'),hero=make('div','codex-theme-home-hero'),copy=make('div','codex-theme-home-copy');
  if(cfg.logo){const logo=make('img','codex-theme-home-logo');logo.src=cfg.logo.slice(5,-2);logo.alt='';copy.append(logo);}
  for(const [tag,cls,key] of [['h1','codex-theme-home-brand','brand'],['p','codex-theme-home-eyebrow','eyebrow'],['h2','codex-theme-home-title','title'],['p','codex-theme-home-subtitle','subtitle']]){if(cfg.home?.[key])copy.append(make(tag,cls,cfg.home[key]));}
  hero.append(copy); if(cfg.pet){const pet=make('img','codex-theme-home-pet');pet.src=cfg.pet.slice(5,-2);pet.alt=cfg.home?.pet?.alt||'';hero.append(pet);} shell.append(hero);
  const actions=make('div','codex-theme-home-actions'); for(const [index,action] of (cfg.home?.quickActions||[]).entries()){const button=make('button','codex-theme-home-action');button.type='button';button.append(make('span','codex-theme-home-action-icon',action.icon||String(index+1)),make('div','codex-theme-home-action-title',action.title),make('div','codex-theme-home-action-description',action.description||''));button.addEventListener('click',()=>{const input=document.querySelector('textarea,[contenteditable="true"]');if(!input)return;if('value' in input)input.value=action.prompt;else input.textContent=action.prompt;input.dispatchEvent(new InputEvent('input',{bubbles:true,inputType:'insertText',data:action.prompt}));input.focus();});actions.append(button);} shell.append(actions);home.append(shell);
  const main=document.querySelector('.main-surface,.browser-main-surface,main.main-surface')||document.body;main.append(home);document.documentElement.dataset.codexThemeId=cfg.themeId;
  const update=()=>{const hasConversation=!!document.querySelector('[data-message-author-role],[data-user-message-bubble="true"],[data-content-search-unit-key$=":assistant"]');main.classList.toggle('codex-theme-home-active',!hasConversation);};
  const observer=new MutationObserver(update);observer.observe(main,{childList:true,subtree:true});update();
  globalThis.__codexThemeStore={observer,dispose(){observer.disconnect();home.remove();main.classList.remove('codex-theme-home-active');}};
  return true;
})();
"#;
