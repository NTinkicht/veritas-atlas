import { useState } from "react";
import { useLogin } from "../hooks/useAuth";

export function LoginPage() {
  const loginMutation = useLogin();
  const [username, setUsername] = useState("admin1");
  const [password, setPassword] = useState("password123");
  const error = (loginMutation.error as Error | null)?.message;

  return (
    <div style={{ fontFamily: "Arial, sans-serif", padding: 24, maxWidth: 520 }}>
      <h1 style={{ marginTop: 0 }}>Login</h1>
      <p style={{ color: "#555" }}>Development authentication entry point for Veritas Atlas.</p>

      <div style={panelStyle}>
        <label style={labelStyle}>
          Username
          <input value={username} onChange={(e) => setUsername(e.target.value)} style={inputStyle} />
        </label>

        <label style={labelStyle}>
          Password
          <input type="password" value={password} onChange={(e) => setPassword(e.target.value)} style={inputStyle} />
        </label>

        <button
          onClick={() => loginMutation.mutate({ username, password })}
          disabled={loginMutation.isPending}
          style={buttonStyle}
        >
          {loginMutation.isPending ? "Signing in..." : "Login"}
        </button>

        {loginMutation.data && <p style={{ margin: 0 }}>Logged in as {loginMutation.data.username} ({loginMutation.data.role})</p>}
        {error && <p style={{ margin: 0, color: "crimson" }}>{error}</p>}
      </div>
    </div>
  );
}

const panelStyle: React.CSSProperties = {
  border: "1px solid #ddd",
  borderRadius: 14,
  padding: 16,
  display: "grid",
  gap: 12,
};

const labelStyle: React.CSSProperties = {
  display: "grid",
  gap: 6,
};

const inputStyle: React.CSSProperties = {
  border: "1px solid #ccc",
  borderRadius: 10,
  padding: "10px 12px",
  font: "inherit",
};

const buttonStyle: React.CSSProperties = {
  border: "1px solid #bbb",
  borderRadius: 10,
  padding: "10px 14px",
  background: "white",
  cursor: "pointer",
  font: "inherit",
};