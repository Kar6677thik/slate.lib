/**
 * Resolves a Markdown link written relative to a note (`../Course/07-rate-limits.md#tokens`) to a library path.
 * Returns null for anything that is not a relative link to a Markdown note: URLs, anchors, absolute paths, assets,
 * or a path that climbs above the library root.
 */
export function resolveNoteHref(notePath: string, href: string | undefined) {
  if (!href || /^[a-z][a-z0-9+.-]*:/i.test(href) || href.startsWith("#") || href.startsWith("/") || href.startsWith("//")) return null;
  const [rawPath, ...fragment] = href.split("#");
  let decoded: string;
  try { decoded = decodeURIComponent(rawPath); } catch { return null; }
  if (!/\.md$/i.test(decoded)) return null;
  const parts = notePath.replace(/\\/g, "/").split("/").slice(0, -1);
  for (const segment of decoded.replace(/\\/g, "/").split("/")) {
    if (!segment || segment === ".") continue;
    if (segment === "..") {
      if (!parts.length) return null;
      parts.pop();
    } else parts.push(segment);
  }
  if (!parts.length) return null;
  const heading = fragment.join("#");
  return { path: parts.join("/"), heading: heading ? decodeURIComponent(heading) : null };
}
