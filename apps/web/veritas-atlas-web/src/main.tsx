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
import { ReviewQueuePage } from "./pages/ReviewQueuePage";
import { ReviewWorkspacePage } from "./pages/ReviewWorkspacePage";
import { PublicationDeskPage } from "./pages/PublicationDeskPage";
import { DashboardPage } from "./pages/DashboardPage";
import { ExecutiveOverviewPage } from "./pages/ExecutiveOverviewPage";
import { DeliveryControlTowerPage } from "./pages/DeliveryControlTowerPage";
import { WorkstreamBoardPage } from "./pages/WorkstreamBoardPage";
import { EscalationCenterPage } from "./pages/EscalationCenterPage";
import { GovernanceConsolePage } from "./pages/GovernanceConsolePage";
import { PublicationReadinessBoardPage } from "./pages/PublicationReadinessBoardPage";
import { DecisionLogPage } from "./pages/DecisionLogPage";
import { OperationsIntelligencePage } from "./pages/OperationsIntelligencePage";
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
<Link to="/executive-overview">Executive Overview</Link>
          <Link to="/delivery-control-tower">Delivery Control Tower</Link>
          <Link to="/workstream-board">Workstream Board</Link>
          <Link to="/escalation-center">Escalation Center</Link>
          <Link to="/operations-intelligence">Operations Intelligence</Link>
          <Link to="/investigation-navigator">Investigation Navigator</Link>
          <Link to="/health">Health</Link>
          <Link to="/operations">Operations Hub</Link>
          <Link to="/navigator">Navigator</Link>
          <Link to="/persons">Persons</Link>
          <Link to="/persons/new">New Person</Link>
          <Link to="/cases">Cases</Link>
          <Link to="/cases/new">New Case</Link>
          <Link to="/reviews">Reviews</Link>
<Link to="/governance-console">Governance Console</Link>
          <Link to="/publication-readiness">Publication Readiness</Link>
          <Link to="/decision-log">Decision Log</Link>
          <Link to="/review-queue">Review Queue</Link>
          <Link to="/review-workspace">Review Workspace</Link>
          <Link to="/publication-desk">Publication Desk</Link>
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
{ path: "/executive-overview", element: <ExecutiveOverviewPage /> },
  { path: "/delivery-control-tower", element: <DeliveryControlTowerPage /> },
  { path: "/workstream-board", element: <WorkstreamBoardPage /> },
  { path: "/escalation-center", element: <EscalationCenterPage /> },
{ path: "/governance-console", element: <GovernanceConsolePage /> },
  { path: "/publication-readiness", element: <PublicationReadinessBoardPage /> },
  { path: "/decision-log", element: <DecisionLogPage /> },
  { path: "/operations-intelligence", element: <OperationsIntelligencePage /> },
  { path: "/investigation-navigator", element: <InvestigationNavigatorPage /> },
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
  { path: "/review-queue", element: <ReviewQueuePage /> },
  { path: "/review-workspace", element: <ReviewWorkspacePage /> },
  { path: "/publication-desk", element: <PublicationDeskPage /> },
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
