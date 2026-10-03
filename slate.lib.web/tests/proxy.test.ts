// @vitest-environment node
import { afterEach, expect, it, vi } from "vitest";
import { GET, POST } from "@/app/api/slate/[...path]/route";
afterEach(() => {
  vi.unstubAllGlobals();
  vi.unstubAllEnvs();
});
function request(
  path = "v1/status",
  headers: Record<string, string> = {},
  method = "GET",
  body?: BodyInit,
) {
  vi.stubEnv("SLATE_ALLOWED_SERVERS", "https://approved.example");
  return [
    new Request("https://web.example/api/slate/" + path, {
      method,
      headers: {
        "X-Slate-Server": "https://approved.example",
        Authorization: "Bearer test",
        ...headers,
      },
      body,
    }),
    { params: Promise.resolve({ path: path.split("/") }) },
  ] as const;
}
it("rejects unapproved upstreams, cross-origin calls, missing credentials and unknown routes without forwarding", async () => {
  const fetch = vi.fn();
  vi.stubGlobal("fetch", fetch);
  expect(
    (
      await GET(
        ...request("v1/status", { "X-Slate-Server": "https://other.example" }),
      )
    ).status,
  ).toBe(403);
  expect(
    (await GET(...request("v1/status", { Origin: "https://other.example" })))
      .status,
  ).toBe(403);
  expect(
    (await GET(...request("v1/status", { Authorization: "" }))).status,
  ).toBe(401);
  expect((await GET(...request("admin/secrets"))).status).toBe(404);
  expect(fetch).not.toHaveBeenCalled();
});
it("forwards revision headers, never forwards cookies or follows credential redirects", async () => {
  const fetch = vi
    .fn()
    .mockResolvedValue(
      new Response(null, {
        status: 302,
        headers: { Location: "https://other.example" },
      }),
    );
  vi.stubGlobal("fetch", fetch);
  expect(
    (
      await POST(
        ...request(
          "v1/notes",
          { "If-Match": '"rev"', Cookie: "session=private" },
          "POST",
          "{}",
        ),
      )
    ).status,
  ).toBe(502);
  const options = fetch.mock.calls[0][1];
  expect(options.redirect).toBe("manual");
  expect(options.headers.get("if-match")).toBe('"rev"');
  expect(options.headers.has("cookie")).toBe(false);
});
it("bounds request bodies even when Content-Length is absent", async () => {
  vi.stubGlobal(
    "fetch",
    vi.fn(async (_url, options) => {
      await new Response(options.body).arrayBuffer();
      return Response.json({});
    }),
  );
  expect(
    (
      await POST(
        ...request(
          "v1/assets",
          {},
          "POST",
          new Uint8Array(26 * 1024 * 1024 + 1),
        ),
      )
    ).status,
  ).toBe(413);
});
it("keeps authenticated responses out of caches and strips upstream cookies", async () => {
  vi.stubGlobal(
    "fetch",
    vi
      .fn()
      .mockResolvedValue(
        Response.json(
          { ok: true },
          { headers: { ETag: '"r"', "Set-Cookie": "secret=x" } },
        ),
      ),
  );
  const response = await GET(...request());
  expect(response.headers.get("cache-control")).toBe("no-store");
  expect(response.headers.get("etag")).toBe('"r"');
  expect(response.headers.has("set-cookie")).toBe(false);
});

it("maps an approved HTTPS identity to an operator-controlled internal upstream", async () => {
  vi.stubEnv("SLATE_UPSTREAM_URL", "http://slate.slate.svc.cluster.local:8080");
  const fetch = vi
    .fn()
    .mockResolvedValue(Response.json({ ok: true }));
  vi.stubGlobal("fetch", fetch);
  const response = await GET(...request());
  expect(response.status).toBe(200);
  expect(fetch.mock.calls[0][0]).toBe(
    "http://slate.slate.svc.cluster.local:8080/v1/status",
  );
  expect(fetch.mock.calls[0][1].headers.get("x-forwarded-proto")).toBe(
    "https",
  );
});

it("accepts browser POSTs from an operator-controlled public origin", async () => {
  vi.stubEnv("SLATE_PUBLIC_ORIGIN", "https://lib.example");
  const fetch = vi.fn().mockResolvedValue(Response.json({ ok: true }));
  vi.stubGlobal("fetch", fetch);
  const response = await POST(
    ...request(
      "v1/sync",
      { Origin: "https://lib.example" },
      "POST",
      "{}",
    ),
  );
  expect(response.status).toBe(200);
  expect(fetch).toHaveBeenCalledOnce();
});

it("rejects a different browser origin when a public origin is configured", async () => {
  vi.stubEnv("SLATE_PUBLIC_ORIGIN", "https://lib.example");
  const fetch = vi.fn();
  vi.stubGlobal("fetch", fetch);
  const response = await POST(
    ...request(
      "v1/sync",
      { Origin: "https://other.example" },
      "POST",
      "{}",
    ),
  );
  expect(response.status).toBe(403);
  expect(fetch).not.toHaveBeenCalled();
});

it("rejects credentials and query strings in the trusted internal upstream", async () => {
  vi.stubEnv("SLATE_UPSTREAM_URL", "http://user:password@slate.svc/?token=x");
  vi.stubGlobal("fetch", vi.fn());
  expect((await GET(...request())).status).toBe(502);
});
