"use client";
import { useState } from "react";
import { ArrowRight, Eye, EyeOff, ShieldCheck } from "lucide-react";
import { useAuth } from "@/lib/auth/context";
import { SlateApi, normalizeServer } from "@/lib/api/client";
import { Button } from "@/components/ui/button";
import { ErrorMessage, IconButton } from "@/components/common/primitives";
export function ConnectionForm({ onDone }: { onDone?: () => void }) {
  const auth = useAuth();
  const [server, setServer] = useState(
    auth.connection?.server ??
      process.env.NEXT_PUBLIC_SLATE_DEFAULT_SERVER ??
      "",
  );
  const [token, setToken] = useState(auth.connection?.token ?? "");
  const [remember, setRemember] = useState(false);
  const [visible, setVisible] = useState(false);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<unknown>(null);
  return (
    <form
      onSubmit={async (e) => {
        e.preventDefault();
        setBusy(true);
        setError(null);
        try {
          const config = {
            server: normalizeServer(server),
            token: token.trim(),
          };
          await new SlateApi(config).status();
          auth.connect(config, remember);
          onDone?.();
        } catch (e) {
          setError(e);
        } finally {
          setBusy(false);
        }
      }}
      className="form-stack"
    >
      <label>
        Server URL
        <input
          required
          type="url"
          value={server}
          onChange={(e) => setServer(e.target.value)}
          placeholder="https://megatron.your-tailnet.ts.net"
          autoComplete="url"
        />
      </label>
      <label>
        Device token
        <div className="password-field">
          <input
            required
            type={visible ? "text" : "password"}
            value={token}
            onChange={(e) => setToken(e.target.value)}
            autoComplete="off"
            spellCheck={false}
          />
          <IconButton
            label={visible ? "Hide token" : "Reveal token"}
            type="button"
            onClick={() => setVisible(!visible)}
          >
            {visible ? <EyeOff size={17} /> : <Eye size={17} />}
          </IconButton>
        </div>
      </label>
      <label className="checkbox-label">
        <input
          type="checkbox"
          checked={remember}
          onChange={(e) => setRemember(e.target.checked)}
        />
        Remember this device
      </label>
      <p className="fine-print">
        <ShieldCheck size={16} />
        By default, your token stays in this browser session. Remembering it
        stores it on this browser profile, accessible to scripts on this site.
        Use a private, trusted device.
      </p>
      {error != null && <ErrorMessage error={error} />}
      <Button disabled={busy}>
        {busy ? "Testing connection…" : "Test connection & open library"}
        <ArrowRight size={17} />
      </Button>
    </form>
  );
}
export function ConnectionScreen() {
  return (
    <main className="connection-page">
      <div className="connection-brand">
        <img src="/icons/slate-192.png" width="28" height="28" alt="" />
        <strong>
          slate<span>.lib</span>
        </strong>
        <span className="eyebrow">PRIVATE KNOWLEDGE LIBRARY</span>
      </div>
      <section className="connection-card">
        <div className="connection-icon">
          <img src="/icons/slate-192.png" width="36" height="36" alt="" />
        </div>
        <p className="eyebrow">CONNECT TO SLATE</p>
        <h1>Your library, in the browser.</h1>
        <p className="connection-intro">
          Enter your server address and device token to open your library.
        </p>
        <ConnectionForm />
      </section>
      <footer>Your Slate server stores your notes.</footer>
    </main>
  );
}
