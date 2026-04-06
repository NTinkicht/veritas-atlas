param(
    [string]$RootDir = "C:\Projects\veritas-atlas"
)

$ErrorActionPreference = "Stop"

function Ensure-Directory {
    param([string]$Path)
    if (-not (Test-Path $Path)) {
        New-Item -ItemType Directory -Force -Path $Path | Out-Null
    }
}

function Write-Utf8File {
    param(
        [string]$Path,
        [string]$Content
    )
    $parent = Split-Path -Parent $Path
    Ensure-Directory $parent
    Set-Content -Path $Path -Value $Content -Encoding UTF8
    Write-Host "Wrote: $Path" -ForegroundColor Green
}

$solutionPath = Join-Path $RootDir "VeritasAtlas.slnx"
$webRoot = Join-Path $RootDir "apps\web\veritas-atlas-web"

$statementsPagePath = Join-Path $webRoot "src\pages\StatementsPage.tsx"
$statementDetailPagePath = Join-Path $webRoot "src\pages\StatementDetailPage.tsx"
$claimsWorkspacePagePath = Join-Path $webRoot "src\pages\ClaimsWorkspacePage.tsx"
$mainTsxPath = Join-Path $webRoot "src\main.tsx"

Push-Location $RootDir

Write-Host ""
Write-Host "Checkpointing current code with git..." -ForegroundColor Cyan

git add -A
if ($LASTEXITCODE -ne 0) {
    Pop-Location
    throw "git add failed."
}

$commitMessage = "checkpoint before phase 6.10 - $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')"
git commit -m $commitMessage 2>$null
if ($LASTEXITCODE -ne 0) {
    Write-Host "No new commit created. Continuing with phase 6.10." -ForegroundColor Yellow
}
else {
    Write-Host "Created git commit: $commitMessage" -ForegroundColor Green
}

Pop-Location

$statementsPageContent = @'
import { Link, useSearchParams } from "react-router-dom";
import { useMemo, useState } from "react";
import { useStatements } from "../hooks/useStatements";

