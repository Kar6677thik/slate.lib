"use client";
import { useAuth } from "@/lib/auth/context";
import { ConnectionScreen } from "./connection";
import { Loading } from "@/components/common/primitives";
import { SearchPage } from "@/components/search/search";
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
import { CommandRuntimeProvider } from "@/features/commands/runtime";
import { CommandCenter } from "@/features/commands/command-center";
import { ProjectBrain } from "@/components/project/project-brain";
import { EvolutionWorkspace } from "@/components/evolution/evolution-workspace";
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
      <CommandRuntimeProvider>
        <LibraryWorkspace />
      </CommandRuntimeProvider>
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
      onSearch={() => w.openCommandCenter()}
      onNew={() => w.setOperation({ kind: "create-note", folder: "" })}
      onCapture={() => w.setCaptureOpen(true)}
    >
      <Tabs />
      {w.active ? (
        <Document key={w.active} id={w.active} />
      ) : w.nav === "search" ? (
        <SearchPage />
      ) : w.nav === "project-brain" && w.projectPath ? (
        <ProjectBrain path={w.projectPath} initialSection={w.projectSection} />
      ) : w.nav === "evolution" ? (
        <EvolutionWorkspace initialScope={w.evolutionScope} />
      ) : (
        <DestinationPage key={w.nav} />
      )}
      <CommandCenter />
      <Capture />
      <OperationDialog />
    </Shell>
  );
}
