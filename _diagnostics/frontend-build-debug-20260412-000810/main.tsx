import React from "react";
import ReactDOM from "react-dom/client";
import { createBrowserRouter, RouterProvider } from "react-router-dom";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
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
import { OperationsIntelligencePage } from "./pages/OperationsIntelligencePage";
import { GovernanceConsolePage } from "./pages/GovernanceConsolePage";
import { PublicationReadinessBoardPage } from "./pages/PublicationReadinessBoardPage";
import { DecisionLogPage } from "./pages/DecisionLogPage";
import { ExecutiveOverviewPage } from "./pages/ExecutiveOverviewPage";
import { DeliveryControlTowerPage } from "./pages/DeliveryControlTowerPage";
import { WorkstreamBoardPage } from "./pages/WorkstreamBoardPage";
import { EscalationCenterPage } from "./pages/EscalationCenterPage";
import { AnalyticsCenterPage } from "./pages/AnalyticsCenterPage";
import { AgentRunsBoardPage } from "./pages/AgentRunsBoardPage";
import { CaseFlowMapPage } from "./pages/CaseFlowMapPage";
import { QualityRadarPage } from "./pages/QualityRadarPage";
import { ReviewQueuePage } from "./pages/ReviewQueuePage";
import { ReviewWorkspacePage } from "./pages/ReviewWorkspacePage";
import { PublicationDeskPage } from "./pages/PublicationDeskPage";
import { CaseDetailPage } from "./pages/CaseDetailPage";
import { ReviewsPage } from "./pages/ReviewsPage";
import { DashboardPage } from "./pages/DashboardPage";
import { AuthDiagnosticsPage } from "./pages/AuthDiagnosticsPage";
import { PersistenceConsolePage } from "./pages/PersistenceConsolePage";
import { Phase12DataCenterPage } from "./pages/Phase12DataCenterPage";
import { LoginPage } from "./pages/LoginPage";
import { AuthMePage } from "./pages/AuthMePage";
import { Phase11AuthCenterPage } from "./pages/Phase11AuthCenterPage";
import { RolePolicyPage } from "./pages/RolePolicyPage";
import { AuditPersistencePage } from "./pages/AuditPersistencePage";
import { Phase10HardeningCenterPage } from "./pages/Phase10HardeningCenterPage";
import { WorkflowAuditPage } from "./pages/WorkflowAuditPage";
import { Phase9IntegrityCenterPage } from "./pages/Phase9IntegrityCenterPage";
import { SeededLifecycleRunnerPage } from "./pages/SeededLifecycleRunnerPage";
import { WorkflowValidationPage } from "./pages/WorkflowValidationPage";
import { IntegrationTestCenterPage } from "./pages/IntegrationTestCenterPage";
import { LifecycleSeedPage } from "./pages/LifecycleSeedPage";
import { WorkflowDiagnosticsPage } from "./pages/WorkflowDiagnosticsPage";
import { MutationPlaygroundPage } from "./pages/MutationPlaygroundPage";
import { Phase7CloseoutPage } from "./pages/Phase7CloseoutPage";
import { WorkflowConsolePage } from "./pages/WorkflowConsolePage";
import { OperationalActionsPage } from "./pages/OperationalActionsPage";
import { AppSurfaceCatalogPage } from "./pages/AppSurfaceCatalogPage";
import { UICompletionCenterPage } from "./pages/UICompletionCenterPage";
import { NavigationIndexPage } from "./pages/NavigationIndexPage";
import { FrontendClosurePage } from "./pages/FrontendClosurePage";
import { PlatformAtlasPage } from "./pages/PlatformAtlasPage";
import { WorkspaceMapPage } from "./pages/WorkspaceMapPage";
import { RouteRegistryPage } from "./pages/RouteRegistryPage";
import { SystemReadinessMapPage } from "./pages/SystemReadinessMapPage";
import { OperatorCockpitPage } from "./pages/OperatorCockpitPage";
import { FinalControlCenterPage } from "./pages/FinalControlCenterPage";
import { ExecutiveReadoutWorkspacePage } from "./pages/ExecutiveReadoutWorkspacePage";
import { OperationalPortfolioPage } from "./pages/OperationalPortfolioPage";
import { OpsFinalizationWorkspacePage } from "./pages/OpsFinalizationWorkspacePage";
import { ReleaseReadinessHubPage } from "./pages/ReleaseReadinessHubPage";
import { DeliveryCloseoutWorkspacePage } from "./pages/DeliveryCloseoutWorkspacePage";
import { OpsCoordinationCenterPage } from "./pages/OpsCoordinationCenterPage";
import { KnowledgeGraphHubPage } from "./pages/KnowledgeGraphHubPage";
import { PublicationGovernanceWorkspacePage } from "./pages/PublicationGovernanceWorkspacePage";
import { ReadinessRadarWorkspacePage } from "./pages/ReadinessRadarWorkspacePage";
import { EvidenceFlowStudioPage } from "./pages/EvidenceFlowStudioPage";
import { OperationalHandoffPage } from "./pages/OperationalHandoffPage";
import { DecisionQueuePage } from "./pages/DecisionQueuePage";
import { DecisionIntelligencePage } from "./pages/DecisionIntelligencePage";
import { UnifiedSearchWorkspacePage } from "./pages/UnifiedSearchWorkspacePage";
import { SourceIntelligencePage } from "./pages/SourceIntelligencePage";
import { EntityGraphWorkspacePage } from "./pages/EntityGraphWorkspacePage";
import { ReviewAuditWorkspacePage } from "./pages/ReviewAuditWorkspacePage";
import { IntelligenceHubPage } from "./pages/IntelligenceHubPage";
import { AgentRunsMonitorPage } from "./pages/AgentRunsMonitorPage";
import { GlobalSearchPage } from "./pages/GlobalSearchPage";
import { ObservabilityDashboardPage } from "./pages/ObservabilityDashboardPage";
import { AdminControlTowerPage } from "./pages/AdminControlTowerPage";
import { ReviewDecisionBoardPage } from "./pages/ReviewDecisionBoardPage";
import { PublicationPipelinePage } from "./pages/PublicationPipelinePage";
import { EvidenceTracePage } from "./pages/EvidenceTracePage";
import { NarrativeBuilderPage } from "./pages/NarrativeBuilderPage";
import { TruthReviewStudioPage } from "./pages/TruthReviewStudioPage";
import { ContradictionResolutionWorkspacePage } from "./pages/ContradictionResolutionWorkspacePage";
import { CaseScoreboardPage } from "./pages/CaseScoreboardPage";
import { CaseExplorerPage } from "./pages/CaseExplorerPage";
import { CaseWorkbenchPage } from "./pages/CaseWorkbenchPage";
import { ContradictionsPage } from "./pages/ContradictionsPage";
import { ContradictionDetailPage } from "./pages/ContradictionDetailPage";
import { ResolutionBoardPage } from "./pages/ResolutionBoardPage";
import { HomePage } from "./pages/HomePage";
import { RequireAuth } from "./components/RequireAuth";
import "./index.css";