export function StatementsPage() {
  const [params] = useSearchParams();
  const forcedEvidenceId = params.get("evidenceId") ?? "";

  const [search, setSearch] = useState("");
  const [statusFilter, setStatusFilter] = useState("All");
  const [polarityFilter, setPolarityFilter] = useState("All");
  const [topicFilter, setTopicFilter] = useState("All");

  const query = useStatements();
  const items = query.data?.items ?? [];

  const filteredItems = useMemo(() => {
    const term = search.trim().toLowerCase();

    return items.filter((item) => {
      const matchesEvidence =
        forcedEvidenceId.length === 0 || item.evidenceId === forcedEvidenceId;

      const matchesSearch =
        term.length === 0 ||
        item.text.toLowerCase().includes(term) ||
        (item.topic ?? "").toLowerCase().includes(term) ||
        (item.predicate ?? "").toLowerCase().includes(term) ||
        (item.object ?? "").toLowerCase().includes(term);

      const matchesStatus =
        statusFilter === "All" || item.status === statusFilter;

      const matchesPolarity =
        polarityFilter === "All" || item.polarity === polarityFilter;

      const matchesTopic =
        topicFilter === "All" || (item.topic ?? "N/A") === topicFilter;

      return matchesEvidence && matchesSearch && matchesStatus && matchesPolarity && matchesTopic;
    });
  }, [items, forcedEvidenceId, search, statusFilter, polarityFilter, topicFilter]);

  const statuses = useMemo(
    () => ["All", ...Array.from(new Set(items.map((x) => x.status))).sort()],
    [items]
  );

  const polarities = useMemo(
    () => ["All", ...Array.from(new Set(items.map((x) => x.polarity))).sort()],
    [items]
  );

  const topics = useMemo(
    () => ["All", ...Array.from(new Set(items.map((x) => x.topic ?? "N/A"))).sort()],
    [items]
  );

  return (
    <div style={{ fontFamily: "Arial, sans-serif", padding: "24px" }}>
      <header style={{ marginBottom: "24px" }}>
        <h1 style={{ margin: 0 }}>Statements</h1>
        <p style={{ color: "#555" }}>Live statement catalog from Veritas Atlas API</p>
        <nav style={{ display: "flex", gap: "16px", marginTop: "12px", flexWrap: "wrap" }}>
          <Link to="/">Home</Link>
          <Link to="/dashboard">Dashboard</Link>
          <Link to="/sources">Sources</Link>
          <Link to="/documents">Documents</Link>
          <Link to="/evidence">Evidence</Link>
          <Link to="/statements">Statements</Link>
        </nav>
      </header>

      <div style={{ marginBottom: "16px", display: "flex", gap: "12px", flexWrap: "wrap" }}>
        <Link to="/statements/new" style={actionLinkStyle}>Create New Statement</Link>
      </div>

      <section style={filterPanelStyle}>
        <input
          value={search}
          onChange={(e) => setSearch(e.target.value)}
          placeholder="Search statement text, topic, predicate, or object"
          style={inputStyle}
        />
        <select value={statusFilter} onChange={(e) => setStatusFilter(e.target.value)} style={selectStyle}>
          {statuses.map((value) => (
            <option key={value} value={value}>{value}</option>
          ))}
        </select>
        <select value={polarityFilter} onChange={(e) => setPolarityFilter(e.target.value)} style={selectStyle}>
          {polarities.map((value) => (
            <option key={value} value={value}>{value}</option>
          ))}
        </select>
        <select value={topicFilter} onChange={(e) => setTopicFilter(e.target.value)} style={selectStyle}>
          {topics.map((value) => (
            <option key={value} value={value}>{value}</option>
          ))}
        </select>
      </section>

      {forcedEvidenceId && (
        <div style={hintCardStyle}>
          <strong>Evidence scope:</strong> {forcedEvidenceId}
        </div>
      )}

      {query.isLoading && <p>Loading statements...</p>}

      {query.isError && (
        <p style={{ color: "crimson" }}>
          Failed to load statements: {(query.error as Error).message}
        </p>
      )}

      {query.isSuccess && (
        <>
          <p>Showing {filteredItems.length} of {query.data.total} statements</p>

          {filteredItems.length === 0 ? (
            <div style={emptyStateStyle}>
              <p style={{ margin: 0 }}>No statements found for the current filters.</p>
            </div>
          ) : (
            <div style={{ overflowX: "auto" }}>
              <table style={tableStyle}>
                <thead>
                  <tr>
                    <th style={thStyle}>Text</th>
                    <th style={thStyle}>Topic</th>
                    <th style={thStyle}>Polarity</th>
                    <th style={thStyle}>Status</th>
                    <th style={thStyle}>Evidence</th>
                    <th style={thStyle}>Created</th>
                  </tr>
                </thead>
                <tbody>
                  {filteredItems.map((item) => (
                    <tr key={item.id}>
                      <td style={tdStyle}><Link to={`/statements/${item.id}`}>{item.text}</Link></td>
                      <td style={tdStyle}>{item.topic ?? "N/A"}</td>
                      <td style={tdStyle}>{item.polarity}</td>
                      <td style={tdStyle}>{item.status}</td>
                      <td style={tdStyle}>{item.evidenceId ? <Link to={`/evidence/${item.evidenceId}`}>{item.evidenceId}</Link> : "N/A"}</td>
                      <td style={tdStyle}>{new Date(item.createdAt).toLocaleString()}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          )}
        </>
      )}
    </div>
  );
}

const filterPanelStyle: React.CSSProperties = {
  display: "grid",
  gridTemplateColumns: "minmax(260px, 1fr) 180px 180px 180px",
  gap: "12px",
  marginBottom: "16px",
};

const inputStyle: React.CSSProperties = {
  width: "100%",
  boxSizing: "border-box",
  padding: "10px 12px",
  borderRadius: "8px",
  border: "1px solid #ccc",
  font: "inherit",
};

const selectStyle: React.CSSProperties = {
  width: "100%",
  boxSizing: "border-box",
  padding: "10px 12px",
  borderRadius: "8px",
  border: "1px solid #ccc",
  font: "inherit",
};

const tableStyle: React.CSSProperties = {
  width: "100%",
  borderCollapse: "collapse",
  marginTop: "16px",
};

const thStyle: React.CSSProperties = {
  textAlign: "left",
  borderBottom: "1px solid #ccc",
  padding: "10px",
};

const tdStyle: React.CSSProperties = {
  borderBottom: "1px solid #eee",
  padding: "10px",
  verticalAlign: "top",
};

const actionLinkStyle: React.CSSProperties = {
  padding: "10px 16px",
  borderRadius: "8px",
  border: "1px solid #1976d2",
  textDecoration: "none",
  color: "inherit",
  display: "inline-flex",
  alignItems: "center",
};

const emptyStateStyle: React.CSSProperties = {
  border: "1px solid #eee",
  borderRadius: "12px",
  padding: "20px",
  color: "#666",
};

const hintCardStyle: React.CSSProperties = {
  marginBottom: "16px",
  padding: "12px 14px",
  border: "1px solid #d9e6ff",
  borderRadius: "12px",
  background: "#f8fbff",
};
'@

$statementDetailPageContent = @'
import { Link, useParams } from "react-router-dom";
import { useStatementDetail } from "../hooks/useStatementDetail";

export function StatementDetailPage() {
  const { id } = useParams();
  const query = useStatementDetail(id);

  if (query.isLoading) {
    return <div style={{ fontFamily: "Arial, sans-serif", padding: "24px" }}>Loading statement...</div>;
  }

  if (query.isError) {
    return <div style={{ fontFamily: "Arial, sans-serif", padding: "24px", color: "crimson" }}>Failed to load statement: {(query.error as Error).message}</div>;
  }

  if (!query.data) {
    return <div style={{ fontFamily: "Arial, sans-serif", padding: "24px" }}>Statement not found.</div>;
  }

  const item = query.data;

  return (
    <div style={{ fontFamily: "Arial, sans-serif", padding: "24px" }}>
      <nav style={{ display: "flex", gap: "16px", marginBottom: "20px", flexWrap: "wrap" }}>
        <Link to="/statements">Back to Statements</Link>
        {item.evidenceId && <Link to={`/evidence/${item.evidenceId}`}>Evidence</Link>}
        <Link to={`/claims/workspace?statementId=${item.id}`}>Claims Workspace</Link>
      </nav>

      <h1 style={{ marginTop: 0 }}>Statement</h1>

      <div style={cardStyle}>
        <Row label="Id" value={item.id} />
        <Row label="Text" value={item.text} />
        <Row label="Topic" value={item.topic ?? "N/A"} />
        <Row label="Predicate" value={item.predicate ?? "N/A"} />
        <Row label="Object" value={item.object ?? "N/A"} />
        <Row label="Polarity" value={item.polarity} />
        <Row label="Status" value={item.status} />
        <Row label="Evidence Id" value={item.evidenceId ?? "N/A"} />
        <Row label="Person Id" value={item.personId ?? "N/A"} />
        <Row label="Created" value={new Date(item.createdAt).toLocaleString()} />
      </div>

      <div style={{ display: "flex", gap: "12px", flexWrap: "wrap" }}>
        {item.evidenceId && <Link to={`/evidence/${item.evidenceId}`} style={actionLinkStyle}>Open Evidence</Link>}
        <Link to={`/claims/workspace?statementId=${item.id}`} style={actionLinkStyle}>Open Claims Workspace</Link>
      </div>
    </div>
  );
}

function Row({ label, value }: { label: string; value: string }) {
  return (
    <div style={{ display: "grid", gridTemplateColumns: "180px 1fr", gap: "12px", padding: "6px 0" }}>
      <strong>{label}</strong>
      <span>{value}</span>
    </div>
  );
}

const cardStyle: React.CSSProperties = {
  border: "1px solid #ddd",
  borderRadius: "12px",
  padding: "16px",
  marginBottom: "16px",
};

const actionLinkStyle: React.CSSProperties = {
  padding: "10px 16px",
  borderRadius: "8px",
  border: "1px solid #1976d2",
  textDecoration: "none",
  color: "inherit",
};
'@

$claimsWorkspacePageContent = @'
import { Link, useSearchParams } from "react-router-dom";

export function ClaimsWorkspacePage() {
  const [params] = useSearchParams();
  const statementId = params.get("statementId") ?? "";

  return (
    <div style={{ fontFamily: "Arial, sans-serif", padding: "24px", maxWidth: "900px" }}>
      <header style={{ marginBottom: "24px" }}>
        <h1 style={{ margin: 0 }}>Claims Workspace</h1>
        <p style={{ color: "#555" }}>Lightweight workspace placeholder for the next claim slice.</p>
        <nav style={{ display: "flex", gap: "16px", marginTop: "12px", flexWrap: "wrap" }}>
          <Link to="/">Home</Link>
          <Link to="/statements">Statements</Link>
          {statementId && <Link to={`/statements/${statementId}`}>Statement</Link>}
        </nav>
      </header>

      <div style={cardStyle}>
        <Row label="Statement Id" value={statementId || "N/A"} />
        <Row label="Status" value="Placeholder ready" />
        <Row label="Next use" value="Claim create/list/detail can be added here next." />
      </div>

      <div style={cardStyle}>
        <h3 style={{ marginTop: 0 }}>Planned actions</h3>
        <ul style={{ marginBottom: 0 }}>
          <li>Create claim from current statement</li>
          <li>List claims linked to statement</li>
          <li>Navigate to contradiction workflows later</li>
        </ul>
      </div>
    </div>
  );
}

function Row({ label, value }: { label: string; value: string }) {
  return (
    <div style={{ display: "grid", gridTemplateColumns: "180px 1fr", gap: "12px", padding: "6px 0" }}>
      <strong>{label}</strong>
      <span>{value}</span>
    </div>
  );
}

const cardStyle: React.CSSProperties = {
  border: "1px solid #ddd",
  borderRadius: "12px",
  padding: "16px",
  marginBottom: "16px",
};
'@

$evidenceDetailPageContent = @'
import { Link, useParams } from "react-router-dom";
import { useEvidenceDetail } from "../hooks/useEvidenceDetail";
import { useStatements } from "../hooks/useStatements";

export function EvidenceDetailPage() {
  const { id } = useParams();
  const query = useEvidenceDetail(id);
  const statementsQuery = useStatements();

  if (query.isLoading) {
    return <div style={{ fontFamily: "Arial, sans-serif", padding: "24px" }}>Loading evidence...</div>;
  }

  if (query.isError) {
    return <div style={{ fontFamily: "Arial, sans-serif", padding: "24px", color: "crimson" }}>Failed to load evidence: {(query.error as Error).message}</div>;
  }

  if (!query.data) {
    return <div style={{ fontFamily: "Arial, sans-serif", padding: "24px" }}>Evidence not found.</div>;
  }

  const item = query.data;
  const relatedStatements = (statementsQuery.data?.items ?? []).filter((x) => x.evidenceId === item.id);

  return (
    <div style={{ fontFamily: "Arial, sans-serif", padding: "24px" }}>
      <nav style={{ display: "flex", gap: "16px", marginBottom: "20px", flexWrap: "wrap" }}>
        <Link to="/evidence">Back to Evidence</Link>
        {item.documentId && <Link to={`/documents/${item.documentId}`}>Document</Link>}
        <Link to="/statements">Statements</Link>
      </nav>

      <h1 style={{ marginTop: 0 }}>Evidence {item.id}</h1>

      <div style={cardStyle}>
        <Row label="Id" value={item.id} />
        <Row label="Source Id" value={item.sourceId} />
        <Row label="Document Id" value={item.documentId ?? "N/A"} />
        <Row label="Type" value={item.type} />
        <Row label="Status" value={item.status} />
        <Row label="LanguageCode" value={item.languageCode ?? "N/A"} />
        <Row label="ContentHash" value={item.contentHash ?? "N/A"} />
        <Row label="Captured" value={item.capturedAtUtc ? new Date(item.capturedAtUtc).toLocaleString() : "N/A"} />
        <Row label="Span" value={item.span ? `${item.span.startOffset} - ${item.span.endOffset}` : "N/A"} />
        <Row label="Created" value={new Date(item.createdAtUtc).toLocaleString()} />
        <Row label="Updated" value={new Date(item.updatedAtUtc).toLocaleString()} />
      </div>

      <div style={cardStyle}>
        <h3 style={{ marginTop: 0 }}>Content</h3>
        <pre style={{ whiteSpace: "pre-wrap", margin: 0, fontFamily: "inherit" }}>{item.content}</pre>
      </div>

      <div style={cardStyle}>
        <h3 style={{ marginTop: 0 }}>Statements</h3>
        <div style={{ display: "flex", gap: "12px", flexWrap: "wrap", marginBottom: "12px" }}>
          <Link to={`/statements/new?evidenceId=${item.id}`} style={actionLinkStyle}>Create Statement for this Evidence</Link>
          <Link to={`/statements?evidenceId=${item.id}`} style={actionLinkStyle}>View Statements for this Evidence</Link>
        </div>

        {statementsQuery.isLoading && <p>Loading statements...</p>}
        {statementsQuery.isSuccess && relatedStatements.length === 0 && <p>No statements are linked to this evidence yet.</p>}
        {statementsQuery.isSuccess && relatedStatements.length > 0 && (
          <ul>
            {relatedStatements.map((statement) => (
              <li key={statement.id}>
                <Link to={`/statements/${statement.id}`}>{statement.text}</Link>
              </li>
            ))}
          </ul>
        )}
      </div>
    </div>
  );
}

function Row({ label, value }: { label: string; value: string }) {
  return (
    <div style={{ display: "grid", gridTemplateColumns: "180px 1fr", gap: "12px", padding: "6px 0" }}>
      <strong>{label}</strong>
      <span>{value}</span>
    </div>
  );
}

const cardStyle: React.CSSProperties = {
  border: "1px solid #ddd",
  borderRadius: "12px",
  padding: "16px",
  marginBottom: "16px",
};

const actionLinkStyle: React.CSSProperties = {
  padding: "10px 16px",
  borderRadius: "8px",
  border: "1px solid #1976d2",
  textDecoration: "none",
  color: "inherit",
};
'@

$mainTsxContent = @'
import React from "react";
import ReactDOM from "react-dom/client";
import {
  createBrowserRouter,
  RouterProvider,
  Link,
} from "react-router-dom";
import {
  QueryClient,
  QueryClientProvider,
  useQuery,
} from "@tanstack/react-query";
import { getDatabaseHealth, getHealth } from "./api/health";
import { PersonsPage } from "./pages/PersonsPage";
import { PersonDetailPage } from "./pages/PersonDetailPage";
import { CasesPage } from "./pages/CasesPage";
import { CreateCasePage } from "./pages/CreateCasePage";
import { CreatePersonPage } from "./pages/CreatePersonPage";
import { CreateStatementPage } from "./pages/CreateStatementPage";
import { CreateDocumentPage } from "./pages/CreateDocumentPage";
import { CreateEvidencePage } from "./pages/CreateEvidencePage";
import { CreateSourcePage } from "./pages/CreateSourcePage";
import { IngestionWorkspacePage } from "./pages/IngestionWorkspacePage";
import { SourcesPage } from "./pages/SourcesPage";
import { SourceDetailPage } from "./pages/SourceDetailPage";
import { DocumentsPage } from "./pages/DocumentsPage";
import { DocumentDetailPage } from "./pages/DocumentDetailPage";
import { EvidencePage } from "./pages/EvidencePage";
import { EvidenceDetailPage } from "./pages/EvidenceDetailPage";
import { StatementsPage } from "./pages/StatementsPage";
import { StatementDetailPage } from "./pages/StatementDetailPage";
import { ClaimsWorkspacePage } from "./pages/ClaimsWorkspacePage";
import { CaseDetailPage } from "./pages/CaseDetailPage";
import { ReviewsPage } from "./pages/ReviewsPage";
import { DashboardPage } from "./pages/DashboardPage";
import "./index.css";

const queryClient = new QueryClient();

function Layout({ children }: { children: React.ReactNode }) {
  return (
    <div style={{ fontFamily: "Arial, sans-serif", padding: "24px" }}>
      <header style={{ marginBottom: "24px" }}>
        <h1 style={{ margin: 0 }}>Veritas Atlas</h1>
        <p style={{ color: "#555" }}>Frontend connected to live API</p>
        <nav style={{ display: "flex", gap: "16px", marginTop: "12px", flexWrap: "wrap" }}>
          <Link to="/">Home</Link>
          <Link to="/dashboard">Dashboard</Link>
          <Link to="/health">Health</Link>
          <Link to="/persons">Persons</Link>
          <Link to="/persons/new">New Person</Link>
          <Link to="/cases">Cases</Link>
          <Link to="/cases/new">New Case</Link>
          <Link to="/reviews">Reviews</Link>
          <Link to="/ingestion">Ingestion</Link>
          <Link to="/sources">Sources</Link>
          <Link to="/documents">Documents</Link>
          <Link to="/evidence">Evidence</Link>
          <Link to="/statements">Statements</Link>
          <Link to="/claims/workspace">Claims Workspace</Link>
          <Link to="/sources/new">New Source</Link>
          <Link to="/documents/new">New Document</Link>
          <Link to="/evidence/new">New Evidence</Link>
          <Link to="/statements/new">New Statement</Link>
        </nav>
      </header>
      <main>{children}</main>
    </div>
  );
}

function HomePage() {
  return (
    <Layout>
      <h2>Home</h2>
      <p>Statement filtering and claim workspace placeholder are now available.</p>
    </Layout>
  );
}

function HealthPage() {
  const healthQuery = useQuery({
    queryKey: ["health"],
    queryFn: getHealth,
  });

  const dbHealthQuery = useQuery({
    queryKey: ["health-db"],
    queryFn: getDatabaseHealth,
  });

  return (
    <Layout>
      <h2>Health</h2>

      <section style={{ marginBottom: "24px" }}>
        <h3>API</h3>
        {healthQuery.isLoading && <p>Loading API health...</p>}
        {healthQuery.isError && (
          <p style={{ color: "crimson" }}>
            API health failed: {(healthQuery.error as Error).message}
          </p>
        )}
        {healthQuery.isSuccess && (
          <pre>{JSON.stringify(healthQuery.data, null, 2)}</pre>
        )}
      </section>

      <section>
        <h3>Database</h3>
        {dbHealthQuery.isLoading && <p>Loading DB health...</p>}
        {dbHealthQuery.isError && (
          <p style={{ color: "crimson" }}>
            DB health failed: {(dbHealthQuery.error as Error).message}
          </p>
        )}
        {dbHealthQuery.isSuccess && (
          <pre>{JSON.stringify(dbHealthQuery.data, null, 2)}</pre>
        )}
      </section>
    </Layout>
  );
}

const router = createBrowserRouter([
  { path: "/", element: <HomePage /> },
  { path: "/dashboard", element: <DashboardPage /> },
  { path: "/health", element: <HealthPage /> },
  { path: "/persons", element: <PersonsPage /> },
  { path: "/persons/new", element: <CreatePersonPage /> },
  { path: "/persons/:id", element: <PersonDetailPage /> },
  { path: "/cases", element: <CasesPage /> },
  { path: "/cases/new", element: <CreateCasePage /> },
  { path: "/cases/:id", element: <CaseDetailPage /> },
  { path: "/reviews", element: <ReviewsPage /> },
  { path: "/ingestion", element: <IngestionWorkspacePage /> },
  { path: "/sources", element: <SourcesPage /> },
  { path: "/sources/new", element: <CreateSourcePage /> },
  { path: "/sources/:id", element: <SourceDetailPage /> },
  { path: "/documents", element: <DocumentsPage /> },
  { path: "/documents/new", element: <CreateDocumentPage /> },
  { path: "/documents/:id", element: <DocumentDetailPage /> },
  { path: "/evidence", element: <EvidencePage /> },
  { path: "/evidence/new", element: <CreateEvidencePage /> },
  { path: "/evidence/:id", element: <EvidenceDetailPage /> },
  { path: "/statements", element: <StatementsPage /> },
  { path: "/statements/new", element: <CreateStatementPage /> },
  { path: "/statements/:id", element: <StatementDetailPage /> },
  { path: "/claims/workspace", element: <ClaimsWorkspacePage /> },
]);

ReactDOM.createRoot(document.getElementById("root")!).render(
  <React.StrictMode>
    <QueryClientProvider client={queryClient}>
      <RouterProvider router={router} />
    </QueryClientProvider>
  </React.StrictMode>
);
'@

Write-Host ""
Write-Host "Applying Phase 6.10..." -ForegroundColor Cyan

Write-Utf8File -Path $statementsPagePath -Content $statementsPageContent
Write-Utf8File -Path $statementDetailPagePath -Content $statementDetailPageContent
Write-Utf8File -Path $claimsWorkspacePagePath -Content $claimsWorkspacePageContent
Write-Utf8File -Path $evidenceDetailPagePath -Content $evidenceDetailPageContent
Write-Utf8File -Path $mainTsxPath -Content $mainTsxContent
Write-Utf8File -Path $statementsApiPath -Content $statementsApiContent
Write-Utf8File -Path $createStatementPagePath -Content $createStatementPageContent

Push-Location $RootDir
Write-Host ""
Write-Host "Building backend..." -ForegroundColor Cyan
dotnet build $solutionPath
if ($LASTEXITCODE -ne 0) {
    Pop-Location
    throw "dotnet build failed."
}
Pop-Location

Push-Location $webRoot
Write-Host ""
Write-Host "Building frontend..." -ForegroundColor Cyan
npm run build
if ($LASTEXITCODE -ne 0) {
    Pop-Location
    throw "npm run build failed."
}
Pop-Location

Write-Host ""
Write-Host "Phase 6.10 completed successfully." -ForegroundColor Green