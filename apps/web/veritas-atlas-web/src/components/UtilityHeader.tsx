import { useNavigate } from "react-router-dom";
import { clearAccessToken, getAccessToken } from "../api/httpAuth";

export function UtilityHeader() {
  const navigate = useNavigate();
  const isLoggedIn = !!getAccessToken();

  function logout() {
    clearAccessToken();
    navigate("/login");
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
          <button onClick={() => navigate("/login")}>{isLoggedIn ? "Switch Account" : "Log In"}</button>
          <button onClick={logout} disabled={!isLoggedIn}>Log Out</button>
        </div>
      </div>
    </section>
  );
}