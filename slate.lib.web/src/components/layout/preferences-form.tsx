"use client";
import { useState, useEffect } from "react";
import { usePreferences } from "@/lib/storage/preferences";
import { drafts } from "@/lib/storage/drafts";
export function PreferencesForm() {
  const { prefs, set } = usePreferences();
  const [count, setCount] = useState<number | null>(null);
  useEffect(() => {
    void drafts
      .count()
      .then(setCount)
      .catch(() => setCount(null));
  }, []);
  return (
    <>
      <h3 className="section-label">EDITOR & READING</h3>
      <label>
        Editor font size
        <select
          value={prefs.fontSize}
          onChange={(e) => set({ fontSize: Number(e.target.value) })}
        >
          {[12, 14, 15, 16, 18, 20, 22].map((v) => (
            <option key={v} value={v}>
              {v} px
            </option>
          ))}
        </select>
      </label>
      <label>
        Reading width
        <select
          value={prefs.width}
          onChange={(e) => set({ width: Number(e.target.value) })}
        >
          <option value={680}>Narrow</option>
          <option value={780}>Comfortable</option>
          <option value={1000}>Wide</option>
        </select>
      </label>
      <label>
        Default note mode
        <select
          value={prefs.mode}
          onChange={(e) => set({ mode: e.target.value as typeof prefs.mode })}
        >
          <option value="preview">Read</option>
          <option value="write">Write</option>
          <option value="split">Split (desktop)</option>
        </select>
      </label>
      <label className="checkbox-label">
        <input
          type="checkbox"
          checked={prefs.lineNumbers}
          onChange={(e) => set({ lineNumbers: e.target.checked })}
        />
        Line numbers
      </label>
      <label className="checkbox-label">
        <input
          type="checkbox"
          checked={prefs.wrap}
          onChange={(e) => set({ wrap: e.target.checked })}
        />
        Wrap long lines
      </label>
      <h3 className="section-label">BROWSER STORAGE</h3>
      <p className="fine-print">
        {count ?? "Unknown number of"} saved browser drafts. Browser storage can
        be cleared by your browser and is not a backup. Server requests are
        never cached by the service worker.
      </p>
    </>
  );
}
