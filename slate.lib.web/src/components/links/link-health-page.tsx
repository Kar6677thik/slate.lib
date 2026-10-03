"use client";
import { useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { ArrowUpRight, Check, Unlink } from "lucide-react";
import { useApi } from "@/lib/auth/context";
import { useWorkspace } from "@/features/notes/workspace-context";
import type { LinkIssue } from "@/lib/api/contracts";
import { Button } from "@/components/ui/button";
import {
  Empty,
  ErrorMessage,
  Loading,
  Modal,
} from "@/components/common/primitives";

export function LinkHealthPage() {
  const api = useApi();
  const workspace = useWorkspace();
  const [page, setPage] = useState(0);
  const [selected, setSelected] = useState<LinkIssue | null>(null);
  const query = useQuery({
    queryKey: ["link-issues", page],
    queryFn: ({ signal }) => api.linkIssues(page, signal),
  });
  return (
    <>
      <div className="view-header">
        <p className="eyebrow">LIBRARY HEALTH</p>
        <div className="section-heading">
          <h1>Link health</h1>
          <Unlink size={20} className="muted" />
        </div>
        <p>Review broken or ambiguous links. Repairs are previewed before apply.</p>
      </div>
      <div className="view-body scroll-area">
        {query.isPending ? (
          <Loading label="Checking links…" />
        ) : query.error ? (
          <ErrorMessage error={query.error} retry={() => query.refetch()} />
        ) : query.data.results.length ? (
          <>
            <div className="results-meta">
              {query.data.total.toLocaleString()} link issues
              <span>Nothing changes without review</span>
            </div>
            {query.data.results.map((issue) => (
              <div className="link-issue" key={`${issue.sourceId}:${issue.link.start}`}>
                <button onClick={() => workspace.open(issue.sourceId)}>
                  <strong>{issue.sourceTitle}</strong>
                  <span>{issue.sourcePath}</span>
                  <ArrowUpRight size={14} />
                </button>
                <div>
                  <code>{issue.link.raw}</code>
                  <span>{issue.link.state}</span>
                  {issue.link.candidates?.length ? (
                    <Button size="sm" variant="outline" onClick={() => setSelected(issue)}>
                      Review candidates
                    </Button>
                  ) : (
                    <small>No deterministic target candidate is available.</small>
                  )}
                </div>
              </div>
            ))}
            <div className="pagination">
              <Button variant="ghost" size="sm" disabled={page === 0} onClick={() => setPage((value) => value - 1)}>
                Previous
              </Button>
              <span>Page {page + 1}</span>
              <Button variant="ghost" size="sm" disabled={(page + 1) * 20 >= query.data.total} onClick={() => setPage((value) => value + 1)}>
                Next
              </Button>
            </div>
          </>
        ) : (
          <Empty
            title="Links are healthy"
            detail="No broken or ambiguous links are currently indexed."
          />
        )}
      </div>
      {selected && <RepairDialog issue={selected} onClose={() => setSelected(null)} />}
    </>
  );
}

function RepairDialog({ issue, onClose }: { issue: LinkIssue; onClose: () => void }) {
  const api = useApi();
  const cache = useQueryClient();
  const [targetId, setTargetId] = useState(issue.link.candidates?.[0] ?? "");
  const target = useQuery({
    queryKey: ["note", targetId],
    queryFn: ({ signal }) => api.note(targetId, signal),
    enabled: !!targetId,
  });
  const request = target.data
    ? {
        sourceRevision: issue.sourceRevision,
        start: issue.link.start,
        targetId: target.data.id,
        targetRevision: target.data.revision,
      }
    : null;
  const preview = useQuery({
    queryKey: ["link-repair-preview", issue.sourceId, issue.link.start, target.data?.revision],
    queryFn: () => api.previewLinkRepair(issue.sourceId, request!),
    enabled: !!request,
  });
  const apply = useMutation({
    mutationFn: () => api.applyLinkRepair(issue.sourceId, request!),
    onSuccess: () => {
      void cache.invalidateQueries({ queryKey: ["link-issues"] });
      void cache.invalidateQueries({ queryKey: ["links"] });
      void cache.invalidateQueries({ queryKey: ["note", issue.sourceId] });
      onClose();
    },
  });
  return (
    <Modal
      open
      onClose={onClose}
      title="Repair link"
      description={`${issue.sourceTitle} · ${issue.link.raw}`}
      wide
    >
      <div className="candidate-list" role="radiogroup" aria-label="Link target">
        {issue.link.candidates?.map((candidate) => (
          <label key={candidate}>
            <input
              type="radio"
              name="target"
              value={candidate}
              checked={targetId === candidate}
              onChange={() => setTargetId(candidate)}
            />
            <span>{candidate}</span>
          </label>
        ))}
      </div>
      {target.isPending || preview.isPending ? (
        <Loading label="Preparing repair preview…" />
      ) : target.error || preview.error ? (
        <ErrorMessage error={target.error ?? preview.error} />
      ) : preview.data ? (
        <div className="history-compare">
          <section>
            <h3>Current source</h3>
            <pre>{preview.data.originalMarkdown}</pre>
          </section>
          <section>
            <h3>Proposed source</h3>
            <pre>{preview.data.proposedMarkdown}</pre>
          </section>
        </div>
      ) : null}
      {apply.error && <ErrorMessage error={apply.error} />}
      <div className="dialog-actions">
        <Button variant="outline" onClick={onClose} disabled={apply.isPending}>
          Cancel
        </Button>
        <Button disabled={!preview.data || apply.isPending} onClick={() => apply.mutate()}>
          <Check size={15} />
          {apply.isPending ? "Applying…" : "Apply reviewed repair"}
        </Button>
      </div>
    </Modal>
  );
}
