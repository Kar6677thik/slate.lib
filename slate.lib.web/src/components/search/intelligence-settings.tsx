"use client";
import { useState } from "react";
import { useMutation, useQuery } from "@tanstack/react-query";
import { useApi } from "@/lib/auth/context";
import { Button } from "@/components/ui/button";

export function IntelligenceSettings() {
  const api = useApi();
  const [confirming, setConfirming] = useState<"knowledge" | "overlap" | "links" | "concepts" | null>(null);
  const status = useQuery({ queryKey: ["intelligence-status"], queryFn: ({ signal }) => api.intelligenceStatus(signal), retry: false });
  const rebuild = useMutation({ mutationFn: () => api.intelligenceRebuild(), onSuccess: () => { setConfirming(null); void status.refetch(); } });
  const data = status.data;
  return (
    <section className="intelligence-settings" aria-labelledby="intelligence-title">
      <div><h3 id="intelligence-title" className="section-label">MEANING SEARCH</h3><span className={`index-state ${data?.state ?? "unavailable"}`}>{data?.state ? data.state[0].toUpperCase() + data.state.slice(1) : "Unavailable"}</span></div>
      <p>{data?.enabled ? `${data.provider} · ${data.model}` : "Disabled by the server operator. Keyword search remains available."}</p>
      {data?.enabled && <dl><div><dt>Notes</dt><dd>{data.noteCount.toLocaleString()} / {data.totalNotes.toLocaleString()}</dd></div><div><dt>Chunks</dt><dd>{data.chunkCount.toLocaleString()}</dd></div><div><dt>Pending</dt><dd>{data.pendingJobs.toLocaleString()}</dd></div><div><dt>Failed</dt><dd>{data.failedJobs.toLocaleString()}</dd></div></dl>}
      {data?.enabled && <p className="index-run-stats">This run: {data.embeddedThisRun.toLocaleString()} embedded · {data.reusedThisRun.toLocaleString()} reused · {data.failedChunksThisRun.toLocaleString()} failed</p>}
      {data?.enabled && data.noteCount === 0 && data.totalNotes > 0 && <small>The existing library is indexed only after you confirm a rebuild.</small>}
      {data?.lastIndexedAt && <small>Last indexed {new Date(data.lastIndexedAt).toLocaleString()}</small>}
      {data?.enabled && (!confirming ? <div className="rebuild-actions"><Button type="button" variant="outline" size="sm" aria-label="Rebuild derived index" onClick={() => setConfirming("knowledge")}>Rebuild Knowledge Analysis</Button><Button type="button" variant="outline" size="sm" onClick={() => setConfirming("overlap")}>Rebuild Overlap Analysis</Button><Button type="button" variant="outline" size="sm" onClick={() => setConfirming("links")}>Rebuild Link Opportunities</Button><Button type="button" variant="outline" size="sm" onClick={() => setConfirming("concepts")}>Rebuild Concept Index</Button></div> : <div className="rebuild-confirm"><span>{confirming === "overlap" ? "Rebuild derived note signatures, overlap candidates, and consolidation signals?" : confirming === "links" ? "Rebuild derived mentions, graph gaps, and project relationship suggestions?" : confirming === "concepts" ? "Rebuild derived concept identities, memberships, relationships, and statistics?" : "Rebuild derived search, claims, contradiction candidates, and stale signals?"} Canonical notes will not be changed.</span><Button type="button" variant="outline" size="sm" onClick={() => setConfirming(null)}>Cancel</Button><Button type="button" size="sm" disabled={rebuild.isPending} onClick={() => rebuild.mutate()}>{rebuild.isPending ? "Queuing…" : "Confirm rebuild"}</Button></div>)}
      <div className="ask-settings">
        <div><h3 className="section-label">ASK SLATE</h3><span className={`index-state ${data?.askEnabled ? "ready" : "unavailable"}`}>{data?.askEnabled ? "Enabled" : "Disabled"}</span></div>
        <p>{data?.askEnabled ? `${data.generationProvider} · ${data.generationModel}` : "Ask Slate isn’t configured on this server."}</p>
        {data?.askEnabled && <><small>Strict library is the default. Relevant excerpts may be sent to the configured provider only when you explicitly ask a question.</small><dl><div><dt>Requests</dt><dd>{data.askRequestsThisRun.toLocaleString()}</dd></div><div><dt>Retrieved</dt><dd>{data.askRetrievedChunksThisRun.toLocaleString()}</dd></div><div><dt>Active</dt><dd>{data.askActiveRequests} / {data.askMaxConcurrent}</dd></div></dl>{(data.askInputTokensThisRun !== undefined || data.askOutputTokensThisRun !== undefined) && <p className="index-run-stats">Provider-reported tokens: {data.askInputTokensThisRun?.toLocaleString() ?? "—"} in · {data.askOutputTokensThisRun?.toLocaleString() ?? "—"} out</p>}</>}
        <a href="https://github.com/kar6677thik/slate.lib/blob/main/slate.lib.web/docs/PRIVACY.md" target="_blank" rel="noopener noreferrer">Read Ask Slate privacy details</a>
      </div>
    </section>
  );
}
