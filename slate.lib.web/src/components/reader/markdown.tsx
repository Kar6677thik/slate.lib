"use client";
import { Children, isValidElement, useState, type ReactNode } from "react";
import dynamic from "next/dynamic";
import ReactMarkdown from "react-markdown";
import remarkGfm from "remark-gfm";
import remarkFrontmatter from "remark-frontmatter";
import rehypeSanitize from "rehype-sanitize";
import rehypeHighlight from "rehype-highlight";
import type { PluggableList } from "unified";
import { Copy, Check } from "lucide-react";
import { assetId } from "@/lib/markdown/assets";
import { AssetView } from "@/components/assets/asset";
import { slateMarkdown, headingSlug } from "@/lib/markdown/slate-markdown";
import { useOptionalApi } from "@/lib/auth/context";
import { resolveNoteHref } from "@/lib/markdown/links";
import { normalizeMathDelimiters } from "@/lib/markdown/math";
import { useWorkspace } from "@/features/notes/workspace-context";
import type { Links } from "@/lib/api/contracts";
import { IconButton } from "@/components/common/primitives";
const Diagram = dynamic(() => import("./diagram"), { ssr: false });
const MathReader = dynamic(() => import("./math-reader"), { ssr: false });
export type MarkdownProps = {
  source: string;
  links?: Links;
  title?: string;
  /** The note's library path, used to resolve relative links to other notes. */
  path?: string;
  /** Called with the source offset of a task list item whose checkbox the reader clicked. */
  onToggleTask?: (offset: number) => void;
};
function plain(children: ReactNode): string {
  return Children.toArray(children)
    .map((c) =>
      typeof c === "string" || typeof c === "number"
        ? String(c)
        : isValidElement<{ children?: ReactNode }>(c)
          ? plain(c.props.children)
          : "",
    )
    .join("");
}
function CodeBlock({ children }: { children: ReactNode }) {
  const [copied, setCopied] = useState(false);
  const child = Children.toArray(children)[0];
  const code = plain(children);
  if (
    isValidElement<{ className?: string }>(child) &&
    child.props.className?.includes("language-mermaid")
  )
    return <Diagram code={code} />;
  return (
    <div className="code-block">
      <div className="code-caption">
        <span>Code</span>
        <IconButton
          label={copied ? "Copied" : "Copy code"}
          onClick={() =>
            void navigator.clipboard
              .writeText(code)
              .then(() => {
                setCopied(true);
                setTimeout(() => setCopied(false), 1500);
              })
              .catch(() => setCopied(false))
          }
        >
          {copied ? <Check size={14} /> : <Copy size={14} />}
        </IconButton>
      </div>
      <pre>{children}</pre>
    </div>
  );
}
export default function Markdown(props: MarkdownProps) {
  const source = normalizeMathDelimiters(props.source);
  // Rewriting delimiters shifts source offsets, so task toggling only stays enabled when nothing was rewritten.
  const rendered = source === props.source ? props : { ...props, source, onToggleTask: undefined };
  return /\$[^\n$]+\$|\$\$/.test(source) ? (
    <MathReader {...rendered} />
  ) : (
    <MarkdownBody {...rendered} />
  );
}
export function MarkdownBody({
  source,
  links,
  title,
  path,
  onToggleTask,
  remarkExtras = [],
  rehypeExtras = [],
}: MarkdownProps & {
  remarkExtras?: PluggableList;
  rehypeExtras?: PluggableList;
}) {
  const w = useWorkspace();
  const api = useOptionalApi();
  const seen = new Map<string, number>();
  function heading(level: 1 | 2 | 3 | 4 | 5 | 6, children: ReactNode) {
    const base = headingSlug(plain(children));
    const count = seen.get(base) ?? 0;
    seen.set(base, count + 1);
    const id = base + (count ? `-${count}` : "");
    const H = `h${level}` as const;
    if (
      level === 1 &&
      title === plain(children) &&
      seen.size === 1 &&
      count === 0
    ) {
      return <span id={id} />;
    }
    return (
      <H id={id}>
        <a
          className="heading-anchor"
          href={"#" + id}
          aria-label={`Link to ${plain(children)}`}
        >
          #
        </a>
        {children}
      </H>
    );
  }
  return (
    <article className="markdown">
      <ReactMarkdown
        remarkPlugins={[
          remarkGfm,
          remarkFrontmatter,
          slateMarkdown,
          ...remarkExtras,
        ]}
        rehypePlugins={[
          rehypeSanitize,
          [rehypeHighlight, { detect: false, ignoreMissing: true }],
          ...rehypeExtras,
        ]}
        skipHtml
        components={{
          li: ({ node, children, className }) => (
            <li
              className={className}
              data-task-offset={
                className?.includes("task-list-item")
                  ? node?.position?.start.offset
                  : undefined
              }
            >
              {children}
            </li>
          ),
          input: ({ checked }) => (
            <input
              type="checkbox"
              disabled={!onToggleTask}
              checked={!!checked}
              readOnly
              aria-label={
                onToggleTask
                  ? checked
                    ? "Mark task as not done"
                    : "Mark task as done"
                  : checked
                    ? "Completed task"
                    : "Incomplete task"
              }
              onClick={(event) => {
                event.preventDefault();
                const offset = Number(
                  event.currentTarget.closest("li")?.dataset.taskOffset,
                );
                if (onToggleTask && Number.isFinite(offset))
                  onToggleTask(offset);
              }}
            />
          ),
          h1: ({ children }) => heading(1, children),
          h2: ({ children }) => heading(2, children),
          h3: ({ children }) => heading(3, children),
          h4: ({ children }) => heading(4, children),
          h5: ({ children }) => heading(5, children),
          h6: ({ children }) => heading(6, children),
          pre: ({ children }) => <CodeBlock>{children}</CodeBlock>,
          a: ({ children, href }) => {
            const id = assetId(href);
            if (id) return <AssetView id={id} label={plain(children)} />;
            const wiki = href?.startsWith("/__slate/wiki?");
            const target = wiki
              ? new URL(href!, "https://slate.invalid").searchParams.get(
                  "target",
                )
              : href;
            const link = links?.outgoing.find(
              (l) =>
                l.target === target ||
                l.raw === target ||
                l.raw === `[[${target}]]` ||
                l.target + (l.heading ? "#" + l.heading : "") === target,
            );
            if (wiki || link) {
              return (
                <button
                  className={`wiki-link ${link?.state === "resolved" ? "" : "unresolved"}`}
                  title={
                    link?.state === "resolved"
                      ? "Open note"
                      : link?.state === "ambiguous"
                        ? "Ambiguous note title"
                        : "Find this note"
                  }
                  onClick={() => {
                    if (link?.targetId) w.open(link.targetId);
                    else {
                      w.setQuery(target ?? plain(children));
                      w.navigate("search");
                    }
                  }}
                >
                  {children}
                  {link?.state !== "resolved" && (
                    <small> {link?.state === "ambiguous" ? "?" : "↗"}</small>
                  )}
                </button>
              );
            }
            // Relative links to other notes open inside Slate, even before (or without) the server's link index.
            const local = path && api ? resolveNoteHref(path, href) : null;
            if (local && api)
              return (
                <button
                  className="wiki-link"
                  title="Open note"
                  onClick={() =>
                    void api
                      .noteByPath(local.path)
                      .then((target) => w.open(target.id))
                      .catch(() => {
                        w.setQuery(local.path.split("/").at(-1)!.replace(/\.md$/i, ""));
                        w.navigate("search");
                      })
                  }
                >
                  {children}
                </button>
              );
            return (
              <a
                href={href}
                target={href?.startsWith("#") ? undefined : "_blank"}
                rel="noopener noreferrer"
              >
                {children}
              </a>
            );
          },
          img: ({ alt, src }) => {
            const id = assetId(typeof src === "string" ? src : undefined);
            return id ? (
              <AssetView id={id} label={alt ?? "Image"} image />
            ) : (
              <span className="muted">
                External image: {alt ?? "image"} (not loaded)
              </span>
            );
          },
        }}
      >
        {source}
      </ReactMarkdown>
    </article>
  );
}
