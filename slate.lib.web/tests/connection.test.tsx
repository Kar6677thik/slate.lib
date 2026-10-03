import { render, screen, fireEvent, waitFor } from "@testing-library/react";
import { it, expect, vi, afterEach } from "vitest";
import * as Tooltip from "@radix-ui/react-tooltip";
import { ConnectionForm } from "@/components/layout/connection";
const connect = vi.fn();
vi.mock("@/lib/auth/context", () => ({
  useAuth: () => ({ connection: null, connect }),
}));
afterEach(() => {
  vi.unstubAllGlobals();
  connect.mockClear();
});
function form() {
  render(
    <Tooltip.Provider>
      <ConnectionForm />
    </Tooltip.Provider>,
  );
  fireEvent.change(screen.getByLabelText("Server URL"), {
    target: { value: "https://fixture.example" },
  });
  fireEvent.change(screen.getByLabelText("Device token"), {
    target: { value: "example-token" },
  });
}
it("masks credentials, defaults to session storage and connects only after validation", async () => {
  vi.stubGlobal(
    "fetch",
    vi
      .fn()
      .mockResolvedValue(
        Response.json({
          libraryId: "10000000-0000-4000-8000-000000000001",
          noteCount: 0,
          serverVersion: "test",
          indexState: "Ready",
          git: { state: "Synced", pending: false },
        }),
      ),
  );
  form();
  expect(screen.getByLabelText("Device token")).toHaveAttribute(
    "type",
    "password",
  );
  expect(screen.getByLabelText("Remember this device")).not.toBeChecked();
  fireEvent.click(screen.getByRole("button", { name: "Reveal token" }));
  expect(screen.getByLabelText("Device token")).toHaveAttribute("type", "text");
  fireEvent.click(
    screen.getByRole("button", { name: "Test connection & open library" }),
  );
  await waitFor(() =>
    expect(connect).toHaveBeenCalledWith(
      { server: "https://fixture.example", token: "example-token" },
      false,
    ),
  );
});
it("does not store a rejected credential and explains recovery", async () => {
  vi.stubGlobal(
    "fetch",
    vi.fn().mockResolvedValue(new Response(null, { status: 401 })),
  );
  form();
  fireEvent.click(
    screen.getByRole("button", { name: "Test connection & open library" }),
  );
  await screen.findByText(/revoked/);
  expect(connect).not.toHaveBeenCalled();
});
