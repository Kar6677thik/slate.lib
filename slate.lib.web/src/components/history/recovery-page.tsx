"use client";
import { useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { format } from "date-fns";
import { ArchiveRestore, FileText } from "lucide-react";
import { useApi } from "@/lib/auth/context";
import { useWorkspace } from "@/features/notes/workspace-context";
import type { RecoverableNote } from "@/lib/api/contracts";
import {
  Empty,
  ErrorMessage,
  Loading,
  Modal,
} from "@/components/common/primitives";
import { Button } from "@/components/ui/button";
import dynamic from "next/dynamic";

const Markdown = dynamic(() => import("@/components/reader/markdown"), {
  ssr: false,
});

export function RecoveryPage() {
  const api = useApi();
  const query = useQuery({
    queryKey: ["recoverable"],
    queryFn: ({ signal }) => api.recoverable(signal),
  });
  const [selected, setSelected] = useState<RecoverableNote | null>(null);
  return (
    <>
      <div className="view-header">
        <p className="eyebrow">GIT RECOVERY</p>
        <div className="section-heading">
          <h1>Deleted notes</h1>
          <ArchiveRestore size={20} className="muted" />
        </div>
        <p>Recover bounded note history without rewriting Git history.</p>
      </div>
      <div className="view-body scroll-area">
        {query.isPending ? (
          <Loading label="Checking recoverable history…" />
        ) : query.error ? (
          <ErrorMessage error={query.error} retry={() => query.refetch()} />
        ) : query.data.notes.length ? (
          <>
            {query.data.bounded && (
              <p className="fine-print">
                This is a bounded scan of recent reachable Git history.
              </p>
            )}
            {query.data.notes.map((note) => (
              <button
                className="result-row"
                key={note.id}
                onClick={() => setSelected(note)}
              >
                <FileText size={18} />
                <div>
                  <strong>{note.title}</strong>
                  <span className="result-path">{note.path}</span>
                </div>
                <small>{format(new Date(note.deletedAt), "PP")}</small>
              </button>
            ))}
          </>
        ) : (
          <Empty
            title="No recoverable notes"
            detail="Recently deleted notes found in reachable Git history will appear here."
          />
        )}
      </div>
      {selected && (
        <RecoverDialog note={selected} onClose={() => setSelected(null)} />
      )}
    </>
  );
}

function RecoverDialog({
  note,
  onClose,
}: {
  note: RecoverableNote;
  onClose: () => void;
}) {
  const api = useApi();
  const workspace = useWorkspace();
  const cache = useQueryClient();
  const [folder, setFolder] = useState(
    note.path.includes("/") ? note.path.slice(0, note.path.lastIndexOf("/")) : "",
  );
  const [name, setName] = useState(
    note.path.split("/").at(-1)?.replace(/\.md$/i, "") ?? note.title,
  );
  const preview = useQuery({
    queryKey: ["historical", note.id, note.sourceCommit],
    queryFn: () => api.historical(note.id, note.sourceCommit),
  });
  const restore = useMutation({
    mutationFn: () =>
      api.restore(note.id, {
        commit: note.sourceCommit,
        mode: "recover",
        folder: folder.trim(),
        name: name.trim(),
      }),
    onSuccess: (restored) => {
      void cache.invalidateQueries({ queryKey: ["recoverable"] });
      void cache.invalidateQueries({ queryKey: ["folder"] });
      void cache.invalidateQueries({ queryKey: ["search"] });
      workspace.open(restored.id);
      onClose();
    },
  });
  return (
    <Modal
      open
      onClose={onClose}
      title={`Recover ${note.title}`}
      description="Review the historical source and choose a collision-free destination."
      wide
    >
      {preview.isPending ? (
        <Loading label="Loading deleted note…" />
      ) : preview.error ? (
        <ErrorMessage error={preview.error} />
      ) : (
        <div className="history-preview">
          <Markdown source={preview.data.markdown} />
        </div>
      )}
      <div className="form-grid">
        <label>
          Folder
          <input value={folder} onChange={(event) => setFolder(event.target.value)} />
        </label>
        <label>
          Note name
          <input required value={name} onChange={(event) => setName(event.target.value)} />
        </label>
      </div>
      {restore.error && <ErrorMessage error={restore.error} />}
      <div className="dialog-actions">
        <Button variant="outline" onClick={onClose} disabled={restore.isPending}>
          Cancel
        </Button>
        <Button
          onClick={() => restore.mutate()}
          disabled={restore.isPending || !name.trim() || preview.isPending}
        >
          {restore.isPending ? "Recovering…" : "Recover note"}
        </Button>
      </div>
    </Modal>
  );
}
