param(
    [string]$RootDir = "C:\Projects\veritas-atlas"
)

$ErrorActionPreference = "Stop"

function Ensure-Directory {
    param([string]$Path)
    if ([string]::IsNullOrWhiteSpace($Path)) {
        throw "Ensure-Directory received an empty path."
    }

    if (-not (Test-Path $Path)) {
        New-Item -ItemType Directory -Force -Path $Path | Out-Null
    }
}

function Write-Utf8File {
    param(
        [string]$Path,
        [string]$Content
    )

    if ([string]::IsNullOrWhiteSpace($Path)) {
        throw "Write-Utf8File received an empty path."
    }

    $parent = Split-Path -Parent $Path
    if ([string]::IsNullOrWhiteSpace($parent)) {
        throw "Could not resolve parent directory for path: $Path"
    }

    Ensure-Directory $parent
    Set-Content -Path $Path -Value $Content -Encoding UTF8
    Write-Host "Wrote: $Path" -ForegroundColor Green
}

$solutionPath = Join-Path $RootDir "VeritasAtlas.slnx"
$webRoot = Join-Path $RootDir "apps\web\veritas-atlas-web"

$operationsHubPagePath = Join-Path $webRoot "src\pages\OperationsHubPage.tsx"
$investigationNavigatorPagePath = Join-Path $webRoot "src\pages\InvestigationNavigatorPage.tsx"
$mainTsxPath = Join-Path $webRoot "src\main.tsx"

Push-Location $RootDir

Write-Host ""
Write-Host "Checkpointing current code with git..." -ForegroundColor Cyan

git add -A
if ($LASTEXITCODE -ne 0) {
    Pop-Location
    throw "git add failed."
}

$commitMessage = "checkpoint before phase 6.15 - $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')"
git commit -m $commitMessage 2>$null
if ($LASTEXITCODE -ne 0) {
    Write-Host "No new commit created. Continuing with phase 6.15." -ForegroundColor Yellow
}
else {
    Write-Host "Created git commit: $commitMessage" -ForegroundColor Green
}

Pop-Location

$operationsHubPageContent = @'
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
'@

$investigationNavigatorPageContent = @'
import { Link, useSearchParams } from "react-router-dom";
import { useMemo } from "react";
import { useStatements } from "../hooks/useStatements";
import { useClaims } from "../hooks/useClaims";

export function InvestigationNavigatorPage() {
  const [params] = useSearchParams();
  const q = (params.get("q") ?? "").toLowerCase();

  const statementsQuery = useStatements();
  const claimsQuery = useClaims();

  const statementMatches = useMemo(() => {
    const items = statementsQuery.data?.items ?? [];
    if (!q) return items.slice(0, 12);

    return items.filter((item) =>
      item.text.toLowerCase().includes(q) ||
      (item.topic ?? "").toLowerCase().includes(q) ||
      (item.object ?? "").toLowerCase().includes(q) ||
      (item.predicate ?? "").toLowerCase().includes(q)
    ).slice(0, 12);
  }, [statementsQuery.data, q]);

  const claimMatches = useMemo(() => {
    const items = claimsQuery.data?.items ?? [];
    if (!q) return items.slice(0, 12);

    return items.filter((item) =>
      item.topic.toLowerCase().includes(q) ||
      item.normalizedText.toLowerCase().includes(q) ||
      item.type.toLowerCase().includes(q)
    ).slice(0, 12);
  }, [claimsQuery.data, q]);

  return (
    <div style={{ fontFamily: "Arial, sans-serif", padding: "24px" }}>
      <header style={{ marginBottom: "24px" }}>
        <h1 style={{ margin: 0 }}>Investigation Navigator</h1>
        <p style={{ color: "#555" }}>
          Fast cross-navigation between statements, claims, and contradiction preparation.
        </p>
        <nav style={{ display: "flex", gap: "16px", marginTop: "12px", flexWrap: "wrap" }}>
          <Link to="/">Home</Link>
          <Link to="/operations">Operations Hub</Link>
          <Link to="/statements">Statements</Link>
          <Link to="/claims">Claims</Link>
          <Link to="/contradictions/workspace">Contradictions Workspace</Link>
        </nav>
      </header>

      <div style={hintCardStyle}>
        Current deep-link query: <strong>{q || "(none)"}</strong>
      </div>

      <section style={gridStyle}>
        <div style={panelStyle}>
          <h3 style={{ marginTop: 0 }}>Statement matches</h3>
          {statementsQuery.isLoading && <p>Loading statements...</p>}
          {statementsQuery.isError && <p style={{ color: "crimson" }}>Failed to load statements.</p>}
          {statementsQuery.isSuccess && statementMatches.length === 0 && <p>No matching statements.</p>}
          {statementsQuery.isSuccess && statementMatches.length > 0 && (
            <ul style={{ marginBottom: 0 }}>
              {statementMatches.map((item) => (
                <li key={item.id}>
                  <Link to={`/statements/${item.id}`}>{item.text}</Link>
                </li>
              ))}
            </ul>
          )}
        </div>

        <div style={panelStyle}>
          <h3 style={{ marginTop: 0 }}>Claim matches</h3>
          {claimsQuery.isLoading && <p>Loading claims...</p>}
          {claimsQuery.isError && <p style={{ color: "crimson" }}>Failed to load claims.</p>}
          {claimsQuery.isSuccess && claimMatches.length === 0 && <p>No matching claims.</p>}
          {claimsQuery.isSuccess && claimMatches.length > 0 && (
            <ul style={{ marginBottom: 0 }}>
              {claimMatches.map((item) => (
                <li key={item.id}>
                  <Link to={`/claims/${item.id}`}>{item.topic}</Link> - {item.type}
                </li>
              ))}
            </ul>
          )}
        </div>
      </section>
    </div>
  );
}

