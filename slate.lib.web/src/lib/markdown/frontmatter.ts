const block = /^---[ \t]*\r?\n([\s\S]*?)\r?\n---[ \t]*(?:\r?\n|$)/;

/** Reads one top-level scalar field from a note's YAML frontmatter, or null when it is absent. */
export function frontmatterField(markdown: string, key: string) {
  const fields = markdown.match(block)?.[1] ?? "";
  for (const line of fields.split(/\r?\n/)) {
    const match = line.match(/^([A-Za-z][\w-]*):\s*(.*?)\s*$/);
    if (match && match[1].toLowerCase() === key.toLowerCase()) return match[2].replace(/^['"]|['"]$/g, "") || null;
  }
  return null;
}

/**
 * Sets (or with null, removes) one top-level scalar field in a note's frontmatter, creating the block when needed.
 * Other fields, their order, and the note body are left exactly as written.
 */
export function setFrontmatterField(markdown: string, key: string, value: string | null) {
  const newline = markdown.includes("\r\n") ? "\r\n" : "\n";
  const line = (text: string) => `${key}: ${/^[\w./-]+$/.test(text) ? text : JSON.stringify(text)}`;
  const match = markdown.match(block);
  if (!match) return value === null ? markdown : `---${newline}${line(value)}${newline}---${newline}${newline}${markdown}`;
  const lines = match[1].split(/\r?\n/);
  const index = lines.findIndex((entry) => entry.match(/^([A-Za-z][\w-]*):/)?.[1].toLowerCase() === key.toLowerCase());
  if (value === null) {
    if (index < 0) return markdown;
    lines.splice(index, 1);
  } else if (index >= 0) lines[index] = line(value);
  else lines.push(line(value));
  const fields = lines.filter((entry, position) => entry.trim() || position < lines.length - 1);
  const rebuilt = fields.length ? `---${newline}${fields.join(newline)}${newline}---${newline}` : "";
  return rebuilt + markdown.slice(match[0].length);
}
