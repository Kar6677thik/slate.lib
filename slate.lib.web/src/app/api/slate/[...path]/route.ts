import { normalizeServer } from "@/lib/api/client";
export const dynamic = "force-dynamic";
const allowedPath =
  /^v1\/(status|library(?:\/(?:item|rename|move|copy|delete|refresh))?|notes(?:\/[a-f0-9-]+(?:\/(?:links|history(?:\/[a-f0-9]+)?))?)?|folders|captures|search|sync|assets(?:\/[a-f0-9-]+(?:\/metadata)?)?)$/i;

function trustedUpstream() {
  const value = process.env.SLATE_UPSTREAM_URL?.trim();
  if (!value) return null;
  const url = new URL(value);
  if (
    !["http:", "https:"].includes(url.protocol) ||
    url.username ||
    url.password ||
    url.search ||
    url.hash
  )
    throw new Error("SLATE_UPSTREAM_URL must be a plain HTTP(S) base URL.");
  return url.href.replace(/\/+$/, "");
}

function trustedPublicOrigin(request: Request) {
  const value = process.env.SLATE_PUBLIC_ORIGIN?.trim();
  if (!value) return new URL(request.url).origin;
  const url = new URL(value);
  if (
    !["http:", "https:"].includes(url.protocol) ||
    url.username ||
    url.password ||
    url.pathname !== "/" ||
    url.search ||
    url.hash
  )
    throw new Error("SLATE_PUBLIC_ORIGIN must be an HTTP(S) origin.");
  return url.origin;
}
async function proxy(
  request: Request,
  { params }: { params: Promise<{ path: string[] }> },
) {
  const fail = (status: number) =>
    Response.json(
      { error: "Slate request could not be forwarded." },
      { status, headers: { "Cache-Control": "no-store" } },
    );
  let server: string;
  try {
    server = normalizeServer(request.headers.get("X-Slate-Server") ?? "");
  } catch {
    return fail(400);
  }
  const approved = (
    process.env.SLATE_ALLOWED_SERVERS ??
    process.env.NEXT_PUBLIC_SLATE_DEFAULT_SERVER ??
    "http://localhost:5080"
  )
    .split(",")
    .map((s) => {
      try {
        return normalizeServer(s);
      } catch {
        return "";
      }
    });
  if (!approved.includes(server)) return fail(403);
  const origin = request.headers.get("origin");
  try {
    if (origin && origin !== trustedPublicOrigin(request)) return fail(403);
  } catch {
    return fail(502);
  }
  const path = (await params).path.join("/");
  if (!allowedPath.test(path)) return fail(404);
  const auth = request.headers.get("authorization");
  if (!auth?.startsWith("Bearer ")) return fail(401);
  const length = Number(request.headers.get("content-length") ?? 0);
  if (length > 26 * 1024 * 1024) return fail(413);
  let tooLarge = false;
  let received = 0;
  const body = request.body?.pipeThrough(
    new TransformStream<Uint8Array, Uint8Array>({
      transform(chunk, controller) {
        received += chunk.byteLength;
        if (received > 26 * 1024 * 1024) {
          tooLarge = true;
          controller.error(new Error("Request body limit exceeded"));
          return;
        }
        controller.enqueue(chunk);
      },
    }),
  );
  const headers = new Headers({ Authorization: auth });
  for (const key of ["content-type", "if-match", "x-slate-sha256", "accept"]) {
    const v = request.headers.get(key);
    if (v) headers.set(key, v);
  }
  try {
    const upstream = trustedUpstream() ?? server;
    if (upstream !== server) headers.set("X-Forwarded-Proto", "https");
    const response = await fetch(
      `${upstream}/${path}${new URL(request.url).search}`,
      {
        method: request.method,
        headers,
        body: request.method === "GET" ? undefined : body,
        duplex: "half",
        redirect: "manual",
        cache: "no-store",
        signal: AbortSignal.any([request.signal, AbortSignal.timeout(29000)]),
      } as RequestInit,
    );
    if (response.status >= 300 && response.status < 400) return fail(502);
    const out = new Headers({
      "Cache-Control": "no-store",
      "X-Content-Type-Options": "nosniff",
    });
    for (const key of ["content-type", "etag", "content-disposition"]) {
      const v = response.headers.get(key);
      if (v) out.set(key, v);
    }
    return new Response(response.body, {
      status: response.status,
      headers: out,
    });
  } catch {
    return fail(tooLarge ? 413 : 502);
  }
}
export { proxy as GET, proxy as POST, proxy as PUT };
