/*
 * Pure helpers for request logging and metrics. No side effects, so tests can import them.
 *
 * Route templates come from an allowlist. Anything not on it becomes ":param" or "other", so note titles, folder
 * paths, query strings and IDs never reach logs or metrics, and metric label cardinality stays bounded.
 */

const SLATE_SEGMENTS = new Set([
  "v1", "notes", "library", "search", "assets", "status", "wiki-export", "related", "history", "comments", "links", "issues",
  "workflows", "daily", "views", "sync", "rediscovery", "notifications", "summary", "read", "restore", "link-repair",
  "preview", "apply", "graph", "visibility", "replies", "delete", "answer", "by-path", "rename", "refresh", "item", "bulk",
  "deleted", "folders",
]);
const INTELLIGENCE_ROUTES = new Set([
  "ask", "concepts", "events", "evolution", "health", "inbox-triage", "knowledge-gaps", "knowledge-issues", "knowledge-overlap",
  "link-opportunities", "project-brain", "rebuild", "related", "search", "status",
]);
const PAGES = new Set(["/", "/manifest.webmanifest", "/favicon.ico", "/robots.txt", "/api/health"]);

/** Maps a request URL to a low-cardinality route template that contains no user content. */
export function routeTemplate(url) {
  let pathname;
  try { pathname = new URL(url ?? "/", "http://local").pathname; } catch { return "other"; }
  if (PAGES.has(pathname)) return pathname;
  if (pathname.startsWith("/_next/static/")) return "/_next/static/*";
  if (pathname.startsWith("/_next/image")) return "/_next/image";
  if (pathname.startsWith("/_next/")) return "/_next/*";
  const parts = pathname.split("/").filter(Boolean);
  if (parts[0] === "api" && parts[1] === "slate") {
    const rest = parts.slice(2, 9).map((part) => (SLATE_SEGMENTS.has(part) ? part : ":param"));
    return `/api/slate/${rest.join("/")}${parts.length > 9 ? "/…" : ""}`.replace(/\/$/, "");
  }
  if (parts[0] === "api" && parts[1] === "intelligence") {
    const name = parts.slice(2).join("/");
    return `/api/intelligence/${INTELLIGENCE_ROUTES.has(name) ? name : ":other"}`;
  }
  if (parts[0] === "api") return "/api/:other";
  if (parts.length === 1 && /\.(?:png|svg|ico|webp|jpg|jpeg|json|txt|woff2?)$/i.test(parts[0])) return "/:asset";
  return "other";
}

