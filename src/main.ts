import { invoke } from "@tauri-apps/api/core";
import { listen } from "@tauri-apps/api/event";
import "./styles.css";

type Source = { id: string; name: string };
type Theme = {
  id: string;
  version: string;
  name: string;
  description: string;
  category: string;
  previewPath: string;
};
type AppState = { themes: Theme[]; sources: Source[]; selectedSourceId: string };

const qaMode = import.meta.env.DEV && new URLSearchParams(location.search).get("qa") === "1";
const qaThemes: Theme[] = [
  ["dilraba-star", "迪丽热巴 · 星愿", "星光舞台与暖金色卡片", "人物", "/qa/captures/dilraba-star-home-final-v5.png"],
  ["enfp-pop", "ENFP 多巴胺", "明亮活泼的彩色工作空间", "其他", "/qa/captures/enfp-pop-home-final-v3.png"],
  ["jackson-sage", "千玺 · 鼠尾草", "克制自然的鼠尾草绿", "人物", "/qa/captures/jackson-sage-home-final-v3.png"],
  ["kun-stage", "舞台 · 银蓝", "深色舞台与银蓝高光", "人物", "/qa/captures/kun-stage-home-final-v3.png"],
].map(([id, name, description, category, previewPath]) => ({ id, version: "1.0.0", name, description, category, previewPath }));

async function call<T>(command: string, args?: Record<string, unknown>): Promise<T> {
  if (!qaMode) return invoke<T>(command, args);
  if (command === "get_app_state") return { themes: qaThemes, sources: [{ id: "github", name: "GitHub 官方" }, { id: "gh-proxy", name: "GH Proxy" }, { id: "ghfast", name: "GHFast" }], selectedSourceId: "github" } as T;
  if (command === "read_preview") return String(args?.path ?? "") as T;
  if (command === "pending_activations") return [] as T;
  if (command === "sync_catalog") return { themeCount: qaThemes.length, sourceId: "github", sourceName: "GitHub 官方" } as T;
  if (command === "theme_runtime_ready") return true as T;
  return "QA 操作已完成" as T;
}

const categories = ["全部", "人物", "动漫", "游戏", "风景", "极简", "节日", "其他"];
const previewCache = new Map<string, string>();
let state: AppState = { themes: [], sources: [], selectedSourceId: "github" };
let selectedCategory = "全部";
let selectedTheme: Theme | undefined;
let busy = false;

document.querySelector<HTMLDivElement>("#app")!.innerHTML = `
  <main class="shell">
    <header class="toolbar">
      <section class="brand"><h1>Codex-Skin</h1><p id="catalog-count">主题目录</p></section>
      <select id="source" aria-label="主题下载线路"></select>
      <button id="refresh" class="button">刷新</button>
      <nav id="categories" class="categories" aria-label="主题分类"></nav>
    </header>
    <div id="progress" class="progress" hidden><i></i></div>
    <section id="themes" class="theme-grid" aria-live="polite"></section>
    <footer class="command-bar">
      <section><strong id="selection">请选择主题</strong><span id="status">准备就绪</span></section>
      <button id="rollback" class="button">恢复默认</button>
      <button id="apply" class="button primary">应用主题</button>
    </footer>
  </main>
`;

const sourceSelect = document.querySelector<HTMLSelectElement>("#source")!;
const themeGrid = document.querySelector<HTMLElement>("#themes")!;
const status = document.querySelector<HTMLElement>("#status")!;

function message(value: string): void { status.textContent = value; }

function setBusy(value: boolean, text?: string): void {
  busy = value;
  document.querySelector<HTMLElement>("#progress")!.hidden = !value;
  for (const button of document.querySelectorAll<HTMLButtonElement>("button")) button.disabled = value;
  sourceSelect.disabled = value;
  if (text) message(text);
  updateCommands();
}

function updateCommands(): void {
  document.querySelector<HTMLButtonElement>("#apply")!.disabled = busy || !selectedTheme;
}

function renderSources(): void {
  sourceSelect.replaceChildren(...state.sources.map(source => {
    const option = document.createElement("option");
    option.value = source.id; option.textContent = source.name;
    option.selected = source.id === state.selectedSourceId;
    return option;
  }));
}

function renderCategories(): void {
  const host = document.querySelector<HTMLElement>("#categories")!;
  host.replaceChildren(...categories.map(category => {
    const button = document.createElement("button");
    button.className = `category${category === selectedCategory ? " selected" : ""}`;
    button.textContent = category;
    button.onclick = () => { selectedCategory = category; renderCategories(); renderThemes(); };
    return button;
  }));
}

async function loadPreview(image: HTMLImageElement, path: string): Promise<void> {
  try {
    let data = previewCache.get(path);
    if (!data) { data = await call<string>("read_preview", { path }); previewCache.set(path, data); }
    image.src = data;
  } catch { image.closest("article")?.classList.add("preview-error"); }
}

