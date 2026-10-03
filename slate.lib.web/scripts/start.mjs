import { cp, access } from "node:fs/promises";
import path from "node:path";
import { pathToFileURL } from "node:url";
const root = path.resolve(import.meta.dirname, "..");
const standalone = path.join(root, ".next", "standalone");
await access(path.join(standalone, "server.js"));
await cp(path.join(root, "public"), path.join(standalone, "public"), {
  recursive: true,
});
await cp(
  path.join(root, ".next", "static"),
  path.join(standalone, ".next", "static"),
  { recursive: true },
);
process.env.HOSTNAME = process.env.SLATE_WEB_HOST ?? "127.0.0.1";
process.env.PORT ??= "3000";
await import(pathToFileURL(path.join(standalone, "server.js")).href);
