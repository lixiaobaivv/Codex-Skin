import { invoke } from "@tauri-apps/api/core";
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
      <button id="apply" class="button">应用主题</button>
      <button id="restart" class="button primary">应用并重启 Codex</button>
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
  document.querySelector<HTMLButtonElement>("#restart")!.disabled = busy || !selectedTheme;
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
    if (!data) { data = await invoke<string>("read_preview", { path }); previewCache.set(path, data); }
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
    state = await invoke<AppState>("get_app_state");
    selectedTheme = state.themes.find(theme => theme.id === selectedTheme?.id) ?? state.themes[0];
    renderSources(); renderCategories(); renderThemes();
    if (selectedTheme) document.querySelector("#selection")!.textContent = `已选择：${selectedTheme.name}`;
    message(state.themes.length ? "主题目录已加载" : "首次使用需要联网同步主题");
  } catch (error) { message(String(error)); }
  updateCommands();
}

document.querySelector<HTMLButtonElement>("#refresh")!.onclick = async () => {
  setBusy(true, "正在同步主题目录…");
  try {
    const result = await invoke<{ themeCount: number; sourceId: string; sourceName: string }>("sync_catalog", { sourceId: sourceSelect.value });
    previewCache.clear(); await loadState();
    message(`已通过 ${result.sourceName} 更新 ${result.themeCount} 个主题`);
  } catch (error) { message(`同步失败：${String(error)}`); }
  finally { setBusy(false); }
};

for (const [id, command, label] of [
  ["apply", "apply_theme", "正在应用主题…"],
  ["restart", "restart_and_apply", "正在重启 Codex 并应用主题…"],
  ["rollback", "rollback_theme", "正在恢复默认主题…"],
] as const) {
  document.querySelector<HTMLButtonElement>(`#${id}`)!.onclick = async () => {
    setBusy(true, label);
    try { message(await invoke<string>(command, { themeId: selectedTheme?.id ?? null })); }
    catch (error) { message(String(error)); }
    finally { setBusy(false); }
  };
}

void loadState();
