import { useProtectedAuthDiagnostics, usePublicAuthDiagnostics } from "../hooks/useAuthDiagnostics";

export function AuthDiagnosticsPage() {
  const publicQuery = usePublicAuthDiagnostics();
  const protectedQuery = useProtectedAuthDiagnostics();

  return (
    <div style={{ fontFamily: "Arial, sans-serif", padding: 24 }}>
      <h1 style={{ marginTop: 0 }}>Auth Diagnostics</h1>
      <p style={{ color: "#555" }}>
        Debug surface for JWT issuer, audience, claims, and protected endpoint validation.
      </p>

      <div style={gridStyle}>
        <div style={panelStyle}>
          <h3 style={{ marginTop: 0 }}>Public Diagnostics</h3>
          {publicQuery.isLoading && <p>Loading...</p>}
          {publicQuery.isError && <p style={{ color: "crimson" }}>Failed to load public diagnostics.</p>}
          {publicQuery.data && (
            <ul style={{ marginBottom: 0 }}>
              <li>Issuer: {publicQuery.data.jwtIssuer}</li>
              <li>Audience: {publicQuery.data.jwtAudience}</li>
              <li>Mode: {publicQuery.data.mode}</li>
            </ul>
          )}
        </div>

        <div style={panelStyle}>
          <h3 style={{ marginTop: 0 }}>Protected Diagnostics</h3>
          {protectedQuery.isLoading && <p>Loading...</p>}
          {protectedQuery.isError && <p style={{ color: "crimson" }}>Protected endpoint still failing.</p>}
          {protectedQuery.data && (
            <ul style={{ marginBottom: 0 }}>
              <li>Username: {protectedQuery.data.username}</li>
              <li>Role: {protectedQuery.data.role}</li>
              <li>Authenticated: {String(protectedQuery.data.isAuthenticated)}</li>
              <li>Claims: {protectedQuery.data.claims.length}</li>
            </ul>
          )}
        </div>
      </div>
    </div>
  );
}

const gridStyle: React.CSSProperties = {
  display: "grid",
  gridTemplateColumns: "1fr 1fr",
  gap: 16,
};

const panelStyle: React.CSSProperties = {
  border: "1px solid #ddd",
  borderRadius: 14,
  padding: 16,
};