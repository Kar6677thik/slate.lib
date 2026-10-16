// @vitest-environment node
import { afterEach, describe, expect, it, vi } from "vitest";
import { fetchSlate, resolveSlateUpstream } from "@/lib/server/slate-upstream";
import { POST as intelligencePost } from "@/app/api/intelligence/[...path]/route";

vi.mock("next/server", () => ({ after: () => undefined }));

afterEach(() => {
  vi.unstubAllEnvs();
  vi.unstubAllGlobals();
});

function request(headers: Record<string, string> = {}) {
  return new Request("https://lib.example/api/intelligence/status", {
    headers: {
      "X-Slate-Server": "https://approved.example",
      Authorization: "Bearer browser-token",
      ...headers,
    },
  });
}

describe("intelligence upstream boundary", () => {
  it("rejects an oversized Ask request before contacting a provider or Slate", async () => {
    const body = "x".repeat(16 * 1024 + 1);
    const response = await intelligencePost(new Request("https://lib.example/api/intelligence/ask", {
      method: "POST",
      headers: { "content-length": String(body.length) },
      body,
    }), { params: Promise.resolve({ path: ["ask"] }) });
    expect(response.status).toBe(413);
  });
  it("accepts only an approved server and configured browser origin", () => {
    vi.stubEnv("SLATE_ALLOWED_SERVERS", "https://approved.example");
    vi.stubEnv("SLATE_PUBLIC_ORIGIN", "https://lib.example");
    vi.stubEnv("SLATE_UPSTREAM_URL", "http://slate.slate.svc.cluster.local:8080");

    expect(
      resolveSlateUpstream(request({ Origin: "https://lib.example" })),
    ).toEqual({
      server: "https://approved.example",
      upstream: "http://slate.slate.svc.cluster.local:8080",
      authorization: "Bearer browser-token",
    });
  });

  it("rejects cross-origin access before any canonical request is made", () => {
    vi.stubEnv("SLATE_ALLOWED_SERVERS", "https://approved.example");
    vi.stubEnv("SLATE_PUBLIC_ORIGIN", "https://lib.example");

    expect(() =>
      resolveSlateUpstream(request({ Origin: "https://attacker.example" })),
    ).toThrow(expect.objectContaining({ status: 403 }));
  });

  it("rejects unapproved servers and missing bearer credentials", () => {
    vi.stubEnv("SLATE_ALLOWED_SERVERS", "https://approved.example");

    expect(() =>
      resolveSlateUpstream(
        request({ "X-Slate-Server": "https://unapproved.example" }),
      ),
    ).toThrow(expect.objectContaining({ status: 403 }));

    expect(() => resolveSlateUpstream(request({ Authorization: "" }))).toThrow(
      expect.objectContaining({ status: 401 }),
    );
  });

  it("rejects credentials and query strings in operator-controlled URLs", () => {
    vi.stubEnv("SLATE_ALLOWED_SERVERS", "https://approved.example");
    vi.stubEnv(
      "SLATE_UPSTREAM_URL",
      "http://user:password@slate.svc/?token=secret",
    );

    expect(() => resolveSlateUpstream(request())).toThrow(
      "Invalid SLATE_UPSTREAM_URL",
    );
  });
});

describe("intelligence credential proof", () => {
  const libraryId = "00000000-0000-4000-8000-0000000000aa";
  function call(path: string, authorization: string, init: RequestInit = {}) {
    return intelligencePost(new Request(`https://lib.example/api/intelligence/${path}`, {
      method: "POST",
      ...init,
      headers: { "X-Slate-Server": "https://approved.example", Authorization: authorization, ...init.headers },
    }), { params: Promise.resolve({ path: path.split("/") }) });
  }

  it("does not let a forged token reuse the library identity proven by another token", async () => {
    vi.stubEnv("SLATE_ALLOWED_SERVERS", "https://approved.example");
    const fetch = vi.fn(async (_url: string, init: RequestInit) =>
      new Headers(init.headers).get("authorization") === "Bearer real-token"
        ? Response.json({ libraryId, noteCount: 1 })
        : new Response(null, { status: 401 }));
    vi.stubGlobal("fetch", fetch);

    expect((await call("unknown-route", "Bearer real-token")).status).toBe(404);
    expect((await call("unknown-route", "Bearer forged")).status).toBe(401);
    expect((await call("events", "Bearer forged", { body: JSON.stringify({ kind: "delete", noteId: libraryId }) })).status).toBe(401);
    expect(fetch).toHaveBeenCalledTimes(3);
  });

  it("tells an internal upstream that the public hop was HTTPS", async () => {
    vi.stubEnv("SLATE_ALLOWED_SERVERS", "https://approved.example");
    vi.stubEnv("SLATE_UPSTREAM_URL", "http://slate.slate.svc.cluster.local:8080");
    const fetch = vi.fn().mockResolvedValue(Response.json({}));
    vi.stubGlobal("fetch", fetch);
    await fetchSlate(resolveSlateUpstream(request()), "v1/status");
    expect(fetch.mock.calls[0][0]).toBe("http://slate.slate.svc.cluster.local:8080/v1/status");
    expect(new Headers(fetch.mock.calls[0][1].headers).get("x-forwarded-proto")).toBe("https");
  });
});
