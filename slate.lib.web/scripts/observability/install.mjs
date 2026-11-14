/*
 * Request logging, event-loop monitoring and a metrics endpoint for the Next.js standalone server.
 * Import this before server.js. It observes requests through Node's built-in `http.server.request.start`
 * diagnostics channel, so Next.js itself is not patched.
 *
 * Environment:
 *   SLATE_WEB_METRICS_PORT  port for GET /metrics (default 9464; "0" or "off" disables it). Not exposed by the
 *                           Service: scrape the pod directly from inside the cluster.
 *   SLATE_WEB_REQUEST_LOG   "off" disables per-request log lines.
 */
import { subscribe } from "node:diagnostics_channel";
import { createServer } from "node:http";
import { monitorEventLoopDelay } from "node:perf_hooks";
import { RequestMetrics, requestLogLine, routeTemplate, userAgentFamily } from "./core.mjs";

const metrics = new RequestMetrics();
const logRequests = process.env.SLATE_WEB_REQUEST_LOG !== "off";

subscribe("http.server.request.start", ({ request, response }) => {
  const started = process.hrtime.bigint();
  response.once("close", () => {
    const durationMs = Number(process.hrtime.bigint() - started) / 1e6;
    // A request the client abandoned before a response was sent is recorded as 499, as nginx does.
    const status = response.writableFinished ? response.statusCode : 499;
    const entry = { method: request.method ?? "GET", route: routeTemplate(request.url), status, durationMs, userAgent: userAgentFamily(request.headers["user-agent"]) };
    metrics.observe(entry);
    if (logRequests) {
      const line = requestLogLine(entry);
      if (line) console.log(line);
    }
  });
});

// Event-loop delay: a blocked loop is what makes probes time out, so it is both logged and exported.
// Recorded delays include the sampling interval itself, so it is subtracted before reporting.
const RESOLUTION_MS = 20;
const lag = (nanoseconds) => Math.max(0, nanoseconds - RESOLUTION_MS * 1e6);
const histogram = monitorEventLoopDelay({ resolution: RESOLUTION_MS });
histogram.enable();
const windowHistogram = monitorEventLoopDelay({ resolution: RESOLUTION_MS });
windowHistogram.enable();
let windowMax = 0;
setInterval(() => {
  windowMax = lag(windowHistogram.max);
  if (windowMax / 1e6 >= 1_000) console.log(JSON.stringify({ level: "warn", msg: "event loop blocked", maxDelayMs: Math.round(windowMax / 1e6), windowSeconds: 10 }));
  windowHistogram.reset();
}, 10_000).unref();

const metricsPort = process.env.SLATE_WEB_METRICS_PORT ?? "9464";
if (metricsPort !== "0" && metricsPort !== "off") {
  const server = createServer((request, response) => {
    if (request.method !== "GET" || request.url !== "/metrics") { response.writeHead(404).end(); return; }
    response.writeHead(200, { "content-type": "text/plain; version=0.0.4; charset=utf-8", "cache-control": "no-store" });
    response.end(metrics.render({ eventLoop: { p50: lag(histogram.percentile(50)), p99: lag(histogram.percentile(99)), max: lag(histogram.max), windowMax }, memory: process.memoryUsage() }));
  });
  server.on("error", (error) => console.error(JSON.stringify({ level: "error", msg: "metrics endpoint unavailable", code: error.code })));
  server.listen(Number(metricsPort), process.env.SLATE_WEB_METRICS_HOST ?? "0.0.0.0");
  server.unref();
}
