import { createHash } from "node:crypto";
import type { Note } from "@/lib/api/contracts";
import type { ChunkMetadata } from "./types";

const MAX_CHARS = 1400;
const CODE_CAP = 480;

function sha(value: string) {
  return createHash("sha256").update(value).digest("hex");
}

function frontmatter(markdown: string) {
  const match = markdown.match(/^---\s*\n([\s\S]*?)\n---\s*(?:\n|$)/);
  const fields = new Map<string, string>();
  if (match) {
    for (const line of match[1].split("\n")) {
      const pair = line.match(/^([A-Za-z][\w-]*):\s*(.+)$/);
      if (pair) fields.set(pair[1].toLowerCase(), pair[2].trim().replace(/^['"]|['"]$/g, ""));
    }
  }
  const tags = (fields.get("tags") ?? "")
    .replace(/^\[|\]$/g, "")
    .split(/[,\s]+/)
    .map((tag) => tag.replace(/^#/, "").trim())
    .filter(Boolean);
  return { body: match ? markdown.slice(match[0].length) : markdown, tags, type: fields.get("type") ?? null, status: fields.get("status") ?? null };
}

function boundedCode(markdown: string) {
  return markdown.replace(/```([^\n]*)\n([\s\S]*?)```/g, (_all, language: string, code: string) => {
    const clean = code.trim();
    const suffix = clean.length > CODE_CAP ? "\n… code truncated for retrieval" : "";
    return `\`\`\`${language}\n${clean.slice(0, CODE_CAP)}${suffix}\n\`\`\``;
  });
}

export function chunkMarkdown(note: Note): ChunkMetadata[] {
  const meta = frontmatter(note.markdown);
  const lines = boundedCode(meta.body).replace(/\r\n/g, "\n").split("\n");
  const sections: { heading: string | null; blocks: string[] }[] = [];
  let current = { heading: null as string | null, blocks: [] as string[] };
  let paragraph: string[] = [];
  const flushParagraph = () => {
    const text = paragraph.join("\n").trim();
    if (text) current.blocks.push(text);
    paragraph = [];
  };
  const flushSection = () => {
    flushParagraph();
    if (current.blocks.length) sections.push(current);
  };
  for (const line of lines) {
    const heading = line.match(/^#{1,6}\s+(.+)$/);
    if (heading) {
      flushSection();
      current = { heading: heading[1].trim(), blocks: [] };
    } else if (!line.trim()) flushParagraph();
    else paragraph.push(line.trimEnd());
  }
  flushSection();

  const chunks: ChunkMetadata[] = [];
  const occurrences = new Map<string, number>();
  let ordinal = 0;
  for (const section of sections) {
    let buffer = "";
    const emit = () => {
      const text = buffer.trim();
      if (!text) return;
      const retrievalText = [note.title, section.heading, text].filter(Boolean).join("\n\n");
      const contentHash = sha(retrievalText);
      const occurrence = occurrences.get(contentHash) ?? 0;
      occurrences.set(contentHash, occurrence + 1);
      chunks.push({
        id: `${note.id}:${contentHash.slice(0, 24)}:${occurrence}`,
        noteId: note.id,
        path: note.path,
        title: note.title,
        heading: section.heading,
        ordinal: ordinal++,
        text: retrievalText,
        contentHash,
        revision: note.revision,
        tags: meta.tags,
        type: meta.type,
        status: meta.status,
      });
      buffer = "";
    };
    for (const block of section.blocks) {
      if (block.length > MAX_CHARS) {
        emit();
        for (let start = 0; start < block.length; start += MAX_CHARS - 160) {
          buffer = block.slice(start, start + MAX_CHARS);
          emit();
        }
      } else if (buffer && buffer.length + block.length + 2 > MAX_CHARS) {
        emit();
        buffer = block;
      } else buffer = buffer ? `${buffer}\n\n${block}` : block;
    }
    emit();
  }
  return chunks;
}
