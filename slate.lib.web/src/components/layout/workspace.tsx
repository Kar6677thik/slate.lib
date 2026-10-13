"use client";
import { useAuth } from "@/lib/auth/context";
import { ConnectionScreen } from "./connection";
import { Loading } from "@/components/common/primitives";
import { useEffect } from "react";
import dynamic from "next/dynamic";
import { usePreferences } from "@/lib/storage/preferences";
import { SyncControl } from "./sync-control";
const DetailsRail = dynamic(
  () =>
    import("@/components/links/details-rail").then(
      (module) => module.DetailsRail,
    ),
  { loading: () => <Loading /> },
);
const OperationDialog = dynamic(
  () =>
    import("@/components/library/operation-dialog").then(
      (module) => module.OperationDialog,
    ),
  { loading: () => <Loading /> },
);
const Capture = dynamic(
  () => import("@/components/capture/capture").then((module) => module.Capture),
  { loading: () => <Loading /> },
);
import { DestinationPage } from "@/components/library/destinations";
import { useShortcuts } from "@/hooks/use-shortcuts";
import { Shell, intelligenceDestinations } from "./shell";
import { useQuery } from "@tanstack/react-query";
import {
  WorkspaceProvider,
  useWorkspace,
} from "@/features/notes/workspace-context";
import { Explorer } from "@/components/library/explorer";
import { Document } from "@/components/editor/document";
import { ErrorMessage } from "@/components/common/primitives";
import { CommandRuntimeProvider } from "@/features/commands/runtime";
const CommandCenter = dynamic(
  () =>
    import("@/features/commands/command-center").then(
      (module) => module.CommandCenter,
    ),
  { loading: () => <Loading /> },
);
const ProjectBrain = dynamic(
  () =>
    import("@/components/project/project-brain").then(
      (module) => module.ProjectBrain,
    ),
  { loading: () => <Loading /> },
);
const EvolutionWorkspace = dynamic(
  () =>
    import("@/components/evolution/evolution-workspace").then(
      (module) => module.EvolutionWorkspace,
    ),
  { loading: () => <Loading /> },
);
const KnowledgeIssuesWorkspace = dynamic(
  () =>
    import("@/components/knowledge/knowledge-issues-workspace").then(
      (module) => module.KnowledgeIssuesWorkspace,
    ),
  { loading: () => <Loading /> },
);
const OverlapWorkspace = dynamic(
  () =>
    import("@/components/overlap/overlap-workspace").then(
      (module) => module.OverlapWorkspace,
    ),
  { loading: () => <Loading /> },
);
const LinkOpportunitiesWorkspace = dynamic(
  () =>
    import("@/components/links/link-opportunities").then(
      (module) => module.LinkOpportunitiesWorkspace,
    ),
  { loading: () => <Loading /> },
);
const ConceptWorkspace = dynamic(
  () =>
    import("@/components/concepts/concept-workspace").then(
      (module) => module.ConceptWorkspace,
    ),
  { loading: () => <Loading /> },
);
const LibraryHealthWorkspace = dynamic(
  () =>
    import("@/components/health/library-health").then(
      (module) => module.LibraryHealthWorkspace,
    ),
  { loading: () => <Loading /> },
);
const KnowledgeGapsWorkspace = dynamic(
  () =>
    import("@/components/gaps/knowledge-gaps").then(
      (module) => module.KnowledgeGapsWorkspace,
    ),
  { loading: () => <Loading /> },
);
import { FolderPicker } from "@/components/library/folder-picker";
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
  const { prefs } = usePreferences();
  useEffect(() => {
    if (
      !prefs.aiEnabled &&
      !w.active &&
      intelligenceDestinations.includes(w.nav)
    )
      w.navigate("library");
    if (w.nav === "search") {
      const query = w.query;
      w.navigate("library");
      w.openCommandCenter(query);
    }
  }, [prefs.aiEnabled, w]);
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
      {w.active ? (
        <Document key={w.active} id={w.active} />
      ) : prefs.aiEnabled && w.nav === "project-brain" && w.projectPath ? (
        <ProjectBrain path={w.projectPath} initialSection={w.projectSection} />
      ) : prefs.aiEnabled && w.nav === "project-brain" ? (
        <div className="view-body scroll-area">
          <h1>Project Brain</h1>
          <p className="muted">
            Choose a project folder to review its notes, questions, and history.
          </p>
          <FolderPicker onSelect={(path) => path && w.openProjectBrain(path)} />
        </div>
      ) : prefs.aiEnabled && w.nav === "evolution" ? (
        <EvolutionWorkspace initialScope={w.evolutionScope} />
      ) : prefs.aiEnabled && w.nav === "knowledge-issues" ? (
        <KnowledgeIssuesWorkspace scope={w.knowledgeScope} />
      ) : prefs.aiEnabled && w.nav === "knowledge-overlap" ? (
        <OverlapWorkspace scope={w.overlapScope} />
      ) : prefs.aiEnabled && w.nav === "link-opportunities" ? (
        <LinkOpportunitiesWorkspace scope={w.linkScope} />
      ) : prefs.aiEnabled && w.nav === "concepts" ? (
        <ConceptWorkspace identity={w.conceptIdentity} />
      ) : prefs.aiEnabled && w.nav === "library-health" ? (
        <LibraryHealthWorkspace />
      ) : prefs.aiEnabled && w.nav === "knowledge-gaps" ? (
        <KnowledgeGapsWorkspace />
      ) : (
        <DestinationPage key={w.nav} />
      )}
      {w.searchOpen && <CommandCenter />}
      {w.captureOpen && <Capture />}
      {w.operation && <OperationDialog />}
    </Shell>
  );
}
