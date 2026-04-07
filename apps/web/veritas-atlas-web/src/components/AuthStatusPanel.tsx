import type { CurrentUserResponse } from "../api/auth";
import { useLogout } from "../hooks/useAuth";

export function AuthStatusPanel({
  user,
}: {
  user: CurrentUserResponse | null;
}) {
  const logout = useLogout();

  return (
    <div style={panelStyle}>
      <strong>Auth Status</strong>
      <span>{user ? `${user.username} (${user.role})` : "Not authenticated"}</span>
      {user && (
        <button onClick={logout} style={buttonStyle}>
          Logout
        </button>
      )}
    </div>
  );
}

const panelStyle: React.CSSProperties = {
  border: "1px solid #ddd",
  borderRadius: 12,
  padding: 12,
  display: "flex",
  gap: 12,
  alignItems: "center",
  flexWrap: "wrap",
};

const buttonStyle: React.CSSProperties = {
  border: "1px solid #bbb",
  borderRadius: 8,
  padding: "8px 10px",
  background: "white",
  cursor: "pointer",
  font: "inherit",
};