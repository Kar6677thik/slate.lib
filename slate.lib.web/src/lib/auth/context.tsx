"use client";
import {
  createContext,
  useContext,
  useState,
  useEffect,
  useMemo,
  type ReactNode,
} from "react";
import { useQueryClient } from "@tanstack/react-query";
import { SlateApi, normalizeServer } from "@/lib/api/client";
import type { Connection } from "@/lib/api/contracts";
type Session = {
  connection: Connection | null;
  ready: boolean;
  api: SlateApi | null;
  connect: (value: Connection, remember: boolean) => void;
  disconnect: () => void;
};
const Context = createContext<Session | null>(null);
export function AuthProvider({ children }: { children: ReactNode }) {
  const [connection, setConnection] = useState<Connection | null>(null);
  const [ready, setReady] = useState(false);
  const cache = useQueryClient();
  useEffect(() => {
    try {
      const saved =
        sessionStorage.getItem("slate.connection") ??
        localStorage.getItem("slate.connection");
      if (saved) {
        const value = JSON.parse(saved);
        if (typeof value.token === "string" && value.token)
          setConnection({
            server: normalizeServer(value.server),
            token: value.token,
          });
      }
    } catch {}
    setReady(true);
  }, []);
  const api = useMemo(
    () => (connection ? new SlateApi(connection) : null),
    [connection],
  );
  return (
    <Context.Provider
      value={{
        connection,
        ready,
        api,
        connect(value, remember) {
          cache.clear();
          const c = {
            server: normalizeServer(value.server),
            token: value.token.trim(),
          };
          localStorage.removeItem("slate.connection");
          sessionStorage.removeItem("slate.connection");
          (remember ? localStorage : sessionStorage).setItem(
            "slate.connection",
            JSON.stringify(c),
          );
          setConnection(c);
        },
        disconnect() {
          cache.clear();
          sessionStorage.removeItem("slate.connection");
          localStorage.removeItem("slate.connection");
          setConnection(null);
        },
      }}
    >
      {children}
    </Context.Provider>
  );
}
export function useAuth() {
  const value = useContext(Context);
  if (!value) throw new Error("AuthProvider missing");
  return value;
}
export function useApi() {
  const { api } = useAuth();
  if (!api) throw new Error("Connect first");
  return api;
}
