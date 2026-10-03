"use client";
import { useState } from "react";
import { useQuery } from "@tanstack/react-query";
import { format } from "date-fns";
import { useApi } from "@/lib/auth/context";
import {
  Modal,
  Loading,
  ErrorMessage,
  Empty,
} from "@/components/common/primitives";
import dynamic from "next/dynamic";
const Markdown = dynamic(() => import("@/components/reader/markdown"), {
  ssr: false,
});
export function HistoryPanel({ id }: { id: string }) {
  const api = useApi();
  const [commit, setCommit] = useState<string | null>(null);
  const q = useQuery({
    queryKey: ["history", id],
    queryFn: ({ signal }) => api.history(id, signal),
  });
  const version = useQuery({
    queryKey: ["historical", id, commit],
    queryFn: () => api.historical(id, commit!),
    enabled: !!commit,
  });
  return (
    <div className="rail-content">
      <h3>Version history</h3>
      {q.isPending ? (
        <Loading />
      ) : q.error ? (
        <ErrorMessage error={q.error} retry={() => q.refetch()} />
      ) : q.data.length ? (
        q.data.map((item) => (
          <button
            className="history-row"
            key={item.commit}
            onClick={() => setCommit(item.commit)}
          >
            <span>
              {format(new Date(item.timestamp), "MMM d, yyyy · HH:mm")}
            </span>
            <strong>{item.message}</strong>
            <small>{item.author}</small>
          </button>
        ))
      ) : (
        <Empty
          title="No committed versions"
          detail="Saved changes appear here after the server records them in history."
        />
      )}
      <Modal
        open={!!commit}
        onClose={() => setCommit(null)}
        title="Historical version"
        description="Read-only snapshot. The current note is unchanged."
        wide
      >
        {version.isPending ? (
          <Loading />
        ) : version.error ? (
          <ErrorMessage error={version.error} />
        ) : (
          version.data && (
            <>
              <p className="fine-print">
                {format(new Date(version.data.timestamp), "PPpp")}
              </p>
              <Markdown source={version.data.markdown} />
            </>
          )
        )}
      </Modal>
    </div>
  );
}
