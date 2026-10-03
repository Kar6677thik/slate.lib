import type { Asset } from "@/lib/api/contracts";
export function assetId(url: string | undefined) {
  return (
    url?.match(
      /(?:^asset:\/\/|(?:^|\/)\.assets\/)([a-f0-9]{8}-[a-f0-9]{4}-[a-f0-9]{4}-[a-f0-9]{4}-[a-f0-9]{12})(?:\.[a-z0-9]+)?(?:$|[?#])/i,
    )?.[1] ?? null
  );
}
export function assetMarkdown(path: string, asset: Asset) {
  const target =
    "../".repeat(path.split("/").length - 1) +
    ".assets/" +
    asset.id +
    asset.extension;
  const label = asset.originalFilename.replace(/[\[\]\\\r\n]/g, "");
  return `${asset.inlineImage ? "!" : ""}[${label}](${target})`;
}
export function downloadBlob(blob: Blob, name: string) {
  const url = URL.createObjectURL(blob);
  const a = document.createElement("a");
  a.href = url;
  a.download = name;
  a.click();
  setTimeout(() => URL.revokeObjectURL(url), 30000);
}
