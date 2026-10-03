"use client";
import { useEffect, useState, useRef } from "react";
import { useQuery } from "@tanstack/react-query";
import { Download, ImageOff } from "lucide-react";
import { useApi } from "@/lib/auth/context";
import { downloadBlob } from "@/lib/markdown/assets";
import { ErrorMessage } from "@/components/common/primitives";
export function AssetView({
  id,
  label,
  image = false,
}: {
  id: string;
  label: string;
  image?: boolean;
}) {
  const api = useApi();
  const [url, setUrl] = useState(""),
    [visible, setVisible] = useState(false),
    [error, setError] = useState<unknown>(null);
  const anchor = useRef<HTMLSpanElement>(null);
  useEffect(() => {
    if (!anchor.current) return;
    const observer = new IntersectionObserver(
      ([entry]) => {
        if (entry.isIntersecting) {
          setVisible(true);
          observer.disconnect();
        }
      },
      { rootMargin: "300px" },
    );
    observer.observe(anchor.current);
    return () => observer.disconnect();
  }, []);
  const metadata = useQuery({
    queryKey: ["asset-meta", id],
    queryFn: () => api.metadata(id),
    enabled: visible,
  });
  const canImage =
    image &&
    metadata.data?.inlineImage &&
    /^image\/(png|jpeg|webp|gif)$/.test(metadata.data.contentType);
  const blob = useQuery({
    queryKey: ["asset", id],
    queryFn: ({ signal }) => api.asset(id, signal),
    enabled: visible && !!canImage,
    gcTime: 60000,
  });
  useEffect(() => {
    if (!blob.data) return;
    const objectUrl = URL.createObjectURL(blob.data);
    setUrl(objectUrl);
    return () => URL.revokeObjectURL(objectUrl);
  }, [blob.data]);
  return (
    <span className="asset-view" ref={anchor}>
      {canImage && url ? (
        <img src={url} alt={label} loading="lazy" />
      ) : image ? (
        <span className="asset-placeholder">
          <ImageOff size={18} />
          {label}
        </span>
      ) : null}
      <button
        className="attachment-link"
        onClick={async () => {
          try {
            const data = blob.data ?? (await api.asset(id));
            downloadBlob(data, metadata.data?.originalFilename ?? label);
          } catch (e) {
            setError(e);
          }
        }}
      >
        <Download size={14} />
        {metadata.data?.originalFilename ?? label}
        {metadata.data && (
          <small>
            {(metadata.data.byteSize / 1024).toFixed(1)} KB ·{" "}
            {metadata.data.contentType}
          </small>
        )}
      </button>
      {(error ?? metadata.error ?? blob.error) != null && (
        <ErrorMessage error={error ?? metadata.error ?? blob.error} />
      )}
    </span>
  );
}
