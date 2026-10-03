"use client";
import { useState } from "react";
import { useQuery, useQueryClient } from "@tanstack/react-query";
import { useApi } from "@/lib/auth/context";
import {
  useWorkspace,
  type Operation,
} from "@/features/notes/workspace-context";
import { Modal, ErrorMessage, Loading } from "@/components/common/primitives";
import { Button } from "@/components/ui/button";
import { FolderPicker } from "./folder-picker";
export function OperationDialog() {
  const w = useWorkspace();
  return w.operation ? (
    <OperationForm key={JSON.stringify(w.operation)} operation={w.operation} />
  ) : null;
}
function OperationForm({ operation: o }: { operation: Operation }) {
  const api = useApi(),
    w = useWorkspace(),
    cache = useQueryClient();
  const [name, setName] = useState(
      o.kind === "rename" ? (o.entry?.name ?? "") : "",
    ),
    [folder, setFolder] = useState(o.folder ?? ""),
    [busy, setBusy] = useState(false),
    [error, setError] = useState<unknown>(null);
  const deleting = o.kind === "delete";
  const transfer = o.kind === "move" || o.kind === "copy";
  const details = useQuery({
    queryKey: ["item", o.entry?.path],
    queryFn: () => api.details(o.entry!.path),
    enabled: deleting,
  });
  const titles: Record<Operation["kind"], string> = {
    "create-note": "New note",
    "create-folder": "New folder",
    rename: "Rename",
    move: "Move to folder",
    copy: "Copy to folder",
    duplicate: "Duplicate",
    delete: "Delete from library",
  };
  async function submit() {
    setBusy(true);
    setError(null);
    try {
      if (
        o.entry &&
        w.tabs.some(
          (t) =>
            t.dirty &&
            (t.path === o.entry!.path ||
              t.path.startsWith(o.entry!.path + "/")),
        )
      )
        throw new Error(
          "Save the open edits in this item before changing its location or deleting it.",
        );
      if (o.kind === "create-note") {
        const n = await api.create(folder, name);
        w.open(n.id);
      } else if (o.kind === "create-folder") await api.folder(folder, name);
      else if (o.kind === "rename") await api.rename(o.entry!.path, name);
      else if (transfer)
        await api.transfer(o.kind as "move" | "copy", o.entry!.path, folder);
      else if (o.kind === "duplicate")
        await api.transfer(
          "copy",
          o.entry!.path,
          o.entry!.path.includes("/")
            ? o.entry!.path.slice(0, o.entry!.path.lastIndexOf("/"))
            : "",
        );
      else if (deleting) {
        await api.delete(o.entry!.path, details.data?.isDirectory ?? false);
        w.closePath(o.entry!.path);
      }
      await cache.invalidateQueries({ queryKey: ["folder"] });
      await cache.invalidateQueries({ queryKey: ["note"] });
      void cache.invalidateQueries({ queryKey: ["search"] });
      void cache.invalidateQueries({ queryKey: ["links"] });
      w.setOperation(null);
    } catch (e) {
      setError(e);
    } finally {
      setBusy(false);
    }
  }
  return (
    <Modal
      open
      onClose={() => {
        if (!busy) w.setOperation(null);
      }}
      title={titles[o.kind]}
      description={o.entry?.path ?? `In ${folder || "Library root"}`}
    >
      <form
        className="form-stack"
        onSubmit={(e) => {
          e.preventDefault();
          void submit();
        }}
      >
        {(o.kind === "create-note" ||
          o.kind === "create-folder" ||
          o.kind === "rename") && (
          <label>
            Name
            <input
              autoFocus
              required
              value={name}
              disabled={busy}
              onChange={(e) => setName(e.target.value)}
              placeholder={
                o.kind === "create-folder" ? "Folder name" : "Note name"
              }
              pattern={"[^/\\\\]+"}
            />
          </label>
        )}
        {(transfer || o.kind.startsWith("create")) && (
          <div>
            <label htmlFor="destination-folder">Destination folder</label>
            <input
              id="destination-folder"
              aria-label="Destination folder"
              placeholder="Library root"
              value={folder}
              onChange={(e) => setFolder(e.target.value)}
              disabled={busy}
            />
            <span className="fine-print">
              Browse below or enter an exact folder path.
            </span>
            <FolderPicker onSelect={setFolder} />
          </div>
        )}
        {o.kind === "duplicate" && (
          <p>
            A copy with a new note identity and an available name will be
            created in the same folder.
          </p>
        )}
        {deleting &&
          (details.isPending ? (
            <Loading label="Checking contents…" />
          ) : details.error ? (
            <ErrorMessage error={details.error} />
          ) : (
            <p className="delete-scope">
              Delete <strong>{o.entry!.name}</strong>
              {details.data.isDirectory
                ? ` and ${details.data.descendantCount} contained items`
                : ""}
              ? This removes the item from the library. No undo is offered here.
            </p>
          ))}
        {error != null && <ErrorMessage error={error} />}
        <div className="dialog-actions">
          <Button
            type="button"
            variant="outline"
            onClick={() => w.setOperation(null)}
            disabled={busy}
          >
            Cancel
          </Button>
          <Button
            type="submit"
            variant={deleting ? "destructive" : "default"}
            disabled={busy || (deleting && !details.data)}
          >
            {busy
              ? "Working…"
              : deleting
                ? "Delete"
                : o.kind === "duplicate"
                  ? "Create duplicate"
                  : transfer
                    ? "Confirm destination"
                    : "Save"}
          </Button>
        </div>
      </form>
    </Modal>
  );
}
