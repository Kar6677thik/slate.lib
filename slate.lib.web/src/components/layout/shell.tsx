"use client";
import type { ReactNode } from "react";
import { useEffect, useState } from "react";
import dynamic from "next/dynamic";
import * as Menu from "@radix-ui/react-dropdown-menu";
import { useWorkspace } from "@/features/notes/workspace-context";
import { useTheme } from "next-themes";
import {
  Search,
  Sun,
  Settings,
  PanelLeft,
  PanelRight,
  FolderOpen,
  Inbox,
  Clock3,
  Plus,
  Zap,
  MoreHorizontal,
  MessageCircleQuestion,
  BrainCircuit,
  History,
  ShieldQuestion,
  Layers3,
  Link2,
  Atom,
  HeartPulse,
  CircleHelp,
} from "lucide-react";
import {
  Group,
  Panel,
  Separator,
  useDefaultLayout,
} from "react-resizable-panels";
import { IconButton, Modal } from "@/components/common/primitives";
import { ConnectionForm } from "./connection";
import { PreferencesForm } from "./preferences-form";
import { useAuth } from "@/lib/auth/context";
import { useMedia } from "@/hooks/use-media";
import { usePreferences } from "@/lib/storage/preferences";
import { Button } from "@/components/ui/button";
import type { SmartViewId } from "@/features/views/smart-views";
import { useCommandRuntime } from "@/features/commands/runtime";
const IntelligenceSettings = dynamic(() =>
  import("@/components/search/intelligence-settings").then(
    (module) => module.IntelligenceSettings,
  ),
);
import { AskSlate, openAskSlate } from "@/components/ask/ask-slate";
import { Tabs } from "@/components/editor/document";
export type Destination =
  | "library"
  | "search"
  | "inbox"
  | "recent"
  | "favorites"
  | "recovery"
  | "rediscover"
  | "link-health"
  | "project-brain"
  | "evolution"
  | "knowledge-issues"
  | "knowledge-overlap"
  | "link-opportunities"
  | "concepts"
  | "library-health"
  | "knowledge-gaps"
  | SmartViewId;
export const intelligenceDestinations: Destination[] = [
  "project-brain",
  "evolution",
  "knowledge-issues",
  "knowledge-overlap",
  "link-opportunities",
  "concepts",
  "library-health",
  "knowledge-gaps",
];
const coreDestinations = [
  ["library", FolderOpen, "Library"],
  ["inbox", Inbox, "Inbox"],
  ["recent", Clock3, "Recent"],
] as const;
const aiDestinations = [
  ["project-brain", BrainCircuit, "Project Brain"],
  ["evolution", History, "Evolution"],
  ["knowledge-issues", ShieldQuestion, "Knowledge Issues"],
  ["knowledge-overlap", Layers3, "Knowledge Overlap"],
  ["link-opportunities", Link2, "Link Opportunities"],
  ["concepts", Atom, "Concepts"],
  ["library-health", HeartPulse, "Library Health"],
  ["knowledge-gaps", CircleHelp, "Knowledge Gaps"],
] as const;

function serverLabel(server?: string) {
  if (!server) return "Slate";
  try {
    return new URL(server).host;
  } catch {
    return server;
  }
}

