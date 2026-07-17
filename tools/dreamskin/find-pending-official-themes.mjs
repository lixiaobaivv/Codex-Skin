import { readFile } from "node:fs/promises";
import { join, resolve } from "node:path";

const sourceDir = resolve(process.argv[2] ?? "");
const requestedId = (process.argv[3] ?? "").trim();
if (requestedId && !/^[a-z0-9]+(?:-[a-z0-9]+)+$/.test(requestedId)) {
  throw new Error("theme_id must contain only lowercase letters, numbers, and hyphens.");
}

const repository = JSON.parse(await readFile(join(sourceDir, "theme-repository.json"), "utf8"));
const pending = [];
for (const entry of repository.themes ?? []) {
  if (requestedId && entry.id !== requestedId) continue;
  const catalog = JSON.parse(await readFile(join(sourceDir, "catalog", "themes", `${entry.id}.json`), "utf8"));
  if (catalog.slug !== entry.id) throw new Error(`${entry.id}: catalog slug does not match the repository index.`);
  if (catalog.package === null) pending.push(entry.id);
}

if (requestedId && !(repository.themes ?? []).some(entry => entry.id === requestedId)) {
  throw new Error(`${requestedId} is not listed in theme-repository.json.`);
}
process.stdout.write(JSON.stringify(pending));
