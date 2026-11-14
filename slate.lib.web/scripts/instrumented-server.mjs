/*
 * Container entry point: installs request logging, event-loop monitoring and the metrics endpoint, then starts the
 * Next.js standalone server. The Dockerfile copies this file next to server.js.
 */
import "./observability/install.mjs";

await import("./server.js");
