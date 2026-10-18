/**
 * Turns a Markdown excerpt into readable plain text for evidence cards: badges and images disappear, links keep
 * their label, and HTML, heading markers, emphasis and code fences are dropped. Sources sent to a model keep the
 * original Markdown; this is for display only.
 */
export function plainExcerpt(markdown: string) {
  return markdown
    .replace(/^---\r?\n[\s\S]*?\r?\n---\r?\n?/, " ")
    .replace(/```[^\n]*\n?/g, " ")
    .replace(/\[!\[[^\]]*\]\([^)]*\)\]\([^)]*\)/g, " ")
    .replace(/!\[[^\]]*\]\([^)]*\)/g, " ")
    .replace(/\[\[([^\]|#]+)(?:#[^\]|]*)?(?:\|([^\]]+))?\]\]/g, (_, target: string, label?: string) => label ?? target)
    .replace(/\[([^\]]+)\]\([^)]*\)/g, "$1")
    .replace(/<[^>]+>/g, " ")
    .replace(/^\s{0,3}(?:#{1,6}\s+|>\s?|[-*+]\s+\[[ xX]\]\s+)/gm, "")
    .replace(/(\*\*|__)(.+?)\1/g, "$2")
    .replace(/(^|[\s(])[*_]([^*_\n]+)[*_](?=[\s).,;:!?]|$)/g, "$1$2")
    .replace(/`([^`]+)`/g, "$1")
    .replace(/\s+/g, " ")
    .trim();
}
