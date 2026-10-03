import type { SearchHit } from "@/lib/api/contracts";
import type { HybridHit, SemanticHit } from "./types";

export function fuseResults(query: string, lexical: SearchHit[], semantic: SemanticHit[], diagnostics = false): HybridHit[] {
  const exact = query
    .replace(/(?:^|\s)[a-z][\w-]*:(?:"[^"]*"|\S+)/gi, " ")
    .replace(/\s+/g, " ")
    .trim()
    .toLocaleLowerCase();
  const byId = new Map<string, HybridHit & { _score: number; _lex?: number; _sem?: number }>();
  lexical.forEach((hit, index) => {
    const title = hit.title.trim().toLocaleLowerCase();
    const titleBoost = title === exact ? 2 : exact && title.startsWith(exact) ? 1 : 0;
    byId.set(hit.id, { ...hit, match: "keyword", _score: titleBoost + 1 / (60 + index + 1), _lex: index + 1 });
  });
  semantic.forEach((hit, index) => {
    const current = byId.get(hit.id);
    const score = 1 / (60 + index + 1);
    if (current) {
      current._score += score;
      current._sem = index + 1;
      current.match = "both";
      if (!current.snippet) current.snippet = hit.snippet;
      current.heading = hit.heading;
    } else byId.set(hit.id, { ...hit, match: "meaning", _score: score, _sem: index + 1 });
  });
  return [...byId.values()]
    .sort((a, b) => b._score - a._score || a.title.localeCompare(b.title))
    .map(({ _score, _lex, _sem, ...hit }) => ({
      ...hit,
      explanation: diagnostics ? {
        lexicalRank: _lex,
        semanticRank: _sem,
        fusedScore: _score,
        matchedFields: hit.match === "both" ? ["lucene", "chunk"] : [hit.match === "meaning" ? "chunk" : "lucene"],
        chunkId: "chunkId" in hit && typeof hit.chunkId === "string" ? hit.chunkId : undefined,
        matchedHeading: hit.heading,
      } : undefined,
    }));
}