const gridStyle: React.CSSProperties = {
  display: "grid",
  gridTemplateColumns: "repeat(auto-fit, minmax(320px, 1fr))",
  gap: "16px",
};

const panelStyle: React.CSSProperties = {
  border: "1px solid #ddd",
  borderRadius: "14px",
  padding: "16px",
};

const hintCardStyle: React.CSSProperties = {
  marginBottom: "16px",
  padding: "12px 14px",
  border: "1px solid #d9e6ff",
  borderRadius: "12px",
  background: "#f8fbff",
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
import { ClaimsPage } from "./pages/ClaimsPage";
import { ClaimDetailPage } from "./pages/ClaimDetailPage";
import { ClaimsWorkspacePage } from "./pages/ClaimsWorkspacePage";
import { ContradictionsWorkspacePage } from "./pages/ContradictionsWorkspacePage";
import { OperationsHubPage } from "./pages/OperationsHubPage";
import { InvestigationNavigatorPage } from "./pages/InvestigationNavigatorPage";
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
          <Link to="/operations">Operations Hub</Link>
          <Link to="/navigator">Navigator</Link>
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
          <Link to="/claims">Claims</Link>
          <Link to="/claims/workspace">Claims Workspace</Link>
          <Link to="/contradictions/workspace">Contradictions Workspace</Link>
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
      <p>Operational navigation pack is now available.</p>

      <div style={{ display: "grid", gridTemplateColumns: "repeat(auto-fit, minmax(220px, 1fr))", gap: "16px", marginTop: "20px" }}>
        <QuickCard title="Operations Hub" text="Monitor ingestion, statements, claims, and next actions." to="/operations" />
        <QuickCard title="Navigator" text="Cross-search statements and claims using deep-link query patterns." to="/navigator" />
        <QuickCard title="Claims Workspace" text="Create claims from statement context." to="/claims/workspace" />
        <QuickCard title="Contradictions Workspace" text="Prepare contradiction review from statements and claims." to="/contradictions/workspace" />
      </div>
    </Layout>
  );
}

function QuickCard({ title, text, to }: { title: string; text: string; to: string }) {
  return (
    <Link
      to={to}
      style={{
        border: "1px solid #ddd",
        borderRadius: "14px",
        padding: "16px",
        textDecoration: "none",
        color: "inherit",
        display: "block",
      }}
    >
      <strong style={{ display: "block", marginBottom: "8px" }}>{title}</strong>
      <span>{text}</span>
    </Link>
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
  { path: "/operations", element: <OperationsHubPage /> },
  { path: "/navigator", element: <InvestigationNavigatorPage /> },
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
  { path: "/claims", element: <ClaimsPage /> },
  { path: "/claims/:id", element: <ClaimDetailPage /> },
  { path: "/claims/workspace", element: <ClaimsWorkspacePage /> },
  { path: "/contradictions/workspace", element: <ContradictionsWorkspacePage /> },
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
Write-Host "Applying Phase 6.15..." -ForegroundColor Cyan

Write-Utf8File -Path $claimsPagePath -Content $claimsPageContent
Write-Utf8File -Path $claimDetailPagePath -Content $claimDetailPageContent
Write-Utf8File -Path $claimsWorkspacePagePath -Content $claimsWorkspacePageContent
Write-Utf8File -Path $statementDetailPagePath -Content $statementDetailPageContent
Write-Utf8File -Path $contradictionsWorkspacePagePath -Content $contradictionsWorkspacePageContent
Write-Utf8File -Path $operationsHubPagePath -Content $operationsHubPageContent
Write-Utf8File -Path $investigationNavigatorPagePath -Content $investigationNavigatorPageContent
Write-Utf8File -Path $mainTsxPath -Content $mainTsxContent

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
Write-Host "Phase 6.15 completed successfully." -ForegroundColor Green
