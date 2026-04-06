import { Link } from "react-router-dom";
import type { CSSProperties } from "react";
import { useDashboardData } from "../hooks/useDashboardData";

export function DashboardPage() {
  const dashboardQuery = useDashboardData();

  return (
    <div style={{ fontFamily: "Arial, sans-serif", padding: "24px" }}>
      <header style={{ marginBottom: "24px" }}>
        <h1 style={{ margin: 0 }}>Dashboard</h1>
        <p style={{ color: "#555" }}>Live operational view from Veritas Atlas API</p>
        <nav style={{ display: "flex", gap: "16px", marginTop: "12px", flexWrap: "wrap" }}>
          <Link to="/">Home</Link>
          <Link to="/dashboard">Dashboard</Link>
          <Link to="/health">Health</Link>
          <Link to="/persons">Persons</Link>
          <Link to="/persons/new">New Person</Link>
          <Link to="/cases">Cases</Link>
          <Link to="/cases/new">New Case</Link>
          <Link to="/reviews">Reviews</Link>
        </nav>
      </header>

      {dashboardQuery.isLoading && <p>Loading dashboard...</p>}

      {dashboardQuery.isError && (
        <p style={{ color: "crimson" }}>
          Failed to load dashboard: {(dashboardQuery.error as Error).message}
        </p>
      )}

      {dashboardQuery.isSuccess && (
        <>
          <section
            style={{
              display: "grid",
              gridTemplateColumns: "repeat(auto-fit, minmax(220px, 1fr))",
              gap: "16px",
              marginBottom: "28px",
            }}
          >
            <StatCard title="Cases" value={dashboardQuery.data.cases.totalCount.toString()} />
            <StatCard title="Reviews" value={dashboardQuery.data.reviews.totalCount.toString()} />
            <StatCard title="Agent Runs" value={dashboardQuery.data.agentRuns.totalCount.toString()} />
            <StatCard title="API Health" value={formatHealth(dashboardQuery.data.health)} />
            <StatCard title="DB Health" value={formatHealth(dashboardQuery.data.dbHealth)} />
          </section>

          <div style={{ marginBottom: "20px", display: "flex", gap: "12px", flexWrap: "wrap" }}>
            <Link to="/persons/new" style={actionLinkStyle}>Create New Person</Link>
            <Link to="/cases/new" style={actionLinkStyle}>Create New Case</Link>
          </div>

          <section style={{ marginBottom: "28px" }}>
            <h2>Recent Cases</h2>
            {dashboardQuery.data.cases.items.length === 0 ? (
              <p>No cases found.</p>
            ) : (
              <div style={{ overflowX: "auto" }}>
                <table style={tableStyle}>
                  <thead>
                    <tr>
                      <th style={thStyle}>Title</th>
                      <th style={thStyle}>Status</th>
                      <th style={thStyle}>Created</th>
                    </tr>
                  </thead>
                  <tbody>
                    {dashboardQuery.data.cases.items.slice(0, 5).map((item) => (
                      <tr key={item.id}>
                        <td style={tdStyle}>
                          <Link to={`/cases/${item.id}`}>{item.title}</Link>
                        </td>
                        <td style={tdStyle}>{item.status}</td>
                        <td style={tdStyle}>{formatDate(item.createdAtUtc)}</td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            )}
          </section>

          <section style={{ marginBottom: "28px" }}>
            <h2>Recent Reviews</h2>
            {dashboardQuery.data.reviews.items.length === 0 ? (
              <p>No reviews found.</p>
            ) : (
              <div style={{ overflowX: "auto" }}>
                <table style={tableStyle}>
                  <thead>
                    <tr>
                      <th style={thStyle}>Case</th>
                      <th style={thStyle}>Status</th>
                      <th style={thStyle}>Decision</th>
                      <th style={thStyle}>Reviewer</th>
                    </tr>
                  </thead>
                  <tbody>
                    {dashboardQuery.data.reviews.items.slice(0, 5).map((item) => (
                      <tr key={item.id}>
                        <td style={tdStyle}>
                          <Link to={`/cases/${item.caseId}`}>{item.caseId}</Link>
                        </td>
                        <td style={tdStyle}>{item.status}</td>
                        <td style={tdStyle}>{item.decision}</td>
                        <td style={tdStyle}>{item.reviewer}</td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            )}
          </section>

          <section>
            <h2>Recent Agent Runs</h2>
            {dashboardQuery.data.agentRuns.items.length === 0 ? (
              <p>No agent runs found.</p>
            ) : (
              <div style={{ overflowX: "auto" }}>
                <table style={tableStyle}>
                  <thead>
                    <tr>
                      <th style={thStyle}>Agent Name</th>
                      <th style={thStyle}>Type</th>
                      <th style={thStyle}>Status</th>
                      <th style={thStyle}>Started</th>
                    </tr>
                  </thead>
                  <tbody>
                    {dashboardQuery.data.agentRuns.items.slice(0, 5).map((item) => (
                      <tr key={item.id}>
                        <td style={tdStyle}>{item.agentName}</td>
                        <td style={tdStyle}>{item.agentType}</td>
                        <td style={tdStyle}>{item.status}</td>
                        <td style={tdStyle}>{formatDate(item.startedAtUtc)}</td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            )}
          </section>
        </>
      )}
    </div>
  );
}

function StatCard({ title, value }: { title: string; value: string }) {
  return (
    <div
      style={{
        border: "1px solid #ddd",
        borderRadius: "12px",
        padding: "18px",
      }}
    >
      <div style={{ color: "#666", marginBottom: "8px" }}>{title}</div>
      <div style={{ fontSize: "28px", fontWeight: 700 }}>{value}</div>
    </div>
  );
}

function formatDate(value: string) {
  return new Date(value).toLocaleString();
}

function formatHealth(value: unknown) {
  if (typeof value === "string") {
    return value;
  }

  if (value && typeof value === "object") {
    return "Healthy";
  }

  return "Unknown";
}

const tableStyle: CSSProperties = {
  width: "100%",
  borderCollapse: "collapse",
  marginTop: "12px",
};

const thStyle: CSSProperties = {
  textAlign: "left",
  borderBottom: "1px solid #ccc",
  padding: "10px",
};

const tdStyle: CSSProperties = {
  borderBottom: "1px solid #eee",
  padding: "10px",
};

const actionLinkStyle: CSSProperties = {
  padding: "10px 16px",
  borderRadius: "8px",
  border: "1px solid #1976d2",
  textDecoration: "none",
  color: "inherit",
  display: "inline-flex",
  alignItems: "center",
};
