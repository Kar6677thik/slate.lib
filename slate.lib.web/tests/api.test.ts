import { describe, it, expect, vi, afterEach } from "vitest";
import { SlateApi, normalizeServer, ApiError } from "@/lib/api/client";
afterEach(() => vi.unstubAllGlobals());
describe("connection boundary", () => {
  it("requires HTTPS outside loopback", () => {
    expect(normalizeServer("https://private.example/")).toBe(
      "https://private.example",
    );
    expect(normalizeServer("http://localhost:5080")).toContain("5080");
    for (const value of [
      "http://private.example",
      "https://user:pass@host",
      "https://host/?token=secret",
      "javascript:alert(1)",
    ])
      expect(() => normalizeServer(value)).toThrow();
  });
  it("sends credentials only as headers and retains revision protection", async () => {
    const fetch = vi
      .fn()
      .mockResolvedValue(
        Response.json({
          id: "00000000-0000-4000-8000-000000000001",
          path: "a.md",
          title: "A",
          markdown: "new",
          revision: '"b"',
        }),
      );
    vi.stubGlobal("fetch", fetch);
    await new SlateApi({
      server: "https://private.example",
      token: "secret",
    }).save(
      {
        id: "00000000-0000-4000-8000-000000000001",
        path: "a.md",
        title: "A",
        markdown: "old",
        revision: '"a"',
      },
      "new",
    );
    expect(fetch.mock.calls[0][0]).not.toContain("secret");
    expect(fetch.mock.calls[0][1].headers).toMatchObject({
      Authorization: "Bearer secret",
      "If-Match": '"a"',
    });
  });
  it("reports conflicts without retrying writes", async () => {
    const fetch = vi
      .fn()
      .mockResolvedValue(new Response(null, { status: 412 }));
    vi.stubGlobal("fetch", fetch);
    await expect(
      new SlateApi({ server: "https://private.example", token: "test" }).post(
        "v1/notes",
        {},
      ),
    ).rejects.toBeInstanceOf(ApiError);
    expect(fetch).toHaveBeenCalledTimes(1);
  });
});
