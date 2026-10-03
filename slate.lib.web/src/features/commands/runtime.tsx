"use client";

import {
  createContext,
  useCallback,
  useContext,
  useMemo,
  useRef,
  useState,
  type ReactNode,
} from "react";

export type CommandHandler = () => void | Promise<void>;

type Runtime = {
  version: number;
  has: (id: string) => boolean;
  run: (id: string) => Promise<boolean>;
  register: (owner: string, actions: Record<string, CommandHandler>) => () => void;
};

const Context = createContext<Runtime | null>(null);

export function CommandRuntimeProvider({ children }: { children: ReactNode }) {
  const owners = useRef(new Map<string, Record<string, CommandHandler>>());
  const signatures = useRef(new Map<string, string>());
  const [version, setVersion] = useState(0);

  const register = useCallback(
    (owner: string, actions: Record<string, CommandHandler>) => {
      owners.current.set(owner, actions);
      const signature = Object.keys(actions).sort().join("|");
      if (signatures.current.get(owner) !== signature) {
        signatures.current.set(owner, signature);
        setVersion((value) => value + 1);
      }
      return () => {
        queueMicrotask(() => {
          if (owners.current.get(owner) !== actions) return;
          owners.current.delete(owner);
          signatures.current.delete(owner);
          setVersion((value) => value + 1);
        });
      };
    },
    [],
  );

  const value = useMemo<Runtime>(
    () => ({
      version,
      register,
      has(id) {
        return [...owners.current.values()].some((actions) => id in actions);
      },
      async run(id) {
        for (const actions of owners.current.values()) {
          const action = actions[id];
          if (action) {
            await action();
            return true;
          }
        }
        return false;
      },
    }),
    [register, version],
  );

  return <Context.Provider value={value}>{children}</Context.Provider>;
}

export function useCommandRuntime() {
  const value = useContext(Context);
  if (!value) throw new Error("Command runtime missing");
  return value;
}
