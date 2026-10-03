type Node = {
  type: string;
  value?: string;
  url?: string;
  children?: Node[];
  data?: { hProperties?: Record<string, unknown> };
};
export function slateMarkdown() {
  return (tree: Node) => {
    function visit(node: Node) {
      if (!node.children) return;
      if (
        node.type === "code" ||
        node.type === "inlineCode" ||
        node.type === "link"
      )
        return;
      node.children = node.children.flatMap((child) => {
        if (child.type !== "text" || !child.value) {
          visit(child);
          return [child];
        }
        const text = child.value;
        const result: Node[] = [];
        let start = 0;
        for (const match of text.matchAll(/\[\[([^\]\n]+)\]\]/g)) {
          if (match.index! > start)
            result.push({
              type: "text",
              value: text.slice(start, match.index),
            });
          const [target, label] = match[1].split("|", 2);
          result.push({
            type: "link",
            url: "/__slate/wiki?target=" + encodeURIComponent(target),
            children: [{ type: "text", value: label ?? target }],
          });
          start = match.index! + match[0].length;
        }
        if (start < text.length)
          result.push({ type: "text", value: text.slice(start) });
        return result.length ? result : [child];
      });
      if (node.type === "blockquote") {
        const p = node.children[0];
        const t = p?.children?.[0];
        const m = t?.value?.match(/^\[!([A-Za-z]+)\][+-]?\s*/);
        if (m && p?.children && t) {
          p.children = [
            {
              type: "strong",
              children: [
                {
                  type: "text",
                  value: m[1][0].toUpperCase() + m[1].slice(1).toLowerCase(),
                },
              ],
            },
            { type: "break" },
            { type: "text", value: " " + t.value!.slice(m[0].length) },
            ...p.children.slice(1),
          ];
        }
      }
    }
    visit(tree);
  };
}
export function headingSlug(value: string) {
  return (
    value
      .toLowerCase()
      .replace(/[^\p{L}\p{N}\s-]/gu, "")
      .trim()
      .replace(/\s+/g, "-") || "section"
  );
}
