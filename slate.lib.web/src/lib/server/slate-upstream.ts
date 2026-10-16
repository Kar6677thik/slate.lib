import { normalizeServer } from "@/lib/api/client";

export interface SlateUpstream {
  server: string;
  upstream: string;
  authorization: string;
}

/** Operator-controlled base URL for reaching Slate from inside the cluster, or null to use the browser's server. */
export function trustedUpstream() {
  const value = process.env.SLATE_UPSTREAM_URL?.trim();
  if (!value) return null;
  const url = new URL(value);
  if (!["http:", "https:"].includes(url.protocol) || url.username || url.password || url.search || url.hash) throw new Error("Invalid SLATE_UPSTREAM_URL");
  return url.href.replace(/\/+$/, "");
}

export function trustedPublicOrigin(request: Request) {
  const value = process.env.SLATE_PUBLIC_ORIGIN?.trim();
  if (!value) return new URL(request.url).origin;
  const url = new URL(value);
  if (!["http:", "https:"].includes(url.protocol) || url.username || url.password || url.pathname !== "/" || url.search || url.hash) throw new Error("Invalid SLATE_PUBLIC_ORIGIN");
  return url.origin;
}

function approvedServers() {
  return (process.env.SLATE_ALLOWED_SERVERS ?? process.env.NEXT_PUBLIC_SLATE_DEFAULT_SERVER ?? "http://localhost:5080")
    .split(",")
    .map((value) => { try { return normalizeServer(value); } catch { return ""; } });
}

/**
 * Validates the browser's Slate connection. Client mistakes are thrown as a Response carrying the status code;
 * operator misconfiguration is thrown as an Error. The bearer token itself is only proven by the upstream.
 */
export function resolveSlateUpstream(request: Request): SlateUpstream {
  const origin = request.headers.get("origin");
  if (origin && origin !== trustedPublicOrigin(request)) throw new Response("Forbidden", { status: 403 });
  let server: string;
  try { server = normalizeServer(request.headers.get("X-Slate-Server") ?? ""); }
  catch { throw new Response("Bad Request", { status: 400 }); }
  if (!approvedServers().includes(server)) throw new Response("Forbidden", { status: 403 });
  const authorization = request.headers.get("authorization") ?? "";
  if (!authorization.startsWith("Bearer ")) throw new Response("Unauthorized", { status: 401 });
  return { server, upstream: trustedUpstream() ?? server, authorization };
}

/** Headers every upstream call needs. An internal upstream only accepts plain HTTP when told the public hop was HTTPS. */
export function upstreamHeaders(connection: SlateUpstream) {
  const headers = new Headers({ Authorization: connection.authorization });
  if (connection.upstream !== connection.server) headers.set("X-Forwarded-Proto", "https");
  return headers;
}

export async function fetchSlate(connection: SlateUpstream, path: string, signal?: AbortSignal) {
  return fetch(`${connection.upstream}/${path}`, {
    headers: upstreamHeaders(connection),
    cache: "no-store",
    redirect: "manual",
    signal: AbortSignal.any([signal ?? new AbortController().signal, AbortSignal.timeout(28000)]),
  });
}
