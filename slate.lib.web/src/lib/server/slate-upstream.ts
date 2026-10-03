import { normalizeServer } from "@/lib/api/client";

export interface SlateUpstream {
  server: string;
  upstream: string;
  authorization: string;
}

export function resolveSlateUpstream(request: Request): SlateUpstream {
  const requestOrigin = new URL(request.url).origin;
  const configuredOrigin = process.env.SLATE_PUBLIC_ORIGIN?.trim();
  let publicOrigin = requestOrigin;
  if (configuredOrigin) {
    const parsed = new URL(configuredOrigin);
    if (!["http:", "https:"].includes(parsed.protocol) || parsed.username || parsed.password || parsed.pathname !== "/" || parsed.search || parsed.hash) throw new Error("Invalid SLATE_PUBLIC_ORIGIN");
    publicOrigin = parsed.origin;
  }
  const origin = request.headers.get("origin");
  if (origin && origin !== publicOrigin) throw new Response("Forbidden", { status: 403 });
  const server = normalizeServer(request.headers.get("X-Slate-Server") ?? "");
  const approved = (process.env.SLATE_ALLOWED_SERVERS ?? process.env.NEXT_PUBLIC_SLATE_DEFAULT_SERVER ?? "http://localhost:5080")
    .split(",")
    .map((value) => { try { return normalizeServer(value); } catch { return ""; } });
  if (!approved.includes(server)) throw new Response("Forbidden", { status: 403 });
  const authorization = request.headers.get("authorization") ?? "";
  if (!authorization.startsWith("Bearer ")) throw new Response("Unauthorized", { status: 401 });
  const configured = process.env.SLATE_UPSTREAM_URL?.trim();
  let upstream = server;
  if (configured) {
    const url = new URL(configured);
    if (!["http:", "https:"].includes(url.protocol) || url.username || url.password || url.search || url.hash) throw new Error("Invalid SLATE_UPSTREAM_URL");
    upstream = url.href.replace(/\/+$/, "");
  }
  return { server, upstream, authorization };
}

export async function fetchSlate(connection: SlateUpstream, path: string, signal?: AbortSignal) {
  return fetch(`${connection.upstream}/${path}`, {
    headers: { Authorization: connection.authorization },
    cache: "no-store",
    redirect: "manual",
    signal: AbortSignal.any([signal ?? new AbortController().signal, AbortSignal.timeout(28000)]),
  });
}
