import { it, expect } from "vitest";
import { assetId, assetMarkdown } from "@/lib/markdown/assets";
it("uses canonical relative asset paths and escapes labels", () => {
  const id = "00000000-0000-4000-8000-000000000001";
  const value = assetMarkdown("Work/Research/Note.md", {
    id,
    originalFilename: "[pic].png",
    extension: ".png",
    inlineImage: true,
    contentType: "image/png",
    byteSize: 100,
  });
  expect(value).toBe(`![pic.png](../../.assets/${id}.png)`);
  expect(assetId(`../../.assets/${id}.png`)).toBe(id);
  expect(assetId("javascript:alert(1)")).toBeNull();
});
