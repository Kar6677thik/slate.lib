import { describe, expect, it } from "vitest";
import { RequestMetrics, requestLogLine, routeTemplate, userAgentFamily } from "../scripts/observability/core.mjs";
import { GET } from "@/app/api/health/route";

describe("route templates", () => {
  it("keeps known routes and drops ids, paths and queries", () => {
    expect(routeTemplate("/api/slate/v1/notes/6f483c59-d4ab-4f1b-87b7-989da5fb0cef/links")).toBe("/api/slate/v1/notes/:param/links");
    expect(routeTemplate("/api/slate/v1/library?path=Projects%2Fsecret-plan&page=2")).toBe("/api/slate/v1/library");
    expect(routeTemplate("/api/slate/v1/library/Projects/secret-plan.md")).toBe("/api/slate/v1/library/:param/:param");
    expect(routeTemplate("/api/slate/v1/notes/x/comments/y/replies")).toBe("/api/slate/v1/notes/:param/comments/:param/replies");
    expect(routeTemplate("/api/intelligence/knowledge-gaps")).toBe("/api/intelligence/knowledge-gaps");
    expect(routeTemplate("/api/intelligence/whatever-new")).toBe("/api/intelligence/:other");
    expect(routeTemplate("/?note=6f483c59-d4ab-4f1b-87b7-989da5fb0cef")).toBe("/");
    expect(routeTemplate("/_next/static/chunks/app.js")).toBe("/_next/static/*");
    expect(routeTemplate("/icon-192.png")).toBe("/:asset");
    expect(routeTemplate("/Projects/secret-plan")).toBe("other");
  });

  it("never echoes unknown path text", () => {
    const template = routeTemplate("/api/slate/v1/library/My%20Diary/2026-11-14.md");
    expect(template).not.toMatch(/diary|2026/i);
  });
});

describe("user agent families", () => {
  it("classifies without returning the raw header", () => {
    expect(userAgentFamily("kube-probe/1.31")).toBe("kube-probe");
    expect(userAgentFamily("Mozilla/5.0 (Windows NT 10.0) AppleWebKit/537.36 Chrome/131.0 Safari/537.36")).toBe("chrome");
    expect(userAgentFamily("Mozilla/5.0 (Windows NT 10.0) AppleWebKit/537.36 Chrome/131.0 Safari/537.36 Edg/131.0")).toBe("edge");
    expect(userAgentFamily("Blackbox Exporter/0.25.0")).toBe("monitor");
    expect(userAgentFamily(undefined)).toBe("none");
  });
});

describe("request log lines", () => {
  it("warns on slow requests", () => {
    const line = JSON.parse(requestLogLine({ method: "POST", route: "/api/intelligence/health", status: 200, durationMs: 1234.4, userAgent: "chrome" })!);
    expect(line).toEqual({ level: "warn", msg: "slow request", method: "POST", route: "/api/intelligence/health", status: 200, durationMs: 1234, ua: "chrome" });
  });

  it("skips fast successful probes but keeps failing or slow ones", () => {
    expect(requestLogLine({ method: "GET", route: "/api/health", status: 200, durationMs: 2, userAgent: "kube-probe" })).toBeNull();
    expect(requestLogLine({ method: "GET", route: "/api/health", status: 200, durationMs: 2_400, userAgent: "kube-probe" })).toContain("slow request");
    expect(JSON.parse(requestLogLine({ method: "GET", route: "/", status: 502, durationMs: 20, userAgent: "chrome" })!).level).toBe("warn");
  });
});

describe("metrics", () => {
  it("renders counters, a duration histogram and event-loop delay", () => {
    const metrics = new RequestMetrics();
    metrics.observe({ method: "POST", route: "/api/intelligence/health", status: 200, durationMs: 1_200 });
    metrics.observe({ method: "POST", route: "/api/intelligence/health", status: 200, durationMs: 40 });
    const text = metrics.render({ eventLoop: { p50: 1e7, p99: 2e7, max: 4e9, windowMax: 3e9 }, memory: { rss: 100, heapUsed: 50 } });
    expect(text).toContain('slate_web_http_requests_total{method="POST",route="/api/intelligence/health",status="200"} 2');
    expect(text).toContain('slate_web_http_request_duration_seconds_bucket{route="/api/intelligence/health",le="0.1"} 1');
    expect(text).toContain('slate_web_http_request_duration_seconds_bucket{route="/api/intelligence/health",le="2.5"} 2');
    expect(text).toContain('slate_web_http_request_duration_seconds_count{route="/api/intelligence/health"} 2');
    expect(text).toContain('slate_web_event_loop_delay_seconds{quantile="1"} 4');
    expect(text).toContain("slate_web_event_loop_delay_window_max_seconds 3");
  });
});

describe("liveness route", () => {
  it("answers without I/O and is never cached", async () => {
    const response = GET();
    expect(response.status).toBe(200);
    expect(response.headers.get("cache-control")).toBe("no-store");
    expect(await response.text()).toBe("ok");
  });
});
