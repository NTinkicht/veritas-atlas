import { Link } from "react-router-dom";
import { useSources } from "../hooks/useSources";
import { useDocuments } from "../hooks/useDocuments";
import { useEvidenceList } from "../hooks/useEvidenceList";
import { useStatements } from "../hooks/useStatements";
import { useClaims } from "../hooks/useClaims";

export function OperationsHubPage() {
  const sourcesQuery = useSources();
  const documentsQuery = useDocuments();
  const evidenceQuery = useEvidenceList();
  const statementsQuery = useStatements();
  const claimsQuery = useClaims();

  return (
    <div style={{ fontFamily: "Arial, sans-serif", padding: "24px" }}>
      <header style={{ marginBottom: "24px" }}>
        <h1 style={{ margin: 0 }}>Operations Hub</h1>
        <p style={{ color: "#555" }}>
          High-level operational view across ingestion, statements, claims, and contradiction preparation.
        </p>
        <nav style={{ display: "flex", gap: "16px", marginTop: "12px", flexWrap: "wrap" }}>
          <Link to="/">Home</Link>
          <Link to="/dashboard">Dashboard</Link>
          <Link to="/navigator">Navigator</Link>
          <Link to="/sources">Sources</Link>
          <Link to="/documents">Documents</Link>
          <Link to="/evidence">Evidence</Link>
          <Link to="/statements">Statements</Link>
          <Link to="/claims">Claims</Link>
          <Link to="/contradictions/workspace">Contradictions Workspace</Link>
        </nav>
      </header>

      <section style={statsGridStyle}>
        <StatCard title="Sources" value={sourcesQuery.data?.items.length} loading={sourcesQuery.isLoading} link="/sources" />
        <StatCard title="Documents" value={documentsQuery.data?.items.length} loading={documentsQuery.isLoading} link="/documents" />
        <StatCard title="Evidence" value={evidenceQuery.data?.items.length} loading={evidenceQuery.isLoading} link="/evidence" />
        <StatCard title="Statements" value={statementsQuery.data?.items.length} loading={statementsQuery.isLoading} link="/statements" />
        <StatCard title="Claims" value={claimsQuery.data?.items.length} loading={claimsQuery.isLoading} link="/claims" />
      </section>

      <section style={panelGridStyle}>
        <div style={panelStyle}>
          <h3 style={{ marginTop: 0 }}>Recommended flow</h3>
          <ol style={{ marginBottom: 0, paddingLeft: "18px" }}>
            <li>Create or inspect a source</li>
            <li>Open the document and evidence records</li>
            <li>Extract statements from evidence</li>
            <li>Create claims from statements</li>
            <li>Open contradiction preparation once multiple claims exist</li>
          </ol>
        </div>

        <div style={panelStyle}>
          <h3 style={{ marginTop: 0 }}>Quick actions</h3>
          <div style={{ display: "flex", gap: "12px", flexWrap: "wrap" }}>
            <QuickLink to="/sources/new" text="New Source" />
            <QuickLink to="/documents/new" text="New Document" />
            <QuickLink to="/evidence/new" text="New Evidence" />
            <QuickLink to="/statements/new" text="New Statement" />
            <QuickLink to="/claims/workspace" text="Claims Workspace" />
            <QuickLink to="/contradictions/workspace" text="Contradictions Workspace" />
          </div>
        </div>
      </section>

      <section style={panelGridStyle}>
        <DataPanel
          title="Recent statements"
          loading={statementsQuery.isLoading}
          emptyText="No statements available."
          items={(statementsQuery.data?.items ?? []).slice(0, 5).map((item) => (
            <li key={item.id}>
              <Link to={`/statements/${item.id}`}>{item.text}</Link>
            </li>
          ))}
        />

        <DataPanel
          title="Recent claims"
          loading={claimsQuery.isLoading}
          emptyText="No claims available."
          items={(claimsQuery.data?.items ?? []).slice(0, 5).map((item) => (
            <li key={item.id}>
              <Link to={`/claims/${item.id}`}>{item.topic}</Link> - {item.type}
            </li>
          ))}
        />
      </section>
    </div>
  );
}

function StatCard({
  title,
  value,
  loading,
  link,
}: {
  title: string;
  value?: number;
  loading: boolean;
  link: string;
}) {
  return (
    <Link to={link} style={statCardStyle}>
      <span style={{ color: "#666", fontSize: "14px" }}>{title}</span>
      <strong style={{ fontSize: "28px" }}>{loading ? "..." : value ?? 0}</strong>
      <span style={{ color: "#1976d2" }}>Open</span>
    </Link>
  );
}

function QuickLink({ to, text }: { to: string; text: string }) {
  return (
    <Link to={to} style={actionLinkStyle}>
      {text}
    </Link>
  );
}

function DataPanel({
  title,
  loading,
  emptyText,
  items,
}: {
  title: string;
  loading: boolean;
  emptyText: string;
  items: React.ReactNode[];
}) {
  return (
    <div style={panelStyle}>
      <h3 style={{ marginTop: 0 }}>{title}</h3>
      {loading && <p>Loading...</p>}
      {!loading && items.length === 0 && <p>{emptyText}</p>}
      {!loading && items.length > 0 && <ul style={{ marginBottom: 0 }}>{items}</ul>}
    </div>
  );
}

const statsGridStyle: React.CSSProperties = {
  display: "grid",
  gridTemplateColumns: "repeat(auto-fit, minmax(180px, 1fr))",
  gap: "16px",
  marginBottom: "24px",
};

const panelGridStyle: React.CSSProperties = {
  display: "grid",
  gridTemplateColumns: "repeat(auto-fit, minmax(320px, 1fr))",
  gap: "16px",
  marginBottom: "24px",
};

const statCardStyle: React.CSSProperties = {
  border: "1px solid #ddd",
  borderRadius: "14px",
  padding: "16px",
  textDecoration: "none",
  color: "inherit",
  display: "grid",
  gap: "8px",
};

const panelStyle: React.CSSProperties = {
  border: "1px solid #ddd",
  borderRadius: "14px",
  padding: "16px",
};

const actionLinkStyle: React.CSSProperties = {
  padding: "10px 16px",
  borderRadius: "8px",
  border: "1px solid #1976d2",
  textDecoration: "none",
  color: "inherit",
};