const queryClient = new QueryClient();

function protect(element: React.ReactElement) {
  return <RequireAuth>{element}</RequireAuth>;
}

const router = createBrowserRouter([
  { path: "/", element: protect(<HomePage />) },
  { path: "/dashboard", element: protect(<DashboardPage />) },
  { path: "/login", element: <LoginPage /> },
  { path: "/workflow-audit", element: protect(<WorkflowAuditPage />) },
  { path: "/persistence", element: protect(<PersistenceConsolePage />) },
  { path: "/persistence-console", element: protect(<PersistenceConsolePage />) },

  { path: "/global-search", element: protect(<GlobalSearchPage />) },
  { path: "/observability", element: protect(<ObservabilityDashboardPage />) },
  { path: "/admin", element: protect(<AdminControlTowerPage />) },
  { path: "/auth-diagnostics", element: protect(<AuthDiagnosticsPage />) },
  { path: "/phase-12-data-center", element: protect(<Phase12DataCenterPage />) },
  { path: "/auth-me", element: protect(<AuthMePage />) },
  { path: "/phase-11-auth-center", element: protect(<Phase11AuthCenterPage />) },
  { path: "/role-policy", element: protect(<RolePolicyPage />) },
  { path: "/audit-persistence", element: protect(<AuditPersistencePage />) },
  { path: "/phase-10-hardening-center", element: protect(<Phase10HardeningCenterPage />) },
  { path: "/phase-9-integrity-center", element: protect(<Phase9IntegrityCenterPage />) },
  { path: "/seeded-lifecycle-runner", element: protect(<SeededLifecycleRunnerPage />) },
  { path: "/workflow-validation", element: protect(<WorkflowValidationPage />) },
  { path: "/integration-test-center", element: protect(<IntegrationTestCenterPage />) },
  { path: "/lifecycle-seed", element: protect(<LifecycleSeedPage />) },
  { path: "/workflow-diagnostics", element: protect(<WorkflowDiagnosticsPage />) },
  { path: "/mutation-playground", element: protect(<MutationPlaygroundPage />) },
  { path: "/phase-7-closeout", element: protect(<Phase7CloseoutPage />) },
  { path: "/workflow-console", element: protect(<WorkflowConsolePage />) },
  { path: "/operational-actions", element: protect(<OperationalActionsPage />) },
  { path: "/decision-intelligence", element: protect(<DecisionIntelligencePage />) },
  { path: "/knowledge-graph-hub", element: protect(<KnowledgeGraphHubPage />) },
  { path: "/release-readiness-hub", element: protect(<ReleaseReadinessHubPage />) },
  { path: "/system-readiness-map", element: protect(<SystemReadinessMapPage />) },
  { path: "/platform-atlas", element: protect(<PlatformAtlasPage />) },
  { path: "/app-surface-catalog", element: protect(<AppSurfaceCatalogPage />) },
  { path: "/ui-completion-center", element: protect(<UICompletionCenterPage />) },
  { path: "/navigation-index", element: protect(<NavigationIndexPage />) },
  { path: "/frontend-closure", element: protect(<FrontendClosurePage />) },
  { path: "/workspace-map", element: protect(<WorkspaceMapPage />) },
  { path: "/route-registry", element: protect(<RouteRegistryPage />) },
  { path: "/operator-cockpit", element: protect(<OperatorCockpitPage />) },
  { path: "/final-control-center", element: protect(<FinalControlCenterPage />) },
  { path: "/executive-readout-workspace", element: protect(<ExecutiveReadoutWorkspacePage />) },
  { path: "/operational-portfolio", element: protect(<OperationalPortfolioPage />) },
  { path: "/ops-finalization-workspace", element: protect(<OpsFinalizationWorkspacePage />) },
  { path: "/delivery-closeout", element: protect(<DeliveryCloseoutWorkspacePage />) },
  { path: "/ops-coordination-center", element: protect(<OpsCoordinationCenterPage />) },
  { path: "/publication-governance", element: protect(<PublicationGovernanceWorkspacePage />) },
  { path: "/readiness-radar-workspace", element: protect(<ReadinessRadarWorkspacePage />) },
  { path: "/evidence-flow-studio", element: protect(<EvidenceFlowStudioPage />) },
  { path: "/operational-handoff", element: protect(<OperationalHandoffPage />) },
  { path: "/decision-queue", element: protect(<DecisionQueuePage />) },
  { path: "/intelligence", element: protect(<IntelligenceHubPage />) },
  { path: "/agents", element: protect(<AgentRunsMonitorPage />) },
  { path: "/operations", element: protect(<OperationsHubPage />) },
  { path: "/navigator", element: protect(<InvestigationNavigatorPage />) },
  { path: "/operations-intelligence", element: protect(<OperationsIntelligencePage />) },
  { path: "/investigation-navigator", element: protect(<InvestigationNavigatorPage />) },
  { path: "/analytics-center", element: protect(<AnalyticsCenterPage />) },
  { path: "/executive-overview", element: protect(<ExecutiveOverviewPage />) },
  { path: "/delivery-control-tower", element: protect(<DeliveryControlTowerPage />) },
  { path: "/workstream-board", element: protect(<WorkstreamBoardPage />) },
  { path: "/escalation-center", element: protect(<EscalationCenterPage />) },
  { path: "/agent-runs-board", element: protect(<AgentRunsBoardPage />) },
  { path: "/case-flow-map", element: protect(<CaseFlowMapPage />) },
  { path: "/quality-radar", element: protect(<QualityRadarPage />) },

  { path: "/persons", element: protect(<PersonsPage />) },
  { path: "/persons/new", element: protect(<CreatePersonPage />) },
  { path: "/persons/:id", element: protect(<PersonDetailPage />) },

  { path: "/cases", element: protect(<CasesPage />) },
  { path: "/cases/new", element: protect(<CreateCasePage />) },
  { path: "/cases/:id", element: protect(<CaseDetailPage />) },
  { path: "/case-explorer", element: protect(<CaseExplorerPage />) },
  { path: "/case-explorer/:id", element: protect(<CaseWorkbenchPage />) },
  { path: "/truth-review-studio", element: protect(<TruthReviewStudioPage />) },
  { path: "/global-search-workspace", element: protect(<UnifiedSearchWorkspacePage />) },
  { path: "/source-intelligence", element: protect(<SourceIntelligencePage />) },
  { path: "/entity-graph", element: protect(<EntityGraphWorkspacePage />) },
  { path: "/review-audit", element: protect(<ReviewAuditWorkspacePage />) },
  { path: "/review-decision-board", element: protect(<ReviewDecisionBoardPage />) },
  { path: "/publication-pipeline", element: protect(<PublicationPipelinePage />) },
  { path: "/evidence-trace", element: protect(<EvidenceTracePage />) },
  { path: "/narrative-builder", element: protect(<NarrativeBuilderPage />) },
  { path: "/contradiction-resolution/:id", element: protect(<ContradictionResolutionWorkspacePage />) },
  { path: "/case-scoreboard", element: protect(<CaseScoreboardPage />) },

  { path: "/reviews", element: protect(<ReviewsPage />) },
  { path: "/review-queue", element: protect(<ReviewQueuePage />) },
  { path: "/review-workspace", element: protect(<ReviewWorkspacePage />) },
  { path: "/publication-desk", element: protect(<PublicationDeskPage />) },
  { path: "/governance-console", element: protect(<GovernanceConsolePage />) },
  { path: "/publication-readiness", element: protect(<PublicationReadinessBoardPage />) },
  { path: "/decision-log", element: protect(<DecisionLogPage />) },

  { path: "/ingestion", element: protect(<IngestionWorkspacePage />) },
  { path: "/sources", element: protect(<SourcesPage />) },
  { path: "/sources/new", element: protect(<CreateSourcePage />) },
  { path: "/sources/:id", element: protect(<SourceDetailPage />) },
  { path: "/documents", element: protect(<DocumentsPage />) },
  { path: "/documents/new", element: protect(<CreateDocumentPage />) },
  { path: "/documents/:id", element: protect(<DocumentDetailPage />) },
  { path: "/evidence", element: protect(<EvidencePage />) },
  { path: "/evidence/new", element: protect(<CreateEvidencePage />) },
  { path: "/evidence/:id", element: protect(<EvidenceDetailPage />) },
  { path: "/statements", element: protect(<StatementsPage />) },
  { path: "/statements/new", element: protect(<CreateStatementPage />) },
  { path: "/statements/:id", element: protect(<StatementDetailPage />) },
  { path: "/claims", element: protect(<ClaimsPage />) },
  { path: "/claims/:id", element: protect(<ClaimDetailPage />) },
  { path: "/claims/workspace", element: protect(<ClaimsWorkspacePage />) },
  { path: "/contradictions", element: protect(<ContradictionsPage />) },
  { path: "/contradictions/:id", element: protect(<ContradictionDetailPage />) },
  { path: "/contradictions/workspace", element: protect(<ContradictionsWorkspacePage />) },
  { path: "/resolution-board", element: protect(<ResolutionBoardPage />) }
]);

ReactDOM.createRoot(document.getElementById("root")!).render(
  <React.StrictMode>
    <QueryClientProvider client={queryClient}>
      <RouterProvider router={router} />
    </QueryClientProvider>
  </React.StrictMode>
);