"use client";
import { useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { format } from "date-fns";
import { useApi } from "@/lib/auth/context";
import {
  Modal,
  Loading,
  ErrorMessage,
  Empty,
} from "@/components/common/primitives";
import dynamic from "next/dynamic";
import { Button } from "@/components/ui/button";
import { useWorkspace } from "@/features/notes/workspace-context";
const Markdown = dynamic(() => import("@/components/reader/markdown"), {
  ssr: false,
});
export function HistoryPanel({ id }: { id: string }) {
  const api = useApi();
  const workspace = useWorkspace();
  const cache = useQueryClient();
  const [commit, setCommit] = useState<string | null>(null);
  const [compareCommit, setCompareCommit] = useState("current");
  const [asNew, setAsNew] = useState(false);
  const [folder, setFolder] = useState("");
  const [name, setName] = useState("");
  const q = useQuery({
    queryKey: ["history", id],
    queryFn: ({ signal }) => api.history(id, signal),
  });
  const version = useQuery({
    queryKey: ["historical", id, commit],
    queryFn: () => api.historical(id, commit!),
    enabled: !!commit,
  });
  const current = useQuery({
    queryKey: ["note", id],
    queryFn: ({ signal }) => api.note(id, signal),
  });
  const comparison = useQuery({
    queryKey: ["historical", id, compareCommit],
    queryFn: () => api.historical(id, compareCommit),
    enabled: compareCommit !== "current",
  });
  const restore = useMutation({
    mutationFn: () => {
      if (!commit || !current.data) throw new Error("The current note is not ready.");
      return api.restore(
        id,
        asNew
          ? {
              commit,
              mode: "new",
              folder: folder.trim(),
              name: name.trim(),
            }
          : {
              commit,
              mode: "current",
              revision: current.data.revision,
            },
      );
    },
    onSuccess: (note) => {
      cache.setQueryData(["note", note.id], note);
      void cache.invalidateQueries({ queryKey: ["history"] });
      void cache.invalidateQueries({ queryKey: ["folder"] });
      void cache.invalidateQueries({ queryKey: ["search"] });
      workspace.open(note.id);
      setCommit(null);
      setAsNew(false);
    },
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
            onClick={() => {
              setCompareCommit("current");
              setCommit(item.commit);
            }}
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
        onClose={() => {
          setCommit(null);
          setCompareCommit("current");
        }}
        title="Historical version"
        description="Compare this snapshot with the current note before choosing an explicit restore action."
        wide
      >
        {version.isPending ? (
          <Loading />
        ) : version.error ? (
          <ErrorMessage error={version.error} />
        ) : (
          version.data && current.data && (
            <>
              <p className="fine-print">
                {format(new Date(version.data.timestamp), "PPpp")}
              </p>
              <label className="history-baseline">
                Compare against
                <select
                  value={compareCommit}
                  onChange={(event) => setCompareCommit(event.target.value)}
                >
                  <option value="current">Current note</option>
                  {q.data
                    ?.filter((entry) => entry.commit !== commit)
                    .map((entry) => (
                      <option key={entry.commit} value={entry.commit}>
                        {format(new Date(entry.timestamp), "MMM d, yyyy · HH:mm")} · {entry.message}
                      </option>
                    ))}
                </select>
              </label>
              {comparison.isPending && compareCommit !== "current" ? (
                <Loading label="Loading comparison version…" />
              ) : comparison.error ? (
                <ErrorMessage error={comparison.error} />
              ) : (
              <div className="history-compare">
                <section>
                  <h3>{compareCommit === "current" ? "Current note" : "Comparison version"}</h3>
                  <pre>{
                    compareCommit === "current"
                      ? current.data.markdown
                      : comparison.data?.markdown
                  }</pre>
                </section>
                <section>
                  <h3>Selected version</h3>
                  <pre>{version.data.markdown}</pre>
                </section>
              </div>
              )}
              <details className="rendered-history" open>
                <summary>Read the selected version</summary>
                <div className="history-preview">
                  <Markdown source={version.data.markdown} />
                </div>
              </details>
              <label className="restore-choice">
                <input
                  type="checkbox"
                  checked={asNew}
                  onChange={(event) => setAsNew(event.target.checked)}
                />
                Restore as a separate note
              </label>
              {asNew && (
                <div className="form-grid">
                  <label>
                    Folder
                    <input
                      value={folder}
                      onChange={(event) => setFolder(event.target.value)}
                      placeholder="Library root"
                    />
                  </label>
                  <label>
                    Note name
                    <input
                      required
                      value={name}
                      onChange={(event) => setName(event.target.value)}
                      placeholder={`${version.data.title} restored`}
                    />
                  </label>
                </div>
              )}
              {restore.error && <ErrorMessage error={restore.error} />}
              <div className="dialog-actions">
                <Button
                  variant="outline"
                  onClick={() => setCommit(null)}
                  disabled={restore.isPending}
                >
                  Cancel
                </Button>
                <Button
                  onClick={() => restore.mutate()}
                  disabled={restore.isPending || (asNew && !name.trim())}
                >
                  {restore.isPending
                    ? "Restoring…"
                    : asNew
                      ? "Create restored note"
                      : "Restore this version"}
                </Button>
              </div>
            </>
          )
        )}
      </Modal>
    </div>
  );
}
