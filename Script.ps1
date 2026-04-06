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

$controllerPath = Join-Path $RootDir "apps\api\VeritasAtlas.Api\Controllers\StatementsController.cs"
$statementsApiPath = Join-Path $webRoot "src\api\statements.ts"
$useStatementsPath = Join-Path $webRoot "src\hooks\useStatements.ts"
$useStatementDetailPath = Join-Path $webRoot "src\hooks\useStatementDetail.ts"
$statementsPagePath = Join-Path $webRoot "src\pages\StatementsPage.tsx"
$statementDetailPagePath = Join-Path $webRoot "src\pages\StatementDetailPage.tsx"
$evidenceDetailPagePath = Join-Path $webRoot "src\pages\EvidenceDetailPage.tsx"
$mainTsxPath = Join-Path $webRoot "src\main.tsx"

Push-Location $RootDir

Write-Host ""
Write-Host "Checkpointing current code with git..." -ForegroundColor Cyan

git add -A
if ($LASTEXITCODE -ne 0) {
    Pop-Location
    throw "git add failed."
}

$commitMessage = "checkpoint before phase 6.8 - $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')"
git commit -m $commitMessage 2>$null
if ($LASTEXITCODE -ne 0) {
    Write-Host "No new commit created. Continuing with phase 6.8." -ForegroundColor Yellow
}
else {
    Write-Host "Created git commit: $commitMessage" -ForegroundColor Green
}

Pop-Location

$controllerContent = @'
using Microsoft.AspNetCore.Mvc;
using VeritasAtlas.Api.Contracts.Statements;
using VeritasAtlas.Application.Contracts.Statements;
using VeritasAtlas.Application.Interfaces;
using VeritasAtlas.Infrastructure.Services;

namespace VeritasAtlas.Api.Controllers;

[ApiController]
[Route("api/v1/statements")]
public class StatementsController : ControllerBase
{
    private readonly IStatementService _statementService;
    private readonly StatementService _statementQueryService;

    public StatementsController(
        IStatementService statementService,
        StatementService statementQueryService)
    {
        _statementService = statementService;
        _statementQueryService = statementQueryService;
    }

    [HttpPost]
    public async Task<ActionResult<CreateStatementResponse>> CreateStatement(
        [FromBody] CreateStatementRequest request,
        CancellationToken cancellationToken = default)
    {
        var entity = await _statementService.ExtractStatementAsync(
            request.EvidenceId,
            request.Text,
            request.CreatedBy,
            cancellationToken);

        var response = new CreateStatementResponse(
            entity.Id,
            entity.EvidenceId,
            entity.DocumentId,
            entity.Text.Raw,
            entity.Polarity.ToString(),
            entity.Status.ToString(),
            entity.Topic,
            entity.CreatedAtUtc,
            entity.UpdatedAtUtc);

        return Ok(response);
    }

    [HttpGet]
    public async Task<IActionResult> GetStatements([FromQuery] StatementListRequest request)
    {
        var (total, items) = await _statementQueryService.GetStatementsAsync(request.Page, request.PageSize);

        return Ok(new
        {
            Total = total,
            Page = request.Page,
            PageSize = request.PageSize,
            Items = items
        });
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetStatement(Guid id)
    {
        var result = await _statementQueryService.GetStatementByIdAsync(id);

        if (result == null)
        {
            return NotFound();
        }

        return Ok(result);
    }
}
'@

$statementsApiContent = @'
import { apiGet } from "./client";

export type StatementItem = {
  id: string;
  text: string;
  topic: string | null;
  predicate: string | null;
  object: string | null;
  polarity: string;
  status: string;
  createdAt: string;
};

export type StatementDetail = {
  id: string;
  text: string;
  topic: string | null;
  predicate: string | null;
  object: string | null;
  polarity: string;
  status: string;
  evidenceId: string | null;
  personId: string | null;
  createdAt: string;
};

export type StatementsResponse = {
  total: number;
  page: number;
  pageSize: number;
  items: StatementItem[];
};

export async function getStatements(): Promise<StatementsResponse> {
  return apiGet<StatementsResponse>("/api/v1/statements?page=1&pageSize=50");
}

export async function getStatementById(id: string): Promise<StatementDetail> {
  return apiGet<StatementDetail>(`/api/v1/statements/${id}`);
}
'@

$useStatementsContent = @'
import { useQuery } from "@tanstack/react-query";
import { getStatements } from "../api/statements";

export function useStatements() {
  return useQuery({
    queryKey: ["statements"],
    queryFn: getStatements,
  });
}
'@

$useStatementDetailContent = @'
import { useQuery } from "@tanstack/react-query";
import { getStatementById } from "../api/statements";

export function useStatementDetail(id?: string) {
  return useQuery({
    queryKey: ["statement-detail", id],
    queryFn: () => getStatementById(id!),
    enabled: !!id,
  });
}
'@

$statementsPageContent = @'
import { Link } from "react-router-dom";
import { useStatements } from "../hooks/useStatements";

export function StatementsPage() {
  const query = useStatements();

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

      {query.isLoading && <p>Loading statements...</p>}

      {query.isError && (
        <p style={{ color: "crimson" }}>
          Failed to load statements: {(query.error as Error).message}
        </p>
      )}

      {query.isSuccess && (
        <>
          <p>Showing {query.data.items.length} of {query.data.total} statements</p>

          {query.data.items.length === 0 ? (
            <div style={emptyStateStyle}>
              <p style={{ margin: 0 }}>No statements found.</p>
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
                    <th style={thStyle}>Created</th>
                  </tr>
                </thead>
                <tbody>
                  {query.data.items.map((item) => (
                    <tr key={item.id}>
                      <td style={tdStyle}><Link to={`/statements/${item.id}`}>{item.text}</Link></td>
                      <td style={tdStyle}>{item.topic ?? "N/A"}</td>
                      <td style={tdStyle}>{item.polarity}</td>
                      <td style={tdStyle}>{item.status}</td>
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

const emptyStateStyle: React.CSSProperties = {
  border: "1px solid #eee",
  borderRadius: "12px",
  padding: "20px",
  color: "#666",
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
  const relatedStatements = statementsQuery.data?.items.filter((x) => x.id && x.text) ?? [];

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
        {statementsQuery.isLoading && <p>Loading statements...</p>}
        {statementsQuery.isSuccess && relatedStatements.length === 0 && <p>No statement links shown yet.</p>}
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
      <p>Statements list and detail pages are now available.</p>
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
Write-Host "Applying Phase 6.8..." -ForegroundColor Cyan

Write-Utf8File -Path $controllerPath -Content $controllerContent
Write-Utf8File -Path $statementsApiPath -Content $statementsApiContent
Write-Utf8File -Path $useStatementsPath -Content $useStatementsContent
Write-Utf8File -Path $useStatementDetailPath -Content $useStatementDetailContent
Write-Utf8File -Path $statementsPagePath -Content $statementsPageContent
Write-Utf8File -Path $statementDetailPagePath -Content $statementDetailPageContent
Write-Utf8File -Path $evidenceDetailPagePath -Content $evidenceDetailPageContent
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
Write-Host "Phase 6.8 completed successfully." -ForegroundColor Green