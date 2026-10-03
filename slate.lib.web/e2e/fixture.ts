import type { Page, Route } from "@playwright/test";
import type { Note, Asset } from "../src/lib/api/contracts";
export const first = "00000000-0000-4000-8000-000000000001",
  second = "00000000-0000-4000-8000-000000000002";
export const libraryId = "10000000-0000-4000-8000-000000000001";
export const scope = "https://fixture.slate.test:" + libraryId;
export async function mockSlate(page: Page, options: { generationEnabled?: boolean } = {}) {
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
  const bulkPlans = new Map<
    string,
    {
      operation: string;
      items: { sourcePath: string; destinationPath: string | null; isDirectory: boolean }[];
      fingerprint: string;
    }
  >();
  const requests: { path: string; method: string; body: unknown }[] = [];
  const askRequests: { question: string; policy: "strict" | "general"; scope: { kind: string; noteId?: string; noteIds?: string[]; path?: string; selectedText?: string } }[] = [];
  const projectRequests: { action: string; path: string; kind?: string; refresh?: boolean }[] = [];
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
  await page.route("**/api/intelligence/**", async (route: Route) => {
    const req = route.request();
    const endpoint = new URL(req.url()).pathname.split("/").at(-1);
    const respond = (data: unknown, status = 200) => route.fulfill({ status, contentType: "application/json", body: JSON.stringify(data) });
    if (endpoint === "status") return respond({ enabled: true, state: "ready", provider: "fixture", model: "fixture-v1", dimensions: 32, noteCount: notes.size, totalNotes: notes.size, chunkCount: notes.size * 3, pendingJobs: 0, failedJobs: 0, embeddedThisRun: 0, reusedThisRun: notes.size * 3, failedChunksThisRun: 0, lastIndexedAt: "2026-10-03T10:00:00Z", lastError: null, askEnabled: true, generationProvider: "deterministic-test", generationModel: "grounded-v1", askDefaultPolicy: "strict", askRequestsThisRun: 0, askRetrievedChunksThisRun: 0, askActiveRequests: 0, askMaxConcurrent: 2 });
    if (endpoint === "rebuild") return respond({ state: "pending" }, 202);
    if (endpoint === "events") return route.fulfill({ status: 202 });
    if (endpoint === "project-brain") {
      const input = req.postDataJSON() as { action: string; path: string; kind?: string; refresh?: boolean };
      projectRequests.push(input);
      const source = { citationId: "S1", noteId: first, title: "Library architecture", path: "Projects/Library architecture.md", heading: "Principles", ordinal: 0, revision: '"r1"', excerpt: "Markdown is the source of truth. Stable identities connect notes.", score: 1 };
      if (input.action === "generate") {
        if (options.generationEnabled === false) return respond({ error: "AI synthesis isn't configured." }, 503);
        const resume = input.kind === "resume";
        return respond({ kind: input.kind, markdown: resume ? "## Project\nSlate is a private Markdown workspace. [S1]\n\n## Where Things Stand\nThe storage architecture is documented. [S1]\n\n## Recent Work\nThe architecture note was updated. [S1]\n\n## Key Decisions\nMarkdown remains the source of truth. [S1]\n\n## Open Questions\nSync retry remains open. [S1]\n\n## Known Problems\nRenderer permissions need review. [S1]\n\n## What to Read First\nLibrary architecture. [S1]\n\n## Possible Next Context\nReview sync behavior. [S1]" : "Slate is a private Markdown knowledge workspace built around stable note identities. [S1]", sources: [source], citations: ["S1"], generatedAt: "2026-10-03T10:00:00Z", cached: false, cacheKey: "fixture" });
      }
      const evidence = (sourceClass: string, title = "Library architecture") => ({ noteId: first, title, path: "Projects/Library architecture.md", revision: '"r1"', heading: "Principles", excerpt: "Markdown is the source of truth. Stable identities connect notes.", sourceClass, type: sourceClass, status: sourceClass === "question" ? "open" : "active", timestamp: "2026-10-03T10:00:00Z" });
      return respond({ schemaVersion: 1, path: input.path, name: input.path.split("/").at(-1), noteCount: 1, folderCount: 1, bounded: false, status: "Active", lastMeaningfulChange: "2026-10-03T10:00:00Z", openQuestionCount: 1, sections: { overview: [evidence("overview")], current: [evidence("recent")], decisions: [evidence("decision", "Use Markdown storage")], questions: [evidence("question", "How should sync retry?")], architecture: [evidence("architecture")], ideas: [evidence("idea", "Offline cache")], experiments: [evidence("experiment", "Index experiment")], failures: [evidence("failure", "WebView failure")], risks: [evidence("risk", "Renderer permissions")], important: [evidence("important")] }, timeline: [{ id: "event-1", noteId: first, title: "Library architecture", path: "Projects/Library architecture.md", timestamp: "2026-10-03T10:00:00Z", label: "Document library structure", commit: "a".repeat(40) }], graph: { nodes: [{ id: first, title: "Library architecture", path: "Projects/Library architecture.md", kind: "architecture" }], edges: [], limited: false }, relatedOutside: [{ id: second, title: "Reading list", path: "Research/Reading list.md", score: .8, reasons: ["Related storage research"] }], sources: [source], sourceFingerprint: "fixture-fingerprint", generatedAt: "2026-10-03T10:00:00Z", provider: { available: options.generationEnabled !== false, name: options.generationEnabled === false ? "disabled" : "deterministic-test", model: options.generationEnabled === false ? "disabled" : "grounded-v1" } });
    }
    if (endpoint === "ask") {
      const input = req.postDataJSON() as { question: string; policy: "strict" | "general"; scope: { kind: string; noteId?: string; noteIds?: string[]; path?: string } };
      askRequests.push(input);
      if (/slow/i.test(input.question)) await new Promise((resolve) => setTimeout(resolve, 1_500));
      let scoped = Array.from(notes.values());
      if (input.scope.kind === "note") scoped = scoped.filter((note) => note.id === input.scope.noteId);
      if (input.scope.kind === "selected") scoped = scoped.filter((note) => input.scope.noteIds?.includes(note.id));
      if (input.scope.kind === "folder" || input.scope.kind === "project") scoped = scoped.filter((note) => note.path === input.scope.path || note.path.startsWith(`${input.scope.path}/`));
      if (/quantum chromodynamics|missing evidence/i.test(input.question)) scoped = [];
      const sources = scoped.slice(0, 2).map((note, index) => ({ citationId: `S${index + 1}`, noteId: note.id, title: note.title, path: note.path, heading: index ? null : "Principles", ordinal: index, revision: note.revision, excerpt: note.markdown.replace(/^---[\s\S]*?---/, "").trim().slice(0, 220), score: 1 - index * .1 }));
      const answer = !sources.length
        ? "I couldn't find enough in your Slate library to answer that."
        : input.policy === "general"
          ? `## From your library\n\nSlate keeps Markdown as the source of truth. [S1]\n\n## General context\n\nGeneral context is clearly separated from library evidence.`
          : `Slate keeps Markdown as the source of truth and uses stable identities for links. [S1]${sources[1] ? " Related reading covers distributed systems. [S2]" : ""}`;
      const events = [
        { type: "retrieval", message: "Searching your Slate sources…" },
        { type: "sources", sources },
        ...answer.match(/.{1,34}(?:\s|$)/g)!.map((text) => ({ type: "delta", text })),
        { type: "done", citations: sources.map((source) => source.citationId), usage: { inputTokens: 120, outputTokens: 36 } },
      ];
      return route.fulfill({ status: 200, contentType: "text/event-stream; charset=utf-8", body: events.map((event) => `data: ${JSON.stringify(event)}\n\n`).join("") }).catch(() => undefined);
    }
    if (endpoint === "search") {
      const input = req.postDataJSON() as { query: string; mode: "hybrid" | "lexical" | "semantic" };
      const term = input.query.toLowerCase();
      const lexical = Array.from(notes.values()).filter((n) => (n.title + " " + n.markdown).toLowerCase().includes(term));
      const semantic = /durable|knowledge|reliable|storage|architecture/.test(term) ? [notes.get(first)!] : /reading|distributed/.test(term) ? [notes.get(second)!] : [];
      const selected = input.mode === "lexical" ? lexical : input.mode === "semantic" ? semantic : [...new Map([...lexical, ...semantic].map((n) => [n.id, n])).values()];
      return respond({ query: input.query, mode: input.mode, effectiveMode: input.mode, page: 0, pageSize: 20, total: selected.length, results: selected.map((n) => ({ id: n.id, title: n.title, path: n.path, revision: n.revision, snippet: n.markdown.slice(0, 180), heading: "Principles", match: lexical.some((hit) => hit.id === n.id) ? "both" : "meaning", score: 1 })) });
    }
    if (endpoint === "related") {
      const note = notes.get(first)!;
      return respond([{ id: second, title: "Reading list", path: "Research/Reading list.md", score: .8, reasons: ["Links to this note", `Similar meaning to ${note.title}`] }]);
    }
    return respond({}, 404);
  });
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
    if (path === "v1/library/bulk/preview") {
      const operationId = String(body.operationId);
      const operation = String(body.operation);
      const destination = String(body.destinationFolderPath ?? "");
      const selected = body.paths as string[];
      const items = selected.map((sourcePath) => {
        const isDirectory = folders.has(sourcePath);
        const filename = sourcePath.split("/").at(-1)!;
        const parent = sourcePath.includes("/")
          ? sourcePath.slice(0, sourcePath.lastIndexOf("/"))
          : "";
        const destinationPath =
          operation === "delete"
            ? null
            : operation === "duplicate"
              ? [parent, `${filename} copy`].filter(Boolean).join("/")
              : [destination, filename].filter(Boolean).join("/");
        return { sourcePath, destinationPath, isDirectory };
      });
      const plan = { operation, items, fingerprint: `fixture-${operationId}` };
      bulkPlans.set(operationId, plan);
      return respond({
        operationId,
        operation,
        items,
        noteCount: selected.reduce(
          (count, selectedPath) =>
            count +
            Array.from(notes.values()).filter(
              (note) =>
                note.path === selectedPath || note.path.startsWith(`${selectedPath}/`),
            ).length,
          0,
        ),
        fingerprint: plan.fingerprint,
        repairs: [],
      });
    }
    if (path === "v1/library/bulk/apply") {
      const operationId = String(body.operationId);
      const plan = bulkPlans.get(operationId);
      if (!plan || plan.fingerprint !== body.fingerprint) return respond({}, 412);
      for (const item of plan.items) {
        const affected = Array.from(notes.values()).filter(
          (note) =>
            note.path === item.sourcePath || note.path.startsWith(`${item.sourcePath}/`),
        );
        if (plan.operation === "delete" || plan.operation === "move") {
          for (const note of affected) notes.delete(note.id);
          if (item.isDirectory) folders.delete(item.sourcePath);
        }
        if (item.destinationPath && plan.operation !== "delete") {
          if (item.isDirectory) folders.add(item.destinationPath);
          for (const note of affected) {
            const copied = {
              ...note,
              id: plan.operation === "move" ? note.id : crypto.randomUUID(),
              path: item.destinationPath + note.path.slice(item.sourcePath.length),
            };
            notes.set(copied.id, copied);
          }
        }
      }
      return respond({
        operationId,
        state: "completed",
        items: plan.items,
        noteCount: plan.items.length,
      });
    }
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
    if (path === "v1/views/unanswered") {
      const note = notes.get(first)!;
      return respond({
        query: "is:unanswered",
        page: 0,
        pageSize: 20,
        total: 1,
        results: [
          {
            id: note.id,
            title: note.title,
            path: note.path,
            revision: note.revision,
            snippet: "An open question from the library.",
            score: 1,
          },
        ],
        searchVersion: 1,
      });
    }
    if (path.startsWith("v1/rediscovery/")) {
      const note = notes.get(second)!;
      return respond({
        view: path.split("/").at(-1),
        page: 0,
        total: 1,
        results: [
          {
            id: note.id,
            title: note.title,
            path: note.path,
            reason: "You have not opened this note recently.",
            date: "2026-09-28",
          },
        ],
      });
    }
    if (path === "v1/workflows/daily") {
      const date = String(body.date);
      const id = "00000000-0000-4000-8000-000000000003";
      const existing = notes.get(id);
      if (existing) return respond(existing);
      folders.add("Daily");
      const note = {
        id,
        path: `Daily/${date}.md`,
        title: date,
        markdown: `---\nid: ${id}\ntype: daily\n---\n\n# ${date}\n`,
        revision: '"r1"',
      };
      notes.set(id, note);
      return respond(note, 201);
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
      const id = String(body.id ?? crypto.randomUUID());
      let name = String(body.name);
      if (!name.endsWith(".md")) name += ".md";
      const target = [body.folderPath, name].filter(Boolean).join("/");
      if (Array.from(notes.values()).some((n) => n.path === target))
        return respond({}, 409);
      const note = {
        id,
        path: target,
        title: String(body.title ?? name.replace(/\.md$/, "")),
        markdown: String(
          body.initialMarkdown ??
            `---\nid: ${id}\n---\n\n# ${name.replace(/\.md$/, "")}\n`,
        ),
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
      if (match[2] === "/related") {
        const target = notes.get(n.id === first ? second : first)!;
        return respond([
          {
            id: target.id,
            title: target.title,
            path: target.path,
            score: 0.82,
            reasons: ["Links to this note", "Shares the library topic"],
          },
        ]);
      }
      if (match[2] === "/graph") {
        const target = notes.get(n.id === first ? second : first)!;
        return respond({
          focus: n.id,
          nodes: [
            {
              id: n.id,
              title: n.title,
              path: n.path,
              type: "reference",
              status: null,
              depth: 0,
            },
            {
              id: target.id,
              title: target.title,
              path: target.path,
              type: "reading",
              status: null,
              depth: 1,
            },
          ],
          edges: [{ source: n.id, target: target.id }],
          limited: false,
        });
      }
      if (match[2] === "/wiki-export" && method === "GET") {
        const proposedMarkdown = n.markdown.replace(
          "[[Reading list]]",
          "[Reading list](../Research/Reading%20list.md)",
        );
        return respond({
          id: n.id,
          path: n.path,
          revision: n.revision,
          originalMarkdown: n.markdown,
          proposedMarkdown,
          changes:
            proposedMarkdown === n.markdown
              ? []
              : [
                  {
                    start: n.markdown.indexOf("[[Reading list]]"),
                    length: "[[Reading list]]".length,
                    original: "[[Reading list]]",
                    proposed: "[Reading list](../Research/Reading%20list.md)",
                    targetId: second,
                    targetPath: "Research/Reading list.md",
                  },
                ],
        });
      }
      if (match[2] === "/wiki-export" && method === "POST") {
        if (body.sourceRevision !== n.revision) return respond({}, 412);
        const updated = {
          ...n,
          markdown: String(body.proposedMarkdown),
          revision: '"wiki-export"',
        };
        notes.set(n.id, updated);
        return respond(updated);
      }
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
    askRequests,
    projectRequests,
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
