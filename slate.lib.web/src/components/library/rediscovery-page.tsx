"use client";
import { useEffect, useState } from "react";
import { useQuery } from "@tanstack/react-query";
import { format } from "date-fns";
import { ArrowUpRight, Compass, FileText } from "lucide-react";
import { useApi } from "@/lib/auth/context";
import { useWorkspace } from "@/features/notes/workspace-context";
import { Button } from "@/components/ui/button";
import { Empty, ErrorMessage, Loading } from "@/components/common/primitives";

const views = [
  ["forgotten", "Something forgotten"],
  ["old-idea", "Old ideas"],
  ["old-question", "Old questions"],
  ["on-this-day", "On this day"],
  ["recently-learned", "Recently learned"],
  ["continue-learning", "Continue learning"],
  ["timeline", "Timeline"],
  ["random", "Random note"],
] as const;

export function RediscoveryPage() {
  const api = useApi();
  const workspace = useWorkspace();
  const [view, setView] = useState<(typeof views)[number][0]>("forgotten");
  const [page, setPage] = useState(0);
  useEffect(() => setPage(0), [view]);
  const today = format(new Date(), "yyyy-MM-dd");
  const query = useQuery({
    queryKey: ["rediscovery", view, today, page],
    queryFn: ({ signal }) => api.rediscover(view, today, page, signal),
  });
  const title = views.find(([id]) => id === view)?.[1] ?? "Rediscover";
  return (
    <>
      <div className="view-header">
        <p className="eyebrow">REDISCOVERY</p>
        <div className="section-heading">
          <h1>Rediscover</h1>
          <Compass size={20} className="muted" />
        </div>
        <p>Return to useful material with an explicit reason for every result.</p>
        <div className="view-switcher" aria-label="Rediscovery view">
          {views.map(([id, label]) => (
            <button
              key={id}
              className={view === id ? "active" : ""}
              onClick={() => setView(id)}
            >
              {label}
            </button>
          ))}
        </div>
      </div>
      <div className="view-body scroll-area">
        {query.isPending ? (
          <Loading label={`Finding ${title.toLowerCase()}…`} />
        ) : query.error ? (
          <ErrorMessage error={query.error} retry={() => query.refetch()} />
        ) : query.data.results.length ? (
          <>
            <div className="results-meta">
              {query.data.total.toLocaleString()} eligible notes
              <span>Why you are seeing this</span>
            </div>
            {query.data.results.map((result) => (
              <button
                className="result-row rediscovery-row"
                key={result.id}
                onClick={() => workspace.open(result.id)}
              >
                <FileText size={18} />
                <div>
                  <strong>{result.title}</strong>
                  <span className="result-path">{result.path}</span>
                  <p>{result.reason}</p>
                </div>
                <ArrowUpRight size={15} />
              </button>
            ))}
            <div className="pagination">
              <Button
                variant="ghost"
                size="sm"
                disabled={page === 0}
                onClick={() => setPage((value) => value - 1)}
              >
                Previous
              </Button>
              <span>Page {page + 1}</span>
              <Button
                variant="ghost"
                size="sm"
                disabled={(page + 1) * 20 >= query.data.total}
                onClick={() => setPage((value) => value + 1)}
              >
                Next
              </Button>
            </div>
          </>
        ) : (
          <Empty
            title={`No ${title.toLowerCase()}`}
            detail="This view has no eligible notes with the metadata currently available."
          />
        )}
      </div>
    </>
  );
}
