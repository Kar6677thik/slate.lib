"use client";
import { useState } from "react";
import { useQuery, useQueryClient } from "@tanstack/react-query";
import { RefreshCw, Check, CloudOff } from "lucide-react";
import { useApi } from "@/lib/auth/context";
import { syncLabel } from "@/features/git/state";
import {
  IconButton,
  Modal,
  ErrorMessage,
} from "@/components/common/primitives";
import { Button } from "@/components/ui/button";
export function SyncControl() {
  const api = useApi(),
    cache = useQueryClient();
  const [open, setOpen] = useState(false),
    [busy, setBusy] = useState(false),
    [error, setError] = useState<unknown>(null);
  const q = useQuery({
    queryKey: ["sync-status"],
    queryFn: ({ signal }) => api.status(signal),
    refetchInterval: 30000,
    retry: false,
  });
  const label = busy
    ? "Syncing"
    : q.error
      ? "Offline"
      : syncLabel(q.data?.git?.state, q.data?.git?.pending);
  async function sync() {
    setBusy(true);
    setError(null);
    try {
      await api.sync();
      await q.refetch();
      void cache.invalidateQueries({ queryKey: ["folder"] });
      void cache.invalidateQueries({ queryKey: ["history"] });
      void cache.invalidateQueries({ queryKey: ["note"] });
      void cache.invalidateQueries({ queryKey: ["links"] });
    } catch (e) {
      setError(e);
      setOpen(true);
    } finally {
      setBusy(false);
    }
  }
  return (
    <>
      <div className="sync-control">
        <button className="sync-label" onClick={() => setOpen(true)}>
          {q.error ? (
            <CloudOff size={13} />
          ) : busy ? (
            <RefreshCw size={13} className="spin" />
          ) : (
            <Check size={13} />
          )}
          <span>{label}</span>
        </button>
        <IconButton label="Sync library" disabled={busy} onClick={sync}>
          <RefreshCw size={16} className={busy ? "spin" : ""} />
        </IconButton>
      </div>
      <Modal
        open={open}
        onClose={() => setOpen(false)}
        title="Library sync"
        description="The server saves notes and synchronizes its Git history."
      >
        <div className="form-stack">
          <p>{label}</p>
          {(error ?? q.error) != null && (
            <ErrorMessage error={error ?? q.error} />
          )}
          <p className="fine-print">
            Backend {q.data?.serverVersion ?? "unavailable"} ·{" "}
            {q.data?.noteCount ?? "—"} notes
          </p>
          <Button onClick={sync} disabled={busy}>
            {busy ? "Syncing…" : "Sync now"}
          </Button>
          <details>
            <summary className="muted">Diagnostics</summary>
            <pre className="diagnostics">
              {q.data?.git?.detail ?? "No additional details."}
            </pre>
          </details>
        </div>
      </Modal>
    </>
  );
}
