"use client";
import {
  createContext,
  useContext,
  useEffect,
  useState,
  type ReactNode,
} from "react";
type Preferences = {
  fontSize: number;
  lineNumbers: boolean;
  wrap: boolean;
  mode: "write" | "preview" | "split";
  width: number;
};
const defaults: Preferences = {
  fontSize: 15,
  lineNumbers: true,
  wrap: true,
  mode: "preview",
  width: 780,
};
const Context = createContext<{
  prefs: Preferences;
  set: (patch: Partial<Preferences>) => void;
}>({ prefs: defaults, set: () => {} });
export function PreferencesProvider({ children }: { children: ReactNode }) {
  const [prefs, setPrefs] = useState(defaults);
  useEffect(() => {
    try {
      const p = JSON.parse(localStorage.getItem("slate.preferences") ?? "{}");
      setPrefs({
        fontSize: Math.min(22, Math.max(12, Number(p.fontSize) || 15)),
        lineNumbers: p.lineNumbers !== false,
        wrap: p.wrap !== false,
        mode: ["write", "split"].includes(p.mode) ? p.mode : "preview",
        width: Math.min(1100, Math.max(600, Number(p.width) || 780)),
      });
    } catch {}
  }, []);
  return (
    <Context.Provider
      value={{
        prefs,
        set(patch) {
          setPrefs((p) => {
            const next = { ...p, ...patch };
            try {
              localStorage.setItem("slate.preferences", JSON.stringify(next));
            } catch {}
            return next;
          });
        },
      }}
    >
      <div
        style={
          {
            display: "contents",
            "--reader-width": prefs.width + "px",
          } as React.CSSProperties
        }
      >
        {children}
      </div>
    </Context.Provider>
  );
}
export const usePreferences = () => useContext(Context);
