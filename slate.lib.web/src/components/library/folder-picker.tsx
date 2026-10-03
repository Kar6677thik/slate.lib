"use client";
import { useState } from "react";
import { useQueryClient, type InfiniteData } from "@tanstack/react-query";
import type { FolderPage } from "@/lib/api/contracts";
import { Button } from "@/components/ui/button";
import { FolderChildren } from "./explorer";

export function FolderPicker({
  onSelect,
}: {
  onSelect: (path: string) => void;
}) {
  const cache = useQueryClient();
  const [filter, setFilter] = useState("");
  // Search only folders already browsed. A destination path can always be entered
  // directly; searching must never recursively download the whole library.
  const paths = [
    ...new Set(
      cache
        .getQueriesData<InfiniteData<FolderPage>>({ queryKey: ["folder"] })
        .flatMap(
          ([, data]) =>
            data?.pages.flatMap((page) =>
              page.entries
                .filter((entry) => entry.isDirectory)
                .map((entry) => entry.path),
            ) ?? [],
        ),
    ),
  ]
    .filter((path) =>
      path.toLocaleLowerCase().includes(filter.toLocaleLowerCase()),
    )
    .sort();
  return (
    <div className="folder-picker">
      <input
        aria-label="Filter browsed folders"
        placeholder="Filter browsed folders…"
        value={filter}
        onChange={(e) => setFilter(e.target.value)}
      />
      <Button
        type="button"
        variant="ghost"
        size="sm"
        onClick={() => onSelect("")}
      >
        Library root
      </Button>
      <div hidden={!!filter} role="tree" aria-label="Destination folders">
        <FolderChildren foldersOnly onFolder={onSelect} />
      </div>
      {filter && (
        <div className="folder-matches">
          {paths.map((path) => (
            <Button
              type="button"
              variant="ghost"
              key={path}
              onClick={() => onSelect(path)}
            >
              {path}
            </Button>
          ))}
          {!paths.length && (
            <p className="fine-print">
              No browsed folders match. Clear the filter to browse, or enter the
              full path above.
            </p>
          )}
        </div>
      )}
    </div>
  );
}
