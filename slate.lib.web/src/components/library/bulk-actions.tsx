"use client";
import { useState } from "react";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { Copy, Files, FolderInput, Trash2 } from "lucide-react";
import { useApi } from "@/lib/auth/context";
import type { BulkPreview } from "@/lib/api/contracts";
import { Button } from "@/components/ui/button";
import { ErrorMessage, Modal } from "@/components/common/primitives";

type Action = "move" | "copy" | "duplicate" | "delete";

export function BulkActions({
  paths,
  onDone,
  onCancel,
}: {
  paths: string[];
  onDone: () => void;
  onCancel: () => void;
}) {
  const api = useApi();
  const cache = useQueryClient();
  const [action, setAction] = useState<Action | null>(null);
  const [destination, setDestination] = useState("");
  const [repairIncoming, setRepairIncoming] = useState(true);
  const preview = useMutation({
    mutationFn: () =>
      api.previewBulk({
        operationId: crypto.randomUUID(),
        operation: action!,
        paths,
        ...(action === "move" || action === "copy"
          ? { destinationFolderPath: destination.trim() }
          : {}),
      }),
  });
  const apply = useMutation({
    mutationFn: (plan: BulkPreview) =>
      api.applyBulk({
        operationId: plan.operationId,
        fingerprint: plan.fingerprint,
        repairIncoming,
      }),
    onSuccess: () => {
      void cache.invalidateQueries({ queryKey: ["folder"] });
      void cache.invalidateQueries({ queryKey: ["note"] });
      void cache.invalidateQueries({ queryKey: ["links"] });
      void cache.invalidateQueries({ queryKey: ["smart-view"] });
      void cache.invalidateQueries({ queryKey: ["search"] });
      onDone();
    },
  });
  const open = (next: Action) => {
    preview.reset();
    apply.reset();
    setDestination("");
    setRepairIncoming(true);
    setAction(next);
  };
  return (
    <>
      <div className="bulk-bar" aria-label="Bulk actions">
        <strong>{paths.length} selected</strong>
        <Button size="sm" variant="ghost" disabled={!paths.length} onClick={() => open("move")}>
          <FolderInput size={15} /> Move
        </Button>
        <Button size="sm" variant="ghost" disabled={!paths.length} onClick={() => open("copy")}>
          <Copy size={15} /> Copy
        </Button>
        <Button size="sm" variant="ghost" disabled={!paths.length} onClick={() => open("duplicate")}>
          <Files size={15} /> Duplicate
        </Button>
        <Button size="sm" variant="ghost" disabled={!paths.length} onClick={() => open("delete")}>
          <Trash2 size={15} /> Delete
        </Button>
        <Button size="sm" variant="outline" onClick={onCancel}>Cancel</Button>
      </div>
      <Modal
        open={!!action}
        onClose={() => setAction(null)}
        title={action ? `${action[0].toUpperCase()}${action.slice(1)} selected items` : "Bulk action"}
        description="Slate prepares a server-validated plan before any library content changes."
        wide
      >
        {!preview.data ? (
          <div className="form-stack">
            <p>{paths.length} selected path{paths.length === 1 ? "" : "s"}.</p>
            {(action === "move" || action === "copy") && (
              <label>
                Destination folder
                <input
                  autoFocus
                  value={destination}
                  onChange={(event) => setDestination(event.target.value)}
                  placeholder="Library root"
                />
              </label>
            )}
            {preview.error && <ErrorMessage error={preview.error} />}
            <div className="dialog-actions">
              <Button variant="outline" onClick={() => setAction(null)}>Cancel</Button>
              <Button
                variant={action === "delete" ? "destructive" : "default"}
                disabled={preview.isPending}
                onClick={() => preview.mutate()}
              >
                {preview.isPending ? "Preparing…" : "Review plan"}
              </Button>
            </div>
          </div>
        ) : (
          <div className="bulk-preview">
            <div className="results-meta">
              {preview.data.noteCount} note{preview.data.noteCount === 1 ? "" : "s"}
              <span>{preview.data.items.length} top-level item{preview.data.items.length === 1 ? "" : "s"}</span>
            </div>
            <div className="bulk-plan-list">
              {preview.data.items.map((item) => (
                <div key={item.sourcePath}>
                  <strong>{item.sourcePath}</strong>
                  <span>{item.destinationPath ?? "Will be deleted"}</span>
                </div>
              ))}
            </div>
            {!!preview.data.repairs?.length && (
              <>
                <label className="check-row">
                  <input
                    type="checkbox"
                    checked={repairIncoming}
                    onChange={(event) => setRepairIncoming(event.target.checked)}
                  />
                  Repair {preview.data.repairs.length} incoming linked note{preview.data.repairs.length === 1 ? "" : "s"}
                </label>
                <details className="repair-preview">
                  <summary>Review link rewrites</summary>
                  {preview.data.repairs.map((repair) => (
                    <div key={repair.id}>
                      <strong>{repair.path}</strong>
                      <pre>{repair.proposedMarkdown}</pre>
                    </div>
                  ))}
                </details>
              </>
            )}
            {apply.error && <ErrorMessage error={apply.error} />}
            <div className="dialog-actions">
              <Button variant="outline" onClick={() => preview.reset()} disabled={apply.isPending}>Back</Button>
              <Button
                variant={action === "delete" ? "destructive" : "default"}
                disabled={apply.isPending}
                onClick={() => apply.mutate(preview.data)}
              >
                {apply.isPending ? "Applying…" : "Apply reviewed plan"}
              </Button>
            </div>
          </div>
        )}
      </Modal>
    </>
  );
}
