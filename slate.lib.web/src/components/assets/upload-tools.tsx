"use client";
import { useRef } from "react";
import { ImagePlus, Paperclip } from "lucide-react";
import { IconButton } from "@/components/common/primitives";
export function UploadTools({
  onFiles,
  busy,
}: {
  onFiles: (files: File[]) => void;
  busy: boolean;
}) {
  const input = useRef<HTMLInputElement>(null);
  return (
    <div className="inline-actions upload-tools">
      <input
        ref={input}
        type="file"
        hidden
        multiple
        aria-label="Upload attachments"
        onChange={(e) => {
          onFiles(Array.from(e.target.files ?? []));
          e.target.value = "";
        }}
      />
      <IconButton
        label="Upload image"
        disabled={busy}
        onClick={() => {
          if (input.current) {
            input.current.accept = "image/png,image/jpeg,image/webp,image/gif";
            input.current.click();
          }
        }}
      >
        <ImagePlus size={17} />
      </IconButton>
      <IconButton
        label="Attach file"
        disabled={busy}
        onClick={() => {
          if (input.current) {
            input.current.accept = "";
            input.current.click();
          }
        }}
      >
        <Paperclip size={17} />
      </IconButton>
    </div>
  );
}
