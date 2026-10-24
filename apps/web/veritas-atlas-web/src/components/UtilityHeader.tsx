import { useNavigate } from "react-router-dom";
import { getAccessToken } from "../api/httpAuth";
import { useLogout } from "../hooks/useAuth";

export function UtilityHeader() {
  const navigate = useNavigate();
  const isLoggedIn = !!getAccessToken();
  const revokeSession = useLogout();

  // VER-P0-02 (#79): ending or switching a session revokes the bearer
  // server-side before it is dropped locally (useLogout always clears it).
  async function endSession() {
    try {
      await revokeSession();
    } catch {
      // The local token is already cleared; an expired or revoked bearer
      // rejected by the server needs no further action here.
    } finally {
      navigate("/login");
    }
  }

  return (
    <section className="panel" style={{ padding: 14 }}>
      <div className="panel-header" style={{ marginBottom: 0 }}>
        <div>
          <div className="panel-title" style={{ fontSize: 18 }}>Operator Utilities</div>
          <div className="hero-subtitle" style={{ marginTop: 6 }}>
            {isLoggedIn ? "Authenticated session active." : "No active session."}
          </div>
        </div>

        <div className="action-row">
          <button onClick={() => (isLoggedIn ? void endSession() : navigate("/login"))}>
            {isLoggedIn ? "Switch Account" : "Log In"}
          </button>
          <button onClick={() => void endSession()} disabled={!isLoggedIn}>Log Out</button>
        </div>
      </div>
    </section>
  );
}