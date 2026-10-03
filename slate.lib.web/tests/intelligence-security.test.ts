// @vitest-environment node
import { afterEach, describe, expect, it, vi } from "vitest";
import { resolveSlateUpstream } from "@/lib/server/slate-upstream";
import { POST as intelligencePost } from "@/app/api/intelligence/[...path]/route";

afterEach(() => {
  vi.unstubAllEnvs();
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
