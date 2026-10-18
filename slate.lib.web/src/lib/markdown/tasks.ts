/**
 * Flips the task marker of the list item that starts at `offset` ("- [ ] x" <-> "- [x] x").
 * Returns null when no task marker starts that line, so a stale offset can never edit the wrong text.
 */
export function toggleTaskAt(markdown: string, offset: number) {
  if (!Number.isInteger(offset) || offset < 0 || offset >= markdown.length) return null;
  const lineStart = markdown.lastIndexOf("\n", offset - 1) + 1;
  const lineEnd = markdown.indexOf("\n", offset);
  const line = markdown.slice(lineStart, lineEnd < 0 ? undefined : lineEnd);
  const match = line.match(/^(\s*(?:>\s*)*(?:[-*+]|\d+[.)])\s+)\[([ xX])\]/);
  if (!match) return null;
  const at = lineStart + match[1].length + 1;
  return markdown.slice(0, at) + (match[2] === " " ? "x" : " ") + markdown.slice(at + 1);
}
