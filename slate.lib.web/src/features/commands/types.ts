import type { LucideIcon } from "lucide-react";

export type CommandGroup =
  | "Create"
  | "Navigation"
  | "Document"
  | "Workspace"
  | "Library"
  | "Appearance"
  | "Settings"
  | "Open tabs"
  | "Favorites"
  | "Recent notes"
  | "Saved searches"
  | "Pinned folders"
  | "Notes";

export interface CommandDefinition {
  id: string;
  title: string;
  description?: string;
  icon: LucideIcon;
  keywords: string[];
  group: CommandGroup;
  shortcut?: string;
  dangerous?: boolean;
  mobileVisible?: boolean;
  when?: () => boolean;
  execute: () => unknown | Promise<unknown>;
}

export interface CommandResult {
  key: string;
  commandId?: string;
  title: string;
  description?: string;
  path?: string;
  icon: LucideIcon;
  group: CommandGroup;
  shortcut?: string;
  dangerous?: boolean;
  score: number;
  execute: () => unknown | Promise<unknown>;
}
