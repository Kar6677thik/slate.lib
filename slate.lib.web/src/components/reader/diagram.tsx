"use client";
import { useEffect, useState, useId } from "react";
import { useTheme } from "next-themes";
import DOMPurify from "dompurify";
export default function Diagram({ code }: { code: string }) {
  const [svg, setSvg] = useState(""),
    [failed, setFailed] = useState(false);
  const id = useId().replace(/[^a-z0-9]/gi, "");
  const { resolvedTheme } = useTheme();
  useEffect(() => {
    let live = true;
    async function render() {
      try {
        if (code.length > 30000) throw new Error("Too large");
        const mermaid = (await import("mermaid")).default;
        mermaid.initialize({
          startOnLoad: false,
          securityLevel: "strict",
          theme: resolvedTheme === "dark" ? "dark" : "neutral",
          maxTextSize: 30000,
          suppressErrorRendering: true,
          htmlLabels: false,
          flowchart: { htmlLabels: false },
        });
        const result = await mermaid.render("diagram" + id, code);
        if (live) {
          setSvg(
            DOMPurify.sanitize(result.svg, {
              USE_PROFILES: { svg: true, svgFilters: true },
              FORBID_TAGS: ["foreignObject", "a"],
              FORBID_ATTR: ["href", "xlink:href"],
            }),
          );
          setFailed(false);
        }
      } catch {
        if (live) setFailed(true);
      }
    }
    void render();
    return () => {
      live = false;
    };
  }, [code, id, resolvedTheme]);
  return failed ? (
    <pre>
      <code>{code}</code>
      <small>Diagram could not be rendered.</small>
    </pre>
  ) : (
    <div
      className="diagram"
      aria-label="Mermaid diagram"
      dangerouslySetInnerHTML={{ __html: svg }}
    />
  );
}
