"use client";
import remarkMath from "remark-math";
import rehypeKatex from "rehype-katex";
import "katex/dist/katex.min.css";
import { MarkdownBody, type MarkdownProps } from "./markdown";
export default function MathReader(props: MarkdownProps) {
  return (
    <MarkdownBody
      {...props}
      remarkExtras={[remarkMath]}
      rehypeExtras={[[rehypeKatex, { trust: false, strict: "warn" }]]}
    />
  );
}
