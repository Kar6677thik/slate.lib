"use client";
import { useState } from "react";
import * as Menu from "@radix-ui/react-dropdown-menu";
import {
  MoreHorizontal,
  Pencil,
  FolderInput,
  Copy,
  Files,
  Trash2,
  Plus,
  FolderPlus,
  Star,
  Pin,
  BrainCircuit,
  FolderCog,
} from "lucide-react";
import {
  useWorkspace,
  type Operation,
} from "@/features/notes/workspace-context";
import type { Entry } from "@/lib/api/contracts";
import { IconButton, Modal } from "@/components/common/primitives";
import { useWorkspacePreferences } from "@/lib/storage/workspace-preferences";
import { useMedia } from "@/hooks/use-media";
import { Button } from "@/components/ui/button";
export function ItemMenu({ entry }: { entry: Entry }) {
  const w = useWorkspace();
  const local = useWorkspacePreferences(w.scope);
  const mobile = useMedia("(max-width: 767px)");
  const [mobileOpen, setMobileOpen] = useState(false);
  const favorite = !entry.isDirectory && !!entry.id && local.preferences.favorites.some((item) => item.id === entry.id);
  const pinned = entry.isDirectory && local.preferences.pins.some((item) => item.path.toLowerCase() === entry.path.toLowerCase());
  const project = entry.isDirectory && local.preferences.projects.some((item) => item.path.toLowerCase() === entry.path.toLowerCase());
  const options: {
    kind: Operation["kind"];
    label: string;
    icon: typeof Pencil;
  }[] = [
    { kind: "rename", label: "Rename", icon: Pencil },
    { kind: "move", label: "Move to…", icon: FolderInput },
    { kind: "copy", label: "Copy to…", icon: Copy },
    { kind: "duplicate", label: "Duplicate", icon: Files },
    { kind: "delete", label: "Delete", icon: Trash2 },
  ];
  const choose = (action: () => void) => {
    setMobileOpen(false);
    action();
  };
  const actions = <>
    {entry.isDirectory ? <>
      <Button role="menuitem" variant="ghost" onClick={() => choose(() => w.openProjectBrain(entry.path))}><BrainCircuit size={18} />Open Project Brain</Button>
      <Button role="menuitem" variant="ghost" onClick={() => choose(() => local.toggleProject({ path: entry.path, label: entry.name }))}><FolderCog size={18} />{project ? "Remove project root" : "Mark as project root"}</Button>
      <Button role="menuitem" variant="ghost" onClick={() => choose(() => local.togglePin({ path: entry.path, label: entry.name }))}><Pin size={18} />{pinned ? "Unpin folder" : "Pin folder"}</Button>
      <span className="mobile-action-separator" role="separator" />
      <Button role="menuitem" variant="ghost" onClick={() => choose(() => w.setOperation({ kind: "create-note", folder: entry.path }))}><Plus size={18} />New note here</Button>
      <Button role="menuitem" variant="ghost" onClick={() => choose(() => w.setOperation({ kind: "create-folder", folder: entry.path }))}><FolderPlus size={18} />New folder here</Button>
      <span className="mobile-action-separator" role="separator" />
    </> : entry.id ? <Button role="menuitem" variant="ghost" onClick={() => choose(() => local.toggleFavorite({ id: entry.id!, title: entry.title ?? entry.name.replace(/\.md$/i, ""), path: entry.path }))}><Star size={18} fill={favorite ? "currentColor" : "none"} />{favorite ? "Remove favorite" : "Add to favorites"}</Button> : null}
    {options.map((option) => <Button role="menuitem" variant="ghost" className={option.kind === "delete" ? "destructive" : ""} key={option.kind} onClick={() => choose(() => w.setOperation({ kind: option.kind, entry }))}><option.icon size={18} />{option.label}</Button>)}
  </>;
  if (mobile) return <>
    <IconButton label={`Actions for ${entry.name}`} aria-haspopup="dialog" aria-expanded={mobileOpen} onClick={() => setMobileOpen(true)}><MoreHorizontal size={16} /></IconButton>
    <Modal title={entry.name} description="Choose an action" open={mobileOpen} onClose={() => setMobileOpen(false)}>
      <div className="mobile-item-actions" role="menu" aria-label={`Actions for ${entry.name}`}>{actions}</div>
    </Modal>
  </>;
  return (
    <Menu.Root>
      <Menu.Trigger asChild>
        <IconButton label={`Actions for ${entry.name}`}>
          <MoreHorizontal size={16} />
        </IconButton>
      </Menu.Trigger>
      <Menu.Portal>
        <Menu.Content className="item-menu" sideOffset={5} align="end" collisionPadding={12}>
          {entry.isDirectory ? (
            <>
              <Menu.Item onSelect={() => w.openProjectBrain(entry.path)}>
                <BrainCircuit size={16} />
                Open Project Brain
              </Menu.Item>
              <Menu.Item onSelect={() => local.toggleProject({ path: entry.path, label: entry.name })}>
                <FolderCog size={16} />
                {project ? "Remove project root" : "Mark as project root"}
              </Menu.Item>
              <Menu.Item onSelect={() => local.togglePin({ path: entry.path, label: entry.name })}>
                <Pin size={16} />
                {pinned ? "Unpin folder" : "Pin folder"}
              </Menu.Item>
            </>
          ) : entry.id ? (
            <Menu.Item
              onSelect={() =>
                local.toggleFavorite({
                  id: entry.id!,
                  title: entry.title ?? entry.name.replace(/\.md$/i, ""),
                  path: entry.path,
                })
              }
            >
              <Star size={16} fill={favorite ? "currentColor" : "none"} />
              {favorite ? "Remove favorite" : "Add to favorites"}
            </Menu.Item>
          ) : null}
          <Menu.Separator />
          {entry.isDirectory && (
            <>
              <Menu.Item
                onSelect={() =>
                  w.setOperation({ kind: "create-note", folder: entry.path })
                }
              >
                <Plus size={16} />
                New note here
              </Menu.Item>
              <Menu.Item
                onSelect={() =>
                  w.setOperation({ kind: "create-folder", folder: entry.path })
                }
              >
                <FolderPlus size={16} />
                New folder here
              </Menu.Item>
              <Menu.Separator />
            </>
          )}
          {options.map((o) => (
            <Menu.Item
              className={o.kind === "delete" ? "destructive" : ""}
              key={o.kind}
              onSelect={() => w.setOperation({ kind: o.kind, entry })}
            >
              <o.icon size={16} />
              {o.label}
            </Menu.Item>
          ))}
        </Menu.Content>
      </Menu.Portal>
    </Menu.Root>
  );
}