function renderThemes(): void {
  const visible = state.themes.filter(theme => selectedCategory === "全部" || theme.category === selectedCategory);
  document.querySelector("#catalog-count")!.textContent = selectedCategory === "全部"
    ? `${state.themes.length} 个主题` : `${visible.length} / ${state.themes.length} 个主题`;
  themeGrid.replaceChildren(...visible.map(theme => {
    const card = document.createElement("article");
    card.className = `theme-card${selectedTheme?.id === theme.id ? " selected" : ""}`;
    card.tabIndex = 0;
    card.innerHTML = `<div class="preview"><img alt="" /></div><div class="card-copy"><div><h2></h2><p></p></div><span></span></div>`;
    card.querySelector("h2")!.textContent = theme.name;
    card.querySelector("p")!.textContent = theme.description;
    card.querySelector("span")!.textContent = theme.category;
    const select = () => { selectedTheme = theme; document.querySelector("#selection")!.textContent = `已选择：${theme.name}`; renderThemes(); updateCommands(); };
    card.onclick = select;
    card.onkeydown = event => { if (event.key === "Enter" || event.key === " ") select(); };
    void loadPreview(card.querySelector("img")!, theme.previewPath);
    return card;
  }));
  if (!visible.length) themeGrid.innerHTML = `<div class="empty">暂无主题，点击“刷新”同步官方主题目录。</div>`;
}

async function loadState(): Promise<void> {
  try {
    state = await call<AppState>("get_app_state");
    selectedTheme = state.themes.find(theme => theme.id === selectedTheme?.id) ?? state.themes[0];
    renderSources(); renderCategories(); renderThemes();
    if (selectedTheme) document.querySelector("#selection")!.textContent = `已选择：${selectedTheme.name}`;
    message(state.themes.length ? "主题目录已加载" : "首次使用需要联网同步主题");
  } catch (error) { message(String(error)); }
  updateCommands();
}

async function handleActivation(values: string[]): Promise<void> {
  for (const value of values) {
    if (value.startsWith("dreamskin:")) {
      if (!window.confirm("是否从网页提供的地址下载主题？\n\n客户端将校验大小、SHA-256、Ed25519 签名和全部图片，下载后不会自动应用。")) continue;
      setBusy(true, "正在下载并验证签名主题…");
      try { const result = await call<{ name:string;version:string }>("install_uri", { uri:value, sourceId:sourceSelect.value }); await loadState(); message(`${result.name} ${result.version} 已安全安装`); }
      catch(error){message(String(error));} finally{setBusy(false);} continue;
    }
    if (!value.toLowerCase().endsWith(".dreamskin")) continue;
    if (!window.confirm(`是否校验并安装本地主题包？\n\n${value}`)) continue;
    setBusy(true, "正在校验主题签名和图片…");
    try {
      const result = await call<{ name: string; version: string; alreadyInstalled: boolean }>("import_local", { path: value });
      await loadState(); message(`${result.name} ${result.version} ${result.alreadyInstalled ? "已存在" : "已安全安装"}`);
    } catch (error) { message(String(error)); }
    finally { setBusy(false); }
  }
}

document.querySelector<HTMLButtonElement>("#refresh")!.onclick = async () => {
  setBusy(true, "正在同步主题目录…");
  try {
    const result = await call<{ themeCount: number; sourceId: string; sourceName: string }>("sync_catalog", { sourceId: sourceSelect.value });
    previewCache.clear(); await loadState();
    message(`已通过 ${result.sourceName} 更新 ${result.themeCount} 个主题`);
  } catch (error) { message(`同步失败：${String(error)}`); }
  finally { setBusy(false); }
};

document.querySelector<HTMLButtonElement>("#apply")!.onclick = async () => {
  if (!selectedTheme) return;
  const themeId = selectedTheme.id;
  setBusy(true, "正在检查 Codex 主题模式…");
  try {
    const ready = await call<boolean>("theme_runtime_ready");
    if (!ready && !window.confirm("当前 Codex 没有启用本机主题端口，需要重启后应用主题。是否继续？")) {
      message("已取消应用主题");
      return;
    }
    message(ready ? "正在应用主题…" : "正在重启 Codex 并应用主题…");
    message(await call<string>(ready ? "apply_theme" : "restart_and_apply", { themeId }));
  } catch (error) { message(String(error)); }
  finally { setBusy(false); }
};

document.querySelector<HTMLButtonElement>("#rollback")!.onclick = async () => {
  setBusy(true, "正在恢复默认主题…");
  try { message(await call<string>("rollback_theme")); }
  catch (error) { message(String(error)); }
  finally { setBusy(false); }
};

if (!qaMode) void listen<string[]>("external-activation", event => void handleActivation(event.payload));
void loadState().then(async () => handleActivation(await call<string[]>("pending_activations")));
