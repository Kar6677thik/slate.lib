export interface SemanticFilters {
  path?: string;
  tag?: string;
  type?: string;
  status?: string;
}

export function parseSemanticQuery(query: string) {
  const filters: SemanticFilters = {};
  let requiresLexicalFilter = false;
  const text = query.replace(/(?:^|\s)(path|tag|type|status|created|modified|date|has):(?:"([^"]+)"|(\S+))/gi, (_all, key: string, quoted: string, bare: string) => {
    const normalized = key.toLowerCase();
    if (["path", "tag", "type", "status"].includes(normalized))
      filters[normalized as keyof SemanticFilters] = quoted ?? bare;
    else requiresLexicalFilter = true;
    return " ";
  }).replace(/\s+/g, " ").trim();
  return { text, filters, requiresLexicalFilter };
}