/** Coarse user-agent family; the raw header is never logged. */
export function userAgentFamily(header) {
  const ua = String(header ?? "");
  if (!ua) return "none";
  if (/kube-probe/i.test(ua)) return "kube-probe";
  if (/blackbox|prometheus|victoriametrics|vmagent/i.test(ua)) return "monitor";
  if (/openai|chatgpt/i.test(ua)) return "openai";
  if (/claude|anthropic/i.test(ua)) return "anthropic";
  if (/curl\//i.test(ua)) return "curl";
  if (/^node|undici|node-fetch/i.test(ua)) return "node";
  if (/bot|crawler|spider/i.test(ua)) return "bot";
  if (/Edg\//.test(ua)) return "edge";
  if (/Firefox\//.test(ua)) return "firefox";
  if (/Chrome\/|CriOS\//.test(ua)) return "chrome";
  if (/Safari\//.test(ua)) return "safari";
  return "other";
}

export const SLOW_REQUEST_MS = 500;
export const DURATION_BUCKETS_SECONDS = [0.005, 0.025, 0.1, 0.25, 0.5, 1, 2.5, 5, 10, 30];

/** One structured log line per request. Successful probe and static requests are skipped to keep logs readable. */
export function requestLogLine({ method, route, status, durationMs, userAgent }) {
  const quiet = (route === "/api/health" || route === "/manifest.webmanifest" || route === "/_next/static/*") && status < 400 && durationMs < SLOW_REQUEST_MS;
  if (quiet) return null;
  return JSON.stringify({
    level: durationMs >= SLOW_REQUEST_MS || status >= 500 ? "warn" : "info",
    msg: durationMs >= SLOW_REQUEST_MS ? "slow request" : "request",
    method,
    route,
    status,
    durationMs: Math.round(durationMs),
    ua: userAgent,
  });
}

/** Minimal Prometheus registry: request counts, a duration histogram by route, and event-loop delay. */
export class RequestMetrics {
  constructor() { this.requests = new Map(); this.durations = new Map(); }

  observe({ method, route, status, durationMs }) {
    const countKey = `${method}\u0000${route}\u0000${status}`;
    this.requests.set(countKey, (this.requests.get(countKey) ?? 0) + 1);
    let histogram = this.durations.get(route);
    if (!histogram) { histogram = { buckets: DURATION_BUCKETS_SECONDS.map(() => 0), sum: 0, count: 0 }; this.durations.set(route, histogram); }
    const seconds = durationMs / 1000;
    DURATION_BUCKETS_SECONDS.forEach((bound, index) => { if (seconds <= bound) histogram.buckets[index]++; });
    histogram.sum += seconds;
    histogram.count++;
  }

  render({ eventLoop, memory } = {}) {
    const label = (value) => String(value).replace(/\\/g, "\\\\").replace(/"/g, '\\"').replace(/\n/g, "\\n");
    const lines = [
      "# HELP slate_web_http_requests_total Completed HTTP requests by method, route template and status.",
      "# TYPE slate_web_http_requests_total counter",
    ];
    for (const [key, value] of this.requests) {
      const [method, route, status] = key.split("\u0000");
      lines.push(`slate_web_http_requests_total{method="${label(method)}",route="${label(route)}",status="${label(status)}"} ${value}`);
    }
    lines.push("# HELP slate_web_http_request_duration_seconds HTTP request duration by route template.", "# TYPE slate_web_http_request_duration_seconds histogram");
    for (const [route, histogram] of this.durations) {
      DURATION_BUCKETS_SECONDS.forEach((bound, index) => lines.push(`slate_web_http_request_duration_seconds_bucket{route="${label(route)}",le="${bound}"} ${histogram.buckets[index]}`));
      lines.push(`slate_web_http_request_duration_seconds_bucket{route="${label(route)}",le="+Inf"} ${histogram.count}`);
      lines.push(`slate_web_http_request_duration_seconds_sum{route="${label(route)}"} ${histogram.sum}`);
      lines.push(`slate_web_http_request_duration_seconds_count{route="${label(route)}"} ${histogram.count}`);
    }
    if (eventLoop) {
      lines.push(
        "# HELP slate_web_event_loop_delay_seconds Event-loop delay since process start (perf_hooks.monitorEventLoopDelay).",
        "# TYPE slate_web_event_loop_delay_seconds gauge",
        `slate_web_event_loop_delay_seconds{quantile="0.5"} ${eventLoop.p50 / 1e9}`,
        `slate_web_event_loop_delay_seconds{quantile="0.99"} ${eventLoop.p99 / 1e9}`,
        `slate_web_event_loop_delay_seconds{quantile="1"} ${eventLoop.max / 1e9}`,
        "# HELP slate_web_event_loop_delay_window_max_seconds Largest event-loop delay in the last reporting window.",
        "# TYPE slate_web_event_loop_delay_window_max_seconds gauge",
        `slate_web_event_loop_delay_window_max_seconds ${eventLoop.windowMax / 1e9}`,
      );
    }
    if (memory) {
      lines.push("# HELP slate_web_process_resident_memory_bytes Resident set size.", "# TYPE slate_web_process_resident_memory_bytes gauge", `slate_web_process_resident_memory_bytes ${memory.rss}`);
      lines.push("# HELP slate_web_process_heap_used_bytes V8 heap in use.", "# TYPE slate_web_process_heap_used_bytes gauge", `slate_web_process_heap_used_bytes ${memory.heapUsed}`);
    }
    return `${lines.join("\n")}\n`;
  }
}
