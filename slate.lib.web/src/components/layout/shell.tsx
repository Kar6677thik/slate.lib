"use client";
import type { ReactNode } from "react";
import { useEffect, useState } from "react";
import { useWorkspace } from "@/features/notes/workspace-context";
import { useTheme } from "next-themes";
import {
  Search,
  Sun,
  Moon,
  Settings,
  PanelLeft,
  PanelRight,
  ArrowLeft,
  ArrowRight,
  FolderOpen,
  Inbox,
  Clock3,
  Plus,
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
import { Button } from "@/components/ui/button";
export type Destination = "library" | "search" | "inbox" | "recent";
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
    [right, setRight] = useState(true),
    [mobileLeft, setMobileLeft] = useState(false),
    [mobileRight, setMobileRight] = useState(false);
  const {
    active,
    settingsOpen: settings,
    setSettingsOpen: setSettings,
  } = useWorkspace();
  useEffect(() => {
    setMobileLeft(false);
    setMobileRight(false);
  }, [active]);
  const { theme, setTheme } = useTheme();
  const auth = useAuth();
  const desktop = useMedia("(min-width: 768px)"),
    wide = useMedia("(min-width: 1100px)");
  const layout = useDefaultLayout({ id: "slate.workspace.panes" });
  const navigation = (
    [
      ["library", FolderOpen, "Library"],
      ["search", Search, "Search"],
      ["inbox", Inbox, "Inbox"],
      ["recent", Clock3, "Recent"],
    ] as const
  ).map(([key, Icon, label]) => (
    <button
      key={key}
      className={`nav-item ${nav === key ? "active" : ""}`}
      onClick={() => {
        onNav(key);
        setMobileLeft(false);
      }}
    >
      <Icon size={18} />
      <span>{label}</span>
    </button>
  ));
  return (
    <div className="app-shell">
      <header className="topbar">
        <div className="brand">
          <img src="/icons/slate-192.png" width="26" height="26" alt="" />
          <strong>
            slate<span>.lib</span>
          </strong>
        </div>
        <div className="history-nav">
          <IconButton label="Back" onClick={() => history.back()}>
            <ArrowLeft size={17} />
          </IconButton>
          <IconButton label="Forward" onClick={() => history.forward()}>
            <ArrowRight size={17} />
          </IconButton>
        </div>
        <button
          className="global-search"
          onClick={onSearch}
          aria-label="Search your library"
        >
          <Search size={17} />
          <span>Search your library</span>
          <kbd>Ctrl K</kbd>
        </button>
        <div className="topbar-end">
          {sync}
          <IconButton
            label="Change theme"
            onClick={() => setTheme(theme === "dark" ? "light" : "dark")}
          >
            {theme === "dark" ? <Moon size={18} /> : <Sun size={18} />}
          </IconButton>
          <IconButton label="Settings" onClick={() => setSettings(true)}>
            <Settings size={18} />
          </IconButton>
        </div>
      </header>
      <div className="shell-body">
        <Group
          orientation="horizontal"
          defaultLayout={layout.defaultLayout}
          onLayoutChanged={layout.onLayoutChanged}
        >
          {desktop && left && (
            <>
              <Panel
                id="navigation"
                defaultSize="19%"
                minSize="180px"
                maxSize="32%"
              >
                <aside className="sidebar" aria-label="Library navigation">
                  <div className="sidebar-heading">
                    <span className="eyebrow">WORKSPACE</span>
                    <IconButton
                      label="Collapse navigation"
                      onClick={() => setLeft(false)}
                    >
                      <PanelLeft size={16} />
                    </IconButton>
                  </div>
                  <nav className="primary-nav">{navigation}</nav>
                  <Button
                    className="capture-button"
                    variant="outline"
                    onClick={onCapture}
                  >
                    <Plus size={16} />
                    Quick Thought
                  </Button>
                  {sidebar}
                  <footer className="sidebar-footer">
                    <span className="server-dot" />
                    <span>Private workspace</span>
                    <IconButton label="New note" onClick={onNew}>
                      <Plus size={16} />
                    </IconButton>
                  </footer>
                </aside>
              </Panel>
              <Separator className="pane-separator" />
            </>
          )}
          <Panel id="document" minSize="35%">
            <main className="workspace-main">
              <div className="pane-toggles">
                <IconButton
                  label="Toggle library"
                  onClick={() => {
                    if (desktop) setLeft(!left);
                    else setMobileLeft(true);
                  }}
                >
                  <PanelLeft size={17} />
                </IconButton>
                <span>Knowledge library</span>
                <IconButton
                  label="Toggle details"
                  onClick={() => {
                    if (wide) setRight(!right);
                    else setMobileRight(true);
                  }}
                >
                  <PanelRight size={17} />
                </IconButton>
              </div>
              {children}
            </main>
          </Panel>
          {wide && right && rail && (
            <>
              <Separator className="pane-separator" />
              <Panel
                id="details"
                defaultSize="21%"
                minSize="220px"
                maxSize="32%"
              >
                <aside className="right-rail" aria-label="Note details">
                  {rail}
                </aside>
              </Panel>
            </>
          )}
        </Group>
      </div>
      <nav className="mobile-nav" aria-label="Main navigation">
        {navigation}
        <IconButton label="Quick Thought" onClick={onCapture}>
          <Plus size={21} />
        </IconButton>
      </nav>
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
        open={settings}
        onClose={() => setSettings(false)}
        title="Settings"
        description="Connection, appearance, and editor preferences."
      >
        <div className="form-stack">
          <h3 className="section-label">CONNECTION</h3>
          <ConnectionForm onDone={() => setSettings(false)} />
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
          <PreferencesForm />
          {settingsExtra}
          <Button
            variant="outline"
            onClick={() => {
              auth.disconnect();
              setSettings(false);
            }}
          >
            Disconnect this browser
          </Button>
          <p className="fine-print">slate.lib.web · 0.1.0</p>
        </div>
      </Modal>
    </div>
  );
}
