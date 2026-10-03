import { render, screen, fireEvent, waitFor } from "@testing-library/react";
import { it, expect, vi } from "vitest";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import * as Tooltip from "@radix-ui/react-tooltip";
import { FolderChildren } from "@/components/library/explorer";
const list = vi.fn(async (path: string) => ({
  path,
  entries: path
    ? [
        {
          name: "Note.md",
          path: "Work/Note.md",
          id: "note",
          title: "Note",
          isDirectory: false,
        },
      ]
    : [{ name: "Work", path: "Work", isDirectory: true }],
  nextPage: null,
}));
vi.mock("@/lib/auth/context", () => ({ useApi: () => ({ list }) }));
vi.mock("@/features/notes/workspace-context", () => ({
  useWorkspace: () => ({ active: null, open: vi.fn(), setOperation: vi.fn() }),
}));
it("loads children only when a folder is expanded", async () => {
  render(
    <QueryClientProvider client={new QueryClient()}>
      <Tooltip.Provider>
        <FolderChildren />
      </Tooltip.Provider>
    </QueryClientProvider>,
  );
  const folder = await screen.findByRole("button", { name: "Work" });
  expect(list).toHaveBeenCalledTimes(1);
  fireEvent.click(folder);
  await screen.findByRole("treeitem", { name: "Note" });
  expect(list).toHaveBeenCalledTimes(2);
  fireEvent.click(folder);
  await waitFor(() =>
    expect(
      screen.queryByRole("treeitem", { name: "Note" }),
    ).not.toBeInTheDocument(),
  );
});
