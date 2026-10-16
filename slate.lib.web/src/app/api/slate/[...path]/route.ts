import { resolveSlateUpstream, upstreamHeaders, type SlateUpstream } from "@/lib/server/slate-upstream";
export const dynamic = "force-dynamic";
const allowedPaths = [
  /^v1\/status$/,
  /^v1\/library$/,
  /^v1\/library\/(?:item|rename|move|copy|delete|refresh)$/,
  /^v1\/library\/bulk\/(?:preview|apply|[a-f0-9-]{36})$/,
  /^v1\/notes$/,
  /^v1\/notes\/by-path$/,
  /^v1\/notes\/[a-f0-9-]{36}$/,
  /^v1\/notes\/[a-f0-9-]{36}\/(?:links|related|graph|restore|answer|append-preview|append-capture|wiki-export)$/,
  /^v1\/notes\/[a-f0-9-]{36}\/history(?:\/[a-f0-9]+)?$/,
  /^v1\/notes\/[a-f0-9-]{36}\/link-repair\/(?:preview|apply)$/,
  /^v1\/(?:folders|captures)$/,
  /^v1\/search(?:\/rebuild)?$/,
  /^v1\/views\/[a-z-]+$/,
  /^v1\/links\/issues$/,
  /^v1\/history\/deleted$/,
  /^v1\/rediscovery\/[a-z-]+$/,
  /^v1\/workflows\/daily$/,
  /^v1\/offline\/replay$/,
  /^v1\/sync$/,
  /^v1\/sync\/(?:merge-preview|merge\/[a-f0-9-]{36})$/,
  /^v1\/git\/flush$/,
  /^v1\/assets$/,
  /^v1\/assets\/[a-f0-9-]{36}(?:\/(?:metadata|references|thumbnail|text|extract|cleanup))?$/,
] as const;
export function isAllowedPath(path: string) {
  return allowedPaths.some((pattern) => pattern.test(path));
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
  let connection: SlateUpstream;
  try {
    connection = resolveSlateUpstream(request);
  } catch (caught) {
    return fail(caught instanceof Response ? caught.status : 502);
  }
  const path = (await params).path.join("/");
  if (!isAllowedPath(path)) return fail(404);
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
  const headers = upstreamHeaders(connection);
  for (const key of ["content-type", "if-match", "x-slate-sha256", "accept"]) {
    const v = request.headers.get(key);
    if (v) headers.set(key, v);
  }
  try {
    const response = await fetch(
      `${connection.upstream}/${path}${new URL(request.url).search}`,
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
