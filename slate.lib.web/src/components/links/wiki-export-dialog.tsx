"use client";
import { useMutation, useQuery } from "@tanstack/react-query";
import { Check, FileOutput } from "lucide-react";
import type { Note } from "@/lib/api/contracts";
import { useApi } from "@/lib/auth/context";
import { Button } from "@/components/ui/button";
import { Empty, ErrorMessage, Loading, Modal } from "@/components/common/primitives";

export function WikiExportDialog({
  note,
  onClose,
  onApplied,
}: {
  note: Pick<Note, "id" | "title">;
  onClose: () => void;
  onApplied: (note: Note) => void;
}) {
  const api = useApi();
  const preview = useQuery({
    queryKey: ["wiki-export", note.id],
    queryFn: ({ signal }) => api.previewWikiExport(note.id, signal),
  });
  const apply = useMutation({
    mutationFn: () =>
      api.applyWikiExport(
        note.id,
        preview.data!.revision,
        preview.data!.proposedMarkdown,
      ),
    onSuccess: onApplied,
  });
  return (
    <Modal
      open
      onClose={onClose}
      title="Export wiki links"
      description={`Review the portable Markdown conversion for ${note.title}.`}
      wide
    >
      {preview.isPending ? (
        <Loading label="Preparing export preview…" />
      ) : preview.error ? (
        <ErrorMessage error={preview.error} retry={() => preview.refetch()} />
      ) : preview.data.changes.length === 0 ? (
        <Empty
          title="Already portable"
          detail="This note has no resolved wiki links that need conversion."
        >
          <Button variant="outline" onClick={onClose}>Close</Button>
        </Empty>
      ) : (
        <>
          <div className="results-meta">
            {preview.data.changes.length} link{preview.data.changes.length === 1 ? "" : "s"} will change
            <span>Original note remains untouched until apply</span>
          </div>
          <div className="history-compare">
            <section>
              <h3>Current Markdown</h3>
              <pre>{preview.data.originalMarkdown}</pre>
            </section>
            <section>
              <h3>Portable Markdown</h3>
              <pre>{preview.data.proposedMarkdown}</pre>
            </section>
          </div>
          {apply.error && <ErrorMessage error={apply.error} />}
          <div className="dialog-actions">
            <Button variant="outline" onClick={onClose} disabled={apply.isPending}>Cancel</Button>
            <Button onClick={() => apply.mutate()} disabled={apply.isPending}>
              {apply.isPending ? <FileOutput size={15} /> : <Check size={15} />}
              {apply.isPending ? "Converting…" : "Apply reviewed conversion"}
            </Button>
          </div>
        </>
      )}
    </Modal>
  );
}
