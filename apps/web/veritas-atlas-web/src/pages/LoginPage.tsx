import { useMemo, useState } from "react";
import { useSearchParams } from "react-router-dom";
import { login } from "../api/auth";
import { setAccessToken } from "../api/httpAuth";

export function LoginPage() {
  const [searchParams] = useSearchParams();
  const [username, setUsername] = useState("admin1");
  const [password, setPassword] = useState("password123");
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const returnTo = useMemo(() => {
    const raw = searchParams.get("returnTo");
    if (!raw) {
      return "/dashboard";
    }

    try {
      const decoded = decodeURIComponent(raw);
      return decoded.startsWith("/") ? decoded : "/dashboard";
    } catch {
      return "/dashboard";
    }
  }, [searchParams]);

  async function handleSubmit(event: React.FormEvent) {
    event.preventDefault();
    setLoading(true);
    setError(null);

    try {
      const result = await login({ username, password });
      setAccessToken(result.accessToken);
      localStorage.setItem("veritas_atlas_access_token", result.accessToken);
      window.location.assign(returnTo);
    } catch (err) {
      const message =
        typeof err === "object" && err && "message" in err
          ? String((err as { message?: unknown }).message ?? "Login failed.")
          : "Login failed.";
      setError(message);
    } finally {
      setLoading(false);
    }
  }

  return (
    <div className="login-shell">
      <section className="login-hero">
        <div className="login-eyebrow">Veritas Atlas</div>
        <h1 className="login-title">Evidence-led truth intelligence for serious operational review.</h1>
        <p className="login-copy">
          Analyze public claims, detect contradictions, validate transitions, and keep the full workflow visible in one command surface.
        </p>

        <div className="login-points">
          <div className="login-point">Live case, claim, contradiction, audit, and persistence views.</div>
          <div className="login-point">Confidence-governed workflows with protected actions and review visibility.</div>
          <div className="login-point">A premium operator-focused shell built for clarity, not clutter.</div>
        </div>
      </section>

      <section className="login-panel">
        <div className="login-card">
          <h2>Log in</h2>
          <p>Use the development credentials below to enter the command surface.</p>

          {error ? <div className="notice-card notice-danger" style={{ marginBottom: 16 }}>{error}</div> : null}

          <form className="login-form" onSubmit={handleSubmit}>
            <div>
              <label htmlFor="username">Username</label>
              <input id="username" value={username} onChange={(e) => setUsername(e.target.value)} autoComplete="username" />
            </div>

            <div>
              <label htmlFor="password">Password</label>
              <input id="password" type="password" value={password} onChange={(e) => setPassword(e.target.value)} autoComplete="current-password" />
            </div>

            <button type="submit" disabled={loading}>
              {loading ? "Signing in..." : "Sign in"}
            </button>
          </form>

          <div className="login-hint">
            Default dev credentials: <strong>admin1</strong> / <strong>password123</strong>
          </div>
        </div>
      </section>
    </div>
  );
}