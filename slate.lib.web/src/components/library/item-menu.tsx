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
} from "lucide-react";
import {
  useWorkspace,
  type Operation,
} from "@/features/notes/workspace-context";
import type { Entry } from "@/lib/api/contracts";
import { IconButton } from "@/components/common/primitives";
export function ItemMenu({ entry }: { entry: Entry }) {
  const w = useWorkspace();
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
