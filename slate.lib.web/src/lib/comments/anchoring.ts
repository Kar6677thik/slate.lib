/**
 * Text-quote anchoring for reader comments, in the style of the W3C Web Annotation TextQuoteSelector (as used by
 * Hypothesis). A passage is stored as the exact rendered text plus a little context on each side. Re-anchoring
 * searches the rendered text again, so an anchor survives edits elsewhere in the note and only detaches when the
 * passage itself changes.
 */
import type { CommentAnchor } from "@/lib/api/contracts";

export const CONTEXT_LENGTH = 32;
export const MAX_QUOTE_LENGTH = 1_000;

/** Elements whose text is reader chrome rather than note content. */
const IGNORED = ".heading-anchor, .code-caption, .katex-mathml, [data-comment-ignore]";

export type TextIndex = {
  text: string;
  nodes: { node: Text; start: number; end: number }[];
};

/** Concatenates the content text nodes under a root, remembering where each node sits in the combined text. */
export function buildTextIndex(root: Node): TextIndex {
  const nodes: TextIndex["nodes"] = [];
  let text = "";
  const walker = root.ownerDocument!.createTreeWalker(root, NodeFilter.SHOW_TEXT, {
    acceptNode: (node) =>
      node.parentElement?.closest(IGNORED) ? NodeFilter.FILTER_REJECT : NodeFilter.FILTER_ACCEPT,
  });
  for (let node = walker.nextNode() as Text | null; node; node = walker.nextNode() as Text | null) {
    const value = node.data;
    if (!value) continue;
    nodes.push({ node, start: text.length, end: text.length + value.length });
    text += value;
  }
  return { text, nodes };
}

/** The combined-text offset of a DOM boundary point, or null when the point is outside the indexed content. */
export function offsetOf(index: TextIndex, container: Node, offset: number): number | null {
  const direct = index.nodes.find((entry) => entry.node === container);
  if (direct) return direct.start + Math.min(offset, direct.node.length);
  const doc = container.ownerDocument ?? (container as Document);
  const point = doc.createRange();
  try {
    point.setStart(container, offset);
  } catch {
    return null;
  }
  point.collapse(true);
  // The first text node that ends after the point is where the point lands.
  for (const entry of index.nodes) {
    if (point.comparePoint(entry.node, entry.node.length) > 0) return entry.start;
  }
  return index.nodes.length ? index.text.length : null;
}

/** Captures a TextQuoteSelector for a DOM selection range inside the indexed root, trimmed of edge whitespace. */
export function quoteFromRange(index: TextIndex, range: Range): CommentAnchor | null {
  let start = offsetOf(index, range.startContainer, range.startOffset);
  let end = offsetOf(index, range.endContainer, range.endOffset);
  if (start === null || end === null || end <= start) return null;
  while (start < end && /\s/.test(index.text[start])) start++;
  while (end > start && /\s/.test(index.text[end - 1])) end--;
  if (end <= start) return null;
  const exact = index.text.slice(start, end);
  if (exact.length > MAX_QUOTE_LENGTH) return null;
  return {
    kind: "text",
    exact,
    prefix: index.text.slice(Math.max(0, start - CONTEXT_LENGTH), start),
    suffix: index.text.slice(end, end + CONTEXT_LENGTH),
  };
}

/** Collapses whitespace runs to single spaces, keeping a map from each output character to its input offset. */
function collapse(value: string) {
  let text = "";
  const map: number[] = [];
  let space = true;
  for (let i = 0; i < value.length; i++) {
    if (/\s/.test(value[i])) {
      if (space) continue;
      text += " ";
      map.push(i);
      space = true;
    } else {
      text += value[i];
      map.push(i);
      space = false;
    }
  }
  return { text, map };
}

function agreement(side: string, context: string, fromEnd: boolean) {
  const a = collapse(side).text;
  const b = collapse(context).text.trim();
  let count = 0;
  if (fromEnd) for (let i = a.length - 1, j = b.length - 1; i >= 0 && j >= 0 && a[i] === b[j]; i--, j--) count++;
  else {
    const left = a.trimStart();
    for (let i = 0; i < left.length && i < b.length && left[i] === b[i]; i++) count++;
  }
  return count;
}

/**
 * Finds an anchor's passage in combined text. Whitespace differences are ignored; when the passage occurs more than
 * once, the occurrence whose surroundings best agree with the stored prefix and suffix wins.
 */
export function locateQuote(text: string, anchor: CommentAnchor): { start: number; end: number } | null {
  if (anchor.kind !== "text" || !anchor.exact?.trim()) return null;
  const haystack = collapse(text);
  const needle = collapse(anchor.exact).text.trim();
  if (!needle) return null;
  let best: number | null = null;
  let bestScore = -1;
  for (let at = haystack.text.indexOf(needle); at >= 0; at = haystack.text.indexOf(needle, at + 1)) {
    const before = text.slice(Math.max(0, haystack.map[at] - 200), haystack.map[at]);
    const endOffset = haystack.map[at + needle.length - 1] + 1;
    const after = text.slice(endOffset, endOffset + 200);
    const score = agreement(before, anchor.prefix ?? "", true) + agreement(after, anchor.suffix ?? "", false);
    if (score > bestScore) {
      bestScore = score;
      best = at;
    }
  }
  if (best === null) return null;
  return { start: haystack.map[best], end: haystack.map[best + needle.length - 1] + 1 };
}

/** Builds a DOM range for a combined-text span. */
export function rangeFor(index: TextIndex, start: number, end: number): Range | null {
  const first = index.nodes.find((entry) => start < entry.end);
  const last = [...index.nodes].reverse().find((entry) => end > entry.start);
  if (!first || !last) return null;
  const range = first.node.ownerDocument.createRange();
  range.setStart(first.node, Math.max(0, start - first.start));
  range.setEnd(last.node, Math.min(last.node.length, end - last.start));
  return range;
}
