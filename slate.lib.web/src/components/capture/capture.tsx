"use client";
import { useState, useEffect } from "react";
import { useQueryClient } from "@tanstack/react-query";
import { Zap, ArrowRight } from "lucide-react";
import { useApi } from "@/lib/auth/context";
import { useWorkspace } from "@/features/notes/workspace-context";
import { captures, type CaptureDraft } from "@/lib/storage/drafts";
import { Modal, ErrorMessage } from "@/components/common/primitives";
import { Button } from "@/components/ui/button";
export function Capture() {
  const api = useApi(),
    w = useWorkspace(),
    cache = useQueryClient();
  const [draft, setDraft] = useState<CaptureDraft | null>(null),
    [busy, setBusy] = useState(false),
    [error, setError] = useState<unknown>(null);
  const key = w.scope + ":quick-thought";
  useEffect(() => {
    if (w.captureOpen)
      void captures
        .get(key)
        .then((d) =>
          setDraft(
            d ?? {
              key,
              id: crypto.randomUUID(),
              content: "",
              title: "",
              capturedAt: new Date().toISOString(),
              submitted: false,
            },
          ),
        )
        .catch(setError);
  }, [key, w.captureOpen]);
  async function change(patch: Partial<CaptureDraft>) {
    if (!draft) return;
    const next = { ...draft, ...patch };
    setDraft(next);
    try {
      await captures.put(next);
    } catch {
      setError(
        new Error(
          "Browser draft storage is unavailable. Keep this window open.",
        ),
      );
    }
  }
  async function submit() {
    if (!draft || !draft.content.trim()) return;
    setBusy(true);
    setError(null);
    const frozen = { ...draft, submitted: true };
    try {
      await captures.put(frozen);
      setDraft(frozen);
      const note = await api.capture(
        frozen.id,
        (frozen.title ? `# ${frozen.title}\n\n` : "") + frozen.content,
        frozen.capturedAt,
      );
      await captures.remove(key);
      void cache.invalidateQueries({ queryKey: ["folder"] });
      void cache.invalidateQueries({ queryKey: ["search"] });
      w.setCaptureOpen(false);
      w.open(note.id);
      setDraft(null);
    } catch (e) {
      setError(e);
    } finally {
      setBusy(false);
    }
  }
  return (
    <Modal
      open={w.captureOpen}
      onClose={() => w.setCaptureOpen(false)}
      title="Quick Thought"
      description="Catch an idea before it slips away. Saved to your Inbox."
    >
      {draft && (
        <div className="form-stack">
          <label>
            Title <span className="muted">optional</span>
            <input
              value={draft.title}
              onChange={(e) => void change({ title: e.target.value })}
              disabled={busy || draft.submitted}
              placeholder="Give it a name, or just start writing"
            />
          </label>
          <label>
            Your thought
            <textarea
              autoFocus
              rows={7}
              value={draft.content}
              disabled={busy || draft.submitted}
              onChange={(e) => void change({ content: e.target.value })}
              placeholder="What’s on your mind?"
            />
          </label>
          {error != null && <ErrorMessage error={error} />}
          <div className="capture-foot">
            <span>
              <Zap size={13} />
              Draft kept in this browser
            </span>
            <Button onClick={submit} disabled={busy || !draft.content.trim()}>
              {busy
                ? "Saving…"
                : draft.submitted
                  ? "Retry same capture"
                  : "Save to Inbox"}
              <ArrowRight size={15} />
            </Button>
          </div>
          {draft.submitted && error != null && (
            <p className="fine-print">
              This submission is kept unchanged so retrying cannot create a
              duplicate. Retry when connected.
            </p>
          )}
        </div>
      )}
    </Modal>
  );
}
