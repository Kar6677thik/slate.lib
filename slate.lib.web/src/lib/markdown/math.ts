/**
 * Rewrites LaTeX-style delimiters, \( inline \) and \[ display \], to the $…$ and $$…$$ forms the Markdown math
 * plugin understands. Without this, Markdown reads "\(" and "\[" as escaped brackets and the math shows as raw text.
 * Fenced and inline code are left untouched.
 */
export function normalizeMathDelimiters(markdown: string) {
  if (!markdown.includes("\\(") && !markdown.includes("\\[")) return markdown;
  return markdown
    .split(/(```[\s\S]*?```|~~~[\s\S]*?~~~|`[^`\n]*`)/g)
    .map((part, index) => index % 2
      ? part
      : part
          .replace(/\\\[([\s\S]+?)\\\]/g, (_, body: string) => `\n$$\n${body.trim()}\n$$\n`)
          .replace(/\\\((.+?)\\\)/g, (_, body: string) => `$${body.trim()}$`))
    .join("");
}
