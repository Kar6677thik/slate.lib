/**
 * Lets queued I/O (other requests, health probes) run between CPU-heavy analysis phases. Whole-library analyses are
 * synchronous; chaining several without yielding held Node's single thread long enough for liveness probes to fail.
 */
export function yieldToEventLoop() {
  return new Promise<void>((resolve) => setImmediate(resolve));
}
