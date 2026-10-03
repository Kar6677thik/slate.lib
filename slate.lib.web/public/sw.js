const CACHE = "slate-static-v1";
self.addEventListener("install", (event) =>
  event.waitUntil(
    caches
      .open(CACHE)
      .then((cache) =>
        cache.addAll([
          "/offline.html",
          "/icons/slate-192.png",
          "/icons/slate-512.png",
        ]),
      ),
  ),
);
self.addEventListener("activate", (event) =>
  event.waitUntil(
    caches
      .keys()
      .then((keys) =>
        Promise.all(
          keys
            .filter((key) => key.startsWith("slate-static-") && key !== CACHE)
            .map((key) => caches.delete(key)),
        ),
      ),
  ),
);
self.addEventListener("fetch", (event) => {
  const request = event.request,
    url = new URL(request.url);
  if (
    request.method !== "GET" ||
    url.origin !== self.location.origin ||
    request.headers.has("authorization") ||
    url.pathname.startsWith("/api/")
  )
    return;
  if (request.mode === "navigate") {
    event.respondWith(
      fetch(request).catch(() => caches.match("/offline.html")),
    );
    return;
  }
  if (
    !url.pathname.startsWith("/_next/static/") &&
    !url.pathname.startsWith("/icons/")
  )
    return;
  event.respondWith(
    caches.open(CACHE).then(async (cache) => {
      const found = await cache.match(request);
      if (found) return found;
      const response = await fetch(request);
      if (response.ok) {
        const keys = await cache.keys();
        if (keys.length < 200) await cache.put(request, response.clone());
      }
      return response;
    }),
  );
});
