/**
 * Liveness check. It does no I/O, so it fails only when the process can't answer at all, for example when the event
 * loop is blocked. Readiness and upstream health are reported elsewhere.
 */
export const dynamic = "force-dynamic";

export function GET() {
  return new Response("ok", { headers: { "content-type": "text/plain; charset=utf-8", "cache-control": "no-store" } });
}
