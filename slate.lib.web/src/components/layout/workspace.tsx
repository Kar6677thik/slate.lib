"use client";
import { useAuth } from "@/lib/auth/context";
import { ConnectionScreen } from "./connection";
import { Loading } from "@/components/common/primitives";
import { SearchPage, QuickOpen } from "@/components/search/search";
import { SyncControl } from "./sync-control";
import { DetailsRail } from "@/components/links/details-rail";
import { OperationDialog } from "@/components/library/operation-dialog";
import { Capture } from "@/components/capture/capture";
import { DestinationPage } from "@/components/library/destinations";
import { useShortcuts } from "@/hooks/use-shortcuts";
import { Shell } from "./shell";
import { useQuery } from "@tanstack/react-query";
import {
  WorkspaceProvider,
  useWorkspace,
} from "@/features/notes/workspace-context";
import { Explorer } from "@/components/library/explorer";
import { Document, Tabs } from "@/components/editor/document";
import { ErrorMessage } from "@/components/common/primitives";
export function Workspace() {
  const { ready, api } = useAuth();
  if (!ready) return <Loading />;
  if (!api) return <ConnectionScreen />;
  return <Connected />;
}
function Connected() {
  const { api, connection } = useAuth();
  const q = useQuery({
    queryKey: ["status", connection?.server],
    queryFn: ({ signal }) => api!.status(signal),
  });
  if (q.isPending) return <Loading label="Connecting to your library…" />;
  if (q.error)
    return (
      <>
        <ErrorMessage error={q.error} retry={() => q.refetch()} />
        <ConnectionScreen />
      </>
    );
  return (
    <WorkspaceProvider
      key={connection!.server + q.data.libraryId}
      scope={connection!.server + ":" + q.data.libraryId}
    >
      <LibraryWorkspace />
    </WorkspaceProvider>
  );
}
function LibraryWorkspace() {
  const w = useWorkspace();
  useShortcuts();
  return (
    <Shell
      sync={<SyncControl />}
      rail={<DetailsRail />}
      sidebar={<Explorer />}
      nav={w.nav}
      onNav={w.navigate}
      onSearch={() => w.setSearchOpen(true)}
      onNew={() => w.setOperation({ kind: "create-note", folder: "" })}
      onCapture={() => w.setCaptureOpen(true)}
    >
      <Tabs />
      {w.active ? (
        <Document key={w.active} id={w.active} />
      ) : w.nav === "search" ? (
        <SearchPage />
      ) : (
        <DestinationPage key={w.nav} />
      )}
      <QuickOpen />
      <Capture />
      <OperationDialog />
    </Shell>
  );
}