export function Shell({
  children,
  sidebar,
  rail,
  nav,
  onNav,
  onSearch,
  onNew,
  onCapture,
  sync,
  settingsExtra,
}: {
  children: ReactNode;
  sidebar: ReactNode;
  rail?: ReactNode;
  nav: Destination;
  onNav: (nav: Destination) => void;
  onSearch: () => void;
  onNew: () => void;
  onCapture: () => void;
  sync?: ReactNode;
  settingsExtra?: ReactNode;
}) {
  const [left, setLeft] = useState(true),
    [right, setRight] = useState(false),
    [mobileLeft, setMobileLeft] = useState(false),
    [mobileRight, setMobileRight] = useState(false);
  const w = useWorkspace();
  const { prefs, set } = usePreferences();
  const { theme, setTheme } = useTheme();
  const auth = useAuth();
  const desktop = useMedia("(min-width: 768px)"),
    wide = useMedia("(min-width: 1100px)");
  const layout = useDefaultLayout({ id: "slate.workspace.panes" });
  const { register } = useCommandRuntime();
  useEffect(() => {
    setMobileLeft(false);
    setMobileRight(false);
  }, [w.active, nav]);
  useEffect(
    () =>
      register("workspace-shell", {
        "workspace.toggle-library": () =>
          desktop ? setLeft((v) => !v) : setMobileLeft((v) => !v),
        "workspace.focus-library": () => {
          if (desktop) setLeft(true);
          else setMobileLeft(true);
          requestAnimationFrame(() =>
            document
              .querySelector<HTMLButtonElement>(
                ".sidebar .tree-label, .modal .tree-label",
              )
              ?.focus(),
          );
        },
        "workspace.toggle-details": () =>
          wide ? setRight((v) => !v) : setMobileRight((v) => !v),
        "workspace.show-details": () => {
          if (wide) setRight(true);
          else setMobileRight(true);
        },
      }),
    [desktop, register, wide],
  );

  const aiMenu = prefs.aiEnabled && (
    <Menu.Root>
      <Menu.Trigger asChild>
        <Button
          variant="ghost"
          size="icon"
          aria-label="AI tools"
          title="AI tools"
          className={`activity-button ${intelligenceDestinations.includes(nav) && !w.active ? "active" : ""}`}
        >
          <BrainCircuit size={18} />
        </Button>
      </Menu.Trigger>
      <Menu.Portal>
        <Menu.Content
          className="item-menu workspace-menu"
          side={desktop ? "right" : "bottom"}
          align="start"
          sideOffset={8}
          collisionPadding={10}
        >
          <Menu.Label className="menu-label">AI tools</Menu.Label>
          <Menu.Item
            onSelect={() =>
              openAskSlate({ scope: w.active ? "note" : "library" })
            }
          >
            <MessageCircleQuestion size={16} />
            Ask Slate
          </Menu.Item>
          <Menu.Separator />
          {aiDestinations.map(([key, Icon, label]) => (
            <Menu.Item key={key} onSelect={() => onNav(key)}>
              <Icon size={16} />
              {label}
            </Menu.Item>
          ))}
        </Menu.Content>
      </Menu.Portal>
    </Menu.Root>
  );
  const moreMenu = (
    <Menu.Root>
      <Menu.Trigger asChild>
        <Button
          variant="ghost"
          size="icon"
          aria-label="Workspace menu"
          title="Workspace menu"
        >
          <MoreHorizontal size={19} />
        </Button>
      </Menu.Trigger>
      <Menu.Portal>
        <Menu.Content
          className="item-menu workspace-menu"
          align="end"
          sideOffset={6}
          collisionPadding={10}
        >
          <Menu.Label className="menu-label">Workspace</Menu.Label>
          <Menu.Item onSelect={() => w.openCommandCenter("> ")}>
            <MoreHorizontal size={16} />
            All commands<kbd>Ctrl Shift P</kbd>
          </Menu.Item>
          <Menu.Item onSelect={onCapture}>
            <Zap size={16} />
            Quick Thought
          </Menu.Item>
          {!desktop && (
            <Menu.Item onSelect={() => setMobileRight(true)}>
              <PanelRight size={16} />
              Document details
            </Menu.Item>
          )}
          <Menu.Separator />
          <Menu.Item
            onSelect={() => setTheme(theme === "dark" ? "light" : "dark")}
          >
            <Sun size={16} />
            Change theme
          </Menu.Item>
          <Menu.Item onSelect={() => w.setSettingsOpen(true)}>
            <Settings size={16} />
            Settings
          </Menu.Item>
        </Menu.Content>
      </Menu.Portal>
    </Menu.Root>
  );
  return (
    <div className="app-shell">
      <header
        className="workspace-topbar"
        aria-label="Workspace tabs and actions"
      >
        <IconButton
          label="Toggle library"
          aria-expanded={desktop ? left : mobileLeft}
          onClick={() =>
            desktop ? setLeft((v) => !v) : setMobileLeft((v) => !v)
          }
        >
          <PanelLeft size={17} />
        </IconButton>
        <Tabs />
        <IconButton label="New note" onClick={onNew}>
          <Plus size={18} />
        </IconButton>
        <div className="workspace-actions">
          <button
            className="search-trigger"
            aria-label="Search your library"
            title="Search notes and commands (Ctrl K)"
            onClick={onSearch}
          >
            <Search size={15} />
            <span className="search-trigger-label">Search</span>
            <kbd>Ctrl K</kbd>
          </button>
          <button
            className="ai-switch"
            role="switch"
            aria-label="AI features"
            aria-checked={prefs.aiEnabled}
            onClick={() => set({ aiEnabled: !prefs.aiEnabled })}
            title="Show or hide AI tools in this browser"
          >
            <span>AI</span>
            <span className="switch-track" aria-hidden="true">
              <span />
            </span>
          </button>
          {w.active && (
            <IconButton
              label="Toggle details"
              aria-expanded={wide ? right : mobileRight}
              className={(wide ? right : mobileRight) ? "active" : ""}
              onClick={() =>
                wide ? setRight((v) => !v) : setMobileRight((v) => !v)
              }
            >
              <PanelRight size={17} />
            </IconButton>
          )}
          {moreMenu}
        </div>
      </header>
      <div className="shell-body">
        {desktop && (
          <nav className="activity-rail" aria-label="Workspace destinations">
            <div className="activity-main">
              {coreDestinations.map(([key, Icon, label]) => (
                <IconButton
                  key={key}
                  label={label}
                  className={`activity-button ${nav === key && !w.active ? "active" : ""}`}
                  onClick={() => onNav(key)}
                >
                  <Icon size={18} />
                </IconButton>
              ))}
              {aiMenu}
            </div>
            <div className="activity-bottom">
              <IconButton
                label="Quick Thought"
                className="activity-button"
                onClick={onCapture}
              >
                <Zap size={18} />
              </IconButton>
              <IconButton
                label="Settings"
                className="activity-button"
                onClick={() => w.setSettingsOpen(true)}
              >
                <Settings size={18} />
              </IconButton>
            </div>
          </nav>
        )}
        <Group
          orientation="horizontal"
          defaultLayout={layout.defaultLayout}
          onLayoutChanged={layout.onLayoutChanged}
        >
          {desktop && left && (
            <>
              <Panel
                id="navigation"
                defaultSize="21%"
                minSize="200px"
                maxSize="35%"
              >
                <aside className="sidebar" aria-label="Library navigation">
                  {sidebar}
                </aside>
              </Panel>
              <Separator className="pane-separator" />
            </>
          )}
          <Panel id="document" minSize="35%">
            <main className="workspace-main">{children}</main>
          </Panel>
          {wide && right && rail && (
            <>
              <Separator className="pane-separator" />
              <Panel
                id="details"
                defaultSize="23%"
                minSize="240px"
                maxSize="35%"
              >
                <aside className="right-rail" aria-label="Note details">
                  {rail}
                </aside>
              </Panel>
            </>
          )}
        </Group>
      </div>
      <footer className="workspace-statusbar">
        <span className="statusbar-connection" title={auth.connection?.server}>
          <span className="server-dot" aria-hidden="true" />
          {serverLabel(auth.connection?.server)}
        </span>
        {sync}
      </footer>
      {!desktop && (
        <nav className="mobile-nav" aria-label="Main navigation">
          {coreDestinations.map(([key, Icon, label]) => (
            <button
              key={key}
              className={`nav-item ${nav === key && !w.active ? "active" : ""}`}
              onClick={() => onNav(key)}
            >
              <Icon size={19} />
              <span>{label}</span>
            </button>
          ))}
          {prefs.aiEnabled && <div className="mobile-ai-entry">{aiMenu}</div>}
          <IconButton label="Quick Thought" onClick={onCapture}>
            <Plus size={21} />
          </IconButton>
        </nav>
      )}
      <Modal
        open={mobileLeft}
        onClose={() => setMobileLeft(false)}
        title="Library"
      >
        {sidebar}
      </Modal>
      <Modal
        open={mobileRight}
        onClose={() => setMobileRight(false)}
        title="Document details"
      >
        {rail}
      </Modal>
      <Modal
        open={w.settingsOpen}
        onClose={() => w.setSettingsOpen(false)}
        title="Settings"
        description="Connection, appearance, and editor preferences."
      >
        <div className="form-stack">
          <h3 className="section-label">CONNECTION</h3>
          <ConnectionForm onDone={() => w.setSettingsOpen(false)} />
          <h3 className="section-label">APPEARANCE</h3>
          <label>
            Theme
            <select
              value={theme ?? "light"}
              onChange={(e) => setTheme(e.target.value)}
            >
              <option value="light">Light</option>
              <option value="dark">Dark</option>
              <option value="system">System</option>
            </select>
          </label>
          <label className="checkbox-label">
            <input
              type="checkbox"
              checked={prefs.aiEnabled}
              onChange={(e) => set({ aiEnabled: e.target.checked })}
            />
            AI features
          </label>
          <p className="fine-print">
            Show AI tools and suggestions in this browser. Turning them off uses
            keyword search and stops automatic analysis requests from this
            client. Server indexing settings stay separate.
          </p>
          <PreferencesForm />
          {prefs.aiEnabled && <IntelligenceSettings />}
          {settingsExtra}
          <Button
            variant="outline"
            onClick={() => {
              auth.disconnect();
              w.setSettingsOpen(false);
            }}
          >
            Disconnect this browser
          </Button>
          <p className="fine-print">slate.lib.web · 0.1.0</p>
        </div>
      </Modal>
      {prefs.aiEnabled && <AskSlate />}
    </div>
  );
}
