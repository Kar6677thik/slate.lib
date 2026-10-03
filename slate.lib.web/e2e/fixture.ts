import type { Page, Route } from "@playwright/test";
import type { Note, Asset } from "../src/lib/api/contracts";
export const first = "00000000-0000-4000-8000-000000000001",
  second = "00000000-0000-4000-8000-000000000002";
export const libraryId = "10000000-0000-4000-8000-000000000001";
export const scope = "https://fixture.slate.test:" + libraryId;
export async function mockSlate(page: Page) {
  const notes = new Map<string, Note>([
    [
      first,
      {
        id: first,
        path: "Projects/Library architecture.md",
        title: "Library architecture",
        revision: '"r1"',
        markdown: `---\nid: ${first}\n---\n\n# Library architecture\n\nA working reference for how we store, connect, and maintain the library.\n\n## Principles\n\nKeep the original files readable. Use stable identities for links. Make every change easy to review.\n\n- Markdown is the source of truth\n- Folders describe where a note belongs\n- Links describe how ideas connect\n\n> [!NOTE]\n> Start with a small structure. Add folders when the work calls for them.\n\n## Storage decisions\n\n| Content | Location |\n| --- | --- |\n| Notes | Markdown files |\n| Attachments | Asset storage |\n| History | Git repository |\n\n## Next steps\n\n- [x] Document the storage model\n- [ ] Review [[Reading list]]\n\n\`\`\`typescript\nconst library = { source: "markdown", private: true };\n\`\`\`\n`,
      },
    ],
    [
      second,
      {
        id: second,
        path: "Research/Reading list.md",
        title: "Reading list",
        revision: '"r1"',
        markdown: `---\nid: ${second}\n---\n\n# Reading list\n\nNotes on distributed systems and reliable storage.\n\nSee [[Library architecture]].\n`,
      },
    ],
  ]);
  const folders = new Set(["Projects", "Research", "inbox"]);
  const assets = new Map<string, { meta: Asset; bytes: Buffer }>();
  const requests: { path: string; method: string; body: unknown }[] = [];
  let conflict = false;
  let assetCounter = 0;
  function getEntry(n: Note) {
    return {
      name: n.path.split("/").at(-1),
      path: n.path,
      isDirectory: false,
      id: n.id,
      title: n.title,
    };
  }
  await page.route("**/api/slate/**", async (route: Route) => {
    const req = route.request();
    const url = new URL(req.url());
    const path = url.pathname.replace("/api/slate/", "");
    const method = req.method();
    let body: Record<string, unknown> = {};
    if (method !== "GET" && req.headers()["content-type"]?.includes("json"))
      body = req.postDataJSON();
    requests.push({ path, method, body });
    const respond = (data: unknown, status = 200) =>
      route.fulfill({
        status,
        contentType: "application/json",
        body: JSON.stringify(data),
      });
    if (!req.headers().authorization?.startsWith("Bearer "))
      return respond({}, 401);
    if (path === "v1/status")
      return respond({
        libraryId,
        noteCount: notes.size,
        serverVersion: "0.4.1-test",
        indexState: "Ready",
        git: { state: "Synced", pending: false },
      });
    if (path === "v1/sync") return respond({ state: "Synced", pending: false });
    if (path === "v1/library/refresh") return route.fulfill({ status: 204 });
    if (path === "v1/library") {
      const folder = url.searchParams.get("path") ?? "";
      if (folder && !folders.has(folder)) return respond({}, 404);
      return respond({
        path: folder,
        entries: [
          ...Array.from(folders)
            .filter(
              (f) =>
                (f.includes("/") ? f.slice(0, f.lastIndexOf("/")) : "") ===
                folder,
            )
            .map((f) => ({
              name: f.split("/").at(-1),
              path: f,
              isDirectory: true,
              id: null,
              title: null,
            })),
          ...Array.from(notes.values())
            .filter(
              (n) =>
                (n.path.includes("/")
                  ? n.path.slice(0, n.path.lastIndexOf("/"))
                  : "") === folder,
            )
            .map(getEntry),
        ],
        nextPage: null,
      });
    }
    if (path === "v1/search") {
      const term = (url.searchParams.get("q") ?? "").toLowerCase();
      const results = Array.from(notes.values())
        .filter((n) =>
          (n.title + " " + n.markdown).toLowerCase().includes(term),
        )
        .map((n) => ({
          ...n,
          snippet: n.markdown.replace(/^---[\s\S]*?---/, "").slice(0, 130),
          score: 1,
        }));
      return respond({
        query: term,
        page: 0,
        pageSize: 20,
        total: results.length,
        results,
      });
    }
    if (path === "v1/captures") {
      const id = String(body.captureId);
      if (notes.has(id)) return respond(notes.get(id));
      const content = String(body.content);
      const note = {
        id,
        path: `inbox/Thought-${id.slice(0, 8)}.md`,
        title: content.match(/^# (.+)/)?.[1] ?? "Quick Thought",
        markdown: `---\nid: ${id}\n---\n\n${content}`,
        revision: '"r1"',
      };
      notes.set(id, note);
      return respond(note, 201);
    }
    if (path === "v1/notes" && method === "POST") {
      const id = crypto.randomUUID();
      let name = String(body.name);
      if (!name.endsWith(".md")) name += ".md";
      const target = [body.folderPath, name].filter(Boolean).join("/");
      if (Array.from(notes.values()).some((n) => n.path === target))
        return respond({}, 409);
      const note = {
        id,
        path: target,
        title: name.replace(/\.md$/, ""),
        markdown: `---\nid: ${id}\n---\n\n# ${name.replace(/\.md$/, "")}\n`,
        revision: '"r1"',
      };
      notes.set(id, note);
      return respond(note, 201);
    }
    if (path === "v1/folders") {
      const target = [body.parentPath, body.name].filter(Boolean).join("/");
      if (folders.has(target)) return respond({}, 409);
      folders.add(target);
      return respond({ path: target, isDirectory: true });
    }
    if (path === "v1/library/item") {
      const target = url.searchParams.get("path")!;
      return respond({
        path: target,
        isDirectory: folders.has(target),
        descendantCount: Array.from(notes.values()).filter((n) =>
          n.path.startsWith(target + "/"),
        ).length,
      });
    }
    if (path.startsWith("v1/library/")) {
      const action = path.split("/").at(-1);
      const source = String(body.sourcePath ?? body.path);
      const note = Array.from(notes.values()).find((n) => n.path === source);
      if (action === "delete") {
        if (note) notes.delete(note.id);
        else {
          folders.delete(source);
          for (const [id, n] of notes)
            if (n.path.startsWith(source + "/")) notes.delete(id);
        }
        return respond({ path: source, isDirectory: !note, affectedItems: 1 });
      }
      const dest =
        action === "rename"
          ? [
              source.includes("/")
                ? source.slice(0, source.lastIndexOf("/"))
                : "",
              String(body.newName),
            ]
              .filter(Boolean)
              .join("/")
          : [body.destinationFolderPath, source.split("/").at(-1)]
              .filter(Boolean)
              .join("/");
      if (!note) {
        folders.delete(source);
        folders.add(dest);
        for (const [id, n] of notes)
          if (n.path.startsWith(source + "/"))
            notes.set(id, { ...n, path: dest + n.path.slice(source.length) });
        return respond({ path: dest, isDirectory: true });
      }
      const target =
        action === "copy" &&
        Array.from(notes.values()).some((n) => n.path === dest)
          ? dest.replace(/\.md$/, " copy.md")
          : dest;
      if (
        action !== "copy" &&
        Array.from(notes.values()).some(
          (n) => n.path === target && n.id !== note.id,
        )
      )
        return respond({}, 409);
      const id = action === "copy" ? crypto.randomUUID() : note.id;
      notes.set(id, {
        ...note,
        id,
        path: target,
        title: target.split("/").at(-1)!.replace(/\.md$/, ""),
      });
      return respond({ path: target, id, isDirectory: false });
    }
    if (path === "v1/assets" && method === "POST") {
      const id = url.searchParams.get("id")!;
      const filename = url.searchParams.get("filename")!;
      const meta = {
        id,
        originalFilename: filename,
        contentType: req.headers()["content-type"],
        byteSize: req.postDataBuffer()?.length ?? 0,
        extension: "." + filename.split(".").at(-1),
        inlineImage: req.headers()["content-type"].startsWith("image/"),
      };
      assets.set(id, { meta, bytes: req.postDataBuffer() ?? Buffer.alloc(0) });
      assetCounter++;
      return respond(meta, 201);
    }
    if (path.startsWith("v1/assets/")) {
      const id = path.split("/")[2],
        asset = assets.get(id);
      if (!asset) return respond({}, 404);
      if (path.endsWith("/metadata")) return respond(asset.meta);
      return route.fulfill({
        status: 200,
        contentType: asset.meta.contentType,
        body: asset.bytes,
      });
    }
    const match = path.match(/^v1\/notes\/([^/]+)(.*)$/);
    if (match) {
      const n = notes.get(match[1]);
      if (!n) return respond({}, 404);
      if (match[2] === "/links") {
        const target = notes.get(n.id === first ? second : first);
        return respond({
          noteId: n.id,
          outgoing: target
            ? [
                {
                  raw: `[[${target.title}]]`,
                  target: target.title,
                  state: "resolved",
                  targetId: target.id,
                  targetTitle: target.title,
                  targetPath: target.path,
                  kind: "wiki",
                },
              ]
            : [],
          backlinks: target
            ? [
                {
                  sourceId: target.id,
                  sourceTitle: target.title,
                  sourcePath: target.path,
                },
              ]
            : [],
        });
      }
      if (match[2] === "/history")
        return respond([
          {
            commit: "a".repeat(40),
            timestamp: "2026-09-28T10:00:00Z",
            message: "Document library structure",
            author: "Slate",
          },
        ]);
      if (match[2].startsWith("/history/"))
        return respond({
          ...n,
          commit: "a".repeat(40),
          timestamp: "2026-09-28T10:00:00Z",
          message: "Document library structure",
          markdown: "# Earlier version\n\nOriginal design notes.",
        });
      if (method === "PUT") {
        if (conflict || req.headers()["if-match"] !== n.revision)
          return respond({}, 412);
        const updated = {
          ...n,
          markdown: String(body.markdown),
          revision: '"r' + Date.now() + '"',
        };
        notes.set(n.id, updated);
        return respond(updated);
      }
      return respond(n);
    }
    return respond({}, 404);
  });
  return {
    notes,
    folders,
    requests,
    setConflict: (value: boolean) => {
      conflict = value;
    },
    uploads: () => assetCounter,
  };
}
export async function connect(page: Page) {
  await page.goto("/");
  await page.getByLabel("Server URL").fill("https://fixture.slate.test");
  await page
    .getByLabel("Device token", { exact: true })
    .fill("fixture-token-not-a-secret");
  await page.getByLabel("Remember this device").check();
  await page
    .getByRole("button", { name: "Test connection & open library" })
    .click();
  await page.getByRole("heading", { name: "Library", exact: true }).waitFor();
}
export async function openNote(page: Page) {
  await page
    .getByRole("treeitem", { name: "Projects", exact: true })
    .first()
    .click();
  await page
    .getByRole("treeitem", { name: "Library architecture", exact: true })
    .first()
    .click();
  await page
    .getByRole("heading", { name: "Library architecture", exact: true })
    .first()
    .waitFor();
}
