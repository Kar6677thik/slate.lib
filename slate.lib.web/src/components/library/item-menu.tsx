"use client";
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
} from "lucide-react";
import {
  useWorkspace,
  type Operation,
} from "@/features/notes/workspace-context";
import type { Entry } from "@/lib/api/contracts";
import { IconButton } from "@/components/common/primitives";
import { useWorkspacePreferences } from "@/lib/storage/workspace-preferences";
export function ItemMenu({ entry }: { entry: Entry }) {
  const w = useWorkspace();
  const local = useWorkspacePreferences(w.scope);
  const favorite = !entry.isDirectory && !!entry.id && local.preferences.favorites.some((item) => item.id === entry.id);
  const pinned = entry.isDirectory && local.preferences.pins.some((item) => item.path.toLowerCase() === entry.path.toLowerCase());
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
  return (
    <Menu.Root>
      <Menu.Trigger asChild>
        <IconButton label={`Actions for ${entry.name}`}>
          <MoreHorizontal size={16} />
        </IconButton>
      </Menu.Trigger>
      <Menu.Portal>
        <Menu.Content className="item-menu" sideOffset={5} align="end">
          {entry.isDirectory ? (
            <Menu.Item
              onSelect={() =>
                local.togglePin({ path: entry.path, label: entry.name })
              }
            >
              <Pin size={16} />
              {pinned ? "Unpin folder" : "Pin folder"}
            </Menu.Item>
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
