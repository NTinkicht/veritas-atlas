# Web surface audit (VER-P0-17)

Every route registered in `apps/web/veritas-atlas-web/src/routes.tsx`, with its backing endpoint(s) and verdict.

- **REAL**: renders data from a real backend endpoint, or is a pure navigation page. Mounted in every build.
- **SEEDED-OR-DEMO**: derives its content from the scripted seeded workflow or scenario snapshot. Mounted in Development builds only.
- **DEAD**: placeholder shell with fixed text and no data; it backs nothing. Mounted in Development builds only; not deleted here (see Decisions).

The table mirrors `src/routeAudit.ts`. `src/routes.test.ts` fails when a route has no row here or in `routeAudit.ts`, and when a production build would mount any route that is not REAL. `npm run build` runs the test first (`prebuild`).

| Route | Page | Verdict | Backing |
| --- | --- | --- | --- |
| `/` | HomePage | REAL | none (navigation landing page, no data claims) |
| `/dashboard` | DashboardPage | REAL | /api/v1/cases, /claims, /contradictions, /workflow-audit/entries (scenario snapshot only in Development) |
| `/login` | LoginPage | REAL | /api/v1/auth/login |
| `/workflow-audit` | WorkflowAuditPage | REAL | /api/v1/workflow-audit/entries/clear |
| `/persistence` | PersistenceConsolePage | SEEDED-OR-DEMO | /api/v1/persistence/snapshot/reset: scenario snapshot of the seeded workflow scenario |
| `/persistence-console` | PersistenceConsolePage | SEEDED-OR-DEMO | /api/v1/persistence/snapshot/reset: scenario snapshot of the seeded workflow scenario |
| `/global-search` | GlobalSearchPage | DEAD | none (placeholder shell: fixed "Stable production shell." text, no data fetch) |
| `/observability` | ObservabilityDashboardPage | DEAD | none (placeholder shell: fixed "Stable production shell." text, no data fetch) |
| `/admin` | AdminControlTowerPage | DEAD | none (placeholder shell: fixed "Stable production shell." text, no data fetch) |
| `/auth-diagnostics` | AuthDiagnosticsPage | DEAD | none (placeholder shell: fixed "Stable production shell." text, no data fetch) |
| `/phase-12-data-center` | Phase12DataCenterPage | DEAD | none (placeholder shell: fixed "Stable production shell." text, no data fetch) |
| `/auth-me` | AuthMePage | DEAD | none (placeholder shell: fixed "Stable production shell." text, no data fetch) |
| `/phase-11-auth-center` | Phase11AuthCenterPage | DEAD | none (placeholder shell: fixed "Stable production shell." text, no data fetch) |
| `/role-policy` | RolePolicyPage | DEAD | none (placeholder shell: fixed "Stable production shell." text, no data fetch) |
| `/audit-persistence` | AuditPersistencePage | DEAD | none (placeholder shell: fixed "Stable production shell." text, no data fetch) |
| `/phase-10-hardening-center` | Phase10HardeningCenterPage | DEAD | none (placeholder shell: fixed "Stable production shell." text, no data fetch) |
| `/phase-9-integrity-center` | Phase9IntegrityCenterPage | DEAD | none (placeholder shell: fixed "Stable production shell." text, no data fetch) |
| `/seeded-lifecycle-runner` | SeededLifecycleRunnerPage | DEAD | none (placeholder shell: fixed "Stable production shell." text, no data fetch) |
| `/workflow-validation` | WorkflowValidationPage | DEAD | none (placeholder shell: fixed "Stable production shell." text, no data fetch) |
| `/integration-test-center` | IntegrationTestCenterPage | DEAD | none (placeholder shell: fixed "Stable production shell." text, no data fetch) |
| `/lifecycle-seed` | LifecycleSeedPage | DEAD | none (placeholder shell: fixed "Stable production shell." text, no data fetch) |
| `/workflow-diagnostics` | WorkflowDiagnosticsPage | DEAD | none (placeholder shell: fixed "Stable production shell." text, no data fetch) |
| `/mutation-playground` | MutationPlaygroundPage | DEAD | none (placeholder shell: fixed "Stable production shell." text, no data fetch) |
| `/phase-7-closeout` | Phase7CloseoutPage | DEAD | none (placeholder shell: fixed "Stable production shell." text, no data fetch) |
| `/workflow-console` | WorkflowConsolePage | DEAD | none (placeholder shell: fixed "Stable production shell." text, no data fetch) |
| `/operational-actions` | OperationalActionsPage | SEEDED-OR-DEMO | SeededWorkflowHarness runs the scripted seeded workflow (api/seededWorkflow.ts, api/workflowSeed.ts) |
| `/decision-intelligence` | DecisionIntelligencePage | DEAD | none (placeholder shell: fixed "Stable production shell." text, no data fetch) |
| `/knowledge-graph-hub` | KnowledgeGraphHubPage | DEAD | none (placeholder shell: fixed "Stable production shell." text, no data fetch) |
| `/release-readiness-hub` | ReleaseReadinessHubPage | DEAD | none (placeholder shell: fixed "Stable production shell." text, no data fetch) |
| `/system-readiness-map` | SystemReadinessMapPage | DEAD | none (placeholder shell: fixed "Stable production shell." text, no data fetch) |
| `/platform-atlas` | PlatformAtlasPage | DEAD | none (placeholder shell: fixed "Stable production shell." text, no data fetch) |
| `/app-surface-catalog` | AppSurfaceCatalogPage | DEAD | none (placeholder shell: fixed "Stable production shell." text, no data fetch) |
| `/ui-completion-center` | UICompletionCenterPage | DEAD | none (placeholder shell: fixed "Stable production shell." text, no data fetch) |
| `/navigation-index` | NavigationIndexPage | DEAD | none (placeholder shell: fixed "Stable production shell." text, no data fetch) |
| `/frontend-closure` | FrontendClosurePage | DEAD | none (placeholder shell: fixed "Stable production shell." text, no data fetch) |
| `/workspace-map` | WorkspaceMapPage | DEAD | none (placeholder shell: fixed "Stable production shell." text, no data fetch) |
| `/route-registry` | RouteRegistryPage | DEAD | none (placeholder shell: fixed "Stable production shell." text, no data fetch) |
| `/operator-cockpit` | OperatorCockpitPage | DEAD | none (placeholder shell: fixed "Stable production shell." text, no data fetch) |
| `/final-control-center` | FinalControlCenterPage | DEAD | none (placeholder shell: fixed "Stable production shell." text, no data fetch) |
| `/executive-readout-workspace` | ExecutiveReadoutWorkspacePage | DEAD | none (placeholder shell: fixed "Stable production shell." text, no data fetch) |
| `/operational-portfolio` | OperationalPortfolioPage | DEAD | none (placeholder shell: fixed "Stable production shell." text, no data fetch) |
| `/ops-finalization-workspace` | OpsFinalizationWorkspacePage | DEAD | none (placeholder shell: fixed "Stable production shell." text, no data fetch) |
| `/delivery-closeout` | DeliveryCloseoutWorkspacePage | DEAD | none (placeholder shell: fixed "Stable production shell." text, no data fetch) |
| `/ops-coordination-center` | OpsCoordinationCenterPage | DEAD | none (placeholder shell: fixed "Stable production shell." text, no data fetch) |
| `/publication-governance` | PublicationGovernanceWorkspacePage | DEAD | none (placeholder shell: fixed "Stable production shell." text, no data fetch) |
| `/readiness-radar-workspace` | ReadinessRadarWorkspacePage | DEAD | none (placeholder shell: fixed "Stable production shell." text, no data fetch) |
| `/evidence-flow-studio` | EvidenceFlowStudioPage | DEAD | none (placeholder shell: fixed "Stable production shell." text, no data fetch) |
| `/operational-handoff` | OperationalHandoffPage | DEAD | none (placeholder shell: fixed "Stable production shell." text, no data fetch) |
| `/decision-queue` | DecisionQueuePage | DEAD | none (placeholder shell: fixed "Stable production shell." text, no data fetch) |
| `/intelligence` | IntelligenceHubPage | DEAD | none (placeholder shell: fixed "Stable production shell." text, no data fetch) |
| `/agents` | AgentRunsMonitorPage | DEAD | none (placeholder shell: fixed "Stable production shell." text, no data fetch) |
| `/operations` | OperationsHubPage | DEAD | none (placeholder shell: fixed "Stable production shell." text, no data fetch) |
| `/navigator` | InvestigationNavigatorPage | DEAD | none (placeholder shell: fixed "Stable production shell." text, no data fetch) |
| `/operations-intelligence` | OperationsIntelligencePage | DEAD | none (placeholder shell: fixed "Stable production shell." text, no data fetch) |
| `/investigation-navigator` | InvestigationNavigatorPage | DEAD | none (placeholder shell: fixed "Stable production shell." text, no data fetch) |
| `/analytics-center` | AnalyticsCenterPage | DEAD | none (placeholder shell: fixed "Stable production shell." text, no data fetch) |
| `/executive-overview` | ExecutiveOverviewPage | DEAD | none (placeholder shell: fixed "Stable production shell." text, no data fetch) |
| `/delivery-control-tower` | DeliveryControlTowerPage | DEAD | none (placeholder shell: fixed "Stable production shell." text, no data fetch) |
| `/workstream-board` | WorkstreamBoardPage | DEAD | none (placeholder shell: fixed "Stable production shell." text, no data fetch) |
| `/escalation-center` | EscalationCenterPage | DEAD | none (placeholder shell: fixed "Stable production shell." text, no data fetch) |
| `/agent-runs-board` | AgentRunsBoardPage | DEAD | none (placeholder shell: fixed "Stable production shell." text, no data fetch) |
| `/case-flow-map` | CaseFlowMapPage | REAL | /api/v1/cases/status-counts |
| `/quality-radar` | QualityRadarPage | DEAD | none (placeholder shell: fixed "Stable production shell." text, no data fetch) |
| `/persons` | PersonsPage | DEAD | none (placeholder shell: fixed "Stable production shell." text, no data fetch) |
| `/persons/new` | CreatePersonPage | DEAD | none (placeholder shell: fixed "Stable production shell." text, no data fetch) |
| `/persons/:id` | PersonDetailPage | DEAD | none (placeholder shell: fixed "Stable production shell." text, no data fetch) |
| `/cases` | CasesPage | REAL | /api/v1/cases |
| `/cases/new` | CreateCasePage | DEAD | none (placeholder shell: fixed "Stable production shell." text, no data fetch) |
| `/cases/:id` | CaseDetailPage | REAL | /api/v1/cases/{id} |
| `/case-explorer` | CaseExplorerPage | REAL | /api/v1/cases |
| `/case-explorer/:id` | CaseWorkbenchPage | DEAD | none (placeholder shell: fixed "Stable production shell." text, no data fetch) |
| `/truth-review-studio` | TruthReviewStudioPage | DEAD | none (placeholder shell: fixed "Stable production shell." text, no data fetch) |
| `/global-search-workspace` | UnifiedSearchWorkspacePage | DEAD | none (placeholder shell: fixed "Stable production shell." text, no data fetch) |
| `/source-intelligence` | SourceIntelligencePage | DEAD | none (placeholder shell: fixed "Stable production shell." text, no data fetch) |
| `/entity-graph` | EntityGraphWorkspacePage | DEAD | none (placeholder shell: fixed "Stable production shell." text, no data fetch) |
| `/review-audit` | ReviewAuditWorkspacePage | DEAD | none (placeholder shell: fixed "Stable production shell." text, no data fetch) |
| `/review-decision-board` | ReviewDecisionBoardPage | DEAD | none (placeholder shell: fixed "Stable production shell." text, no data fetch) |
| `/publication-pipeline` | PublicationPipelinePage | DEAD | none (placeholder shell: fixed "Stable production shell." text, no data fetch) |
| `/evidence-trace` | EvidenceTracePage | DEAD | none (placeholder shell: fixed "Stable production shell." text, no data fetch) |
| `/narrative-builder` | NarrativeBuilderPage | DEAD | none (placeholder shell: fixed "Stable production shell." text, no data fetch) |
| `/contradiction-resolution/:id` | ContradictionResolutionWorkspacePage | DEAD | none (placeholder shell: fixed "Stable production shell." text, no data fetch) |
| `/case-scoreboard` | CaseScoreboardPage | REAL | /api/v1/cases |
| `/reviews` | ReviewsPage | DEAD | none (placeholder shell: fixed "Stable production shell." text, no data fetch) |
| `/review-queue` | ReviewQueuePage | DEAD | none (placeholder shell: fixed "Stable production shell." text, no data fetch) |
| `/review-workspace` | ReviewWorkspacePage | DEAD | none (placeholder shell: fixed "Stable production shell." text, no data fetch) |
| `/publication-desk` | PublicationDeskPage | DEAD | none (placeholder shell: fixed "Stable production shell." text, no data fetch) |
| `/governance-console` | GovernanceConsolePage | DEAD | none (placeholder shell: fixed "Stable production shell." text, no data fetch) |
| `/publication-readiness` | PublicationReadinessBoardPage | DEAD | none (placeholder shell: fixed "Stable production shell." text, no data fetch) |
| `/decision-log` | DecisionLogPage | DEAD | none (placeholder shell: fixed "Stable production shell." text, no data fetch) |
| `/ingestion` | IngestionWorkspacePage | DEAD | none (placeholder shell: fixed "Stable production shell." text, no data fetch) |
| `/sources` | SourcesPage | DEAD | none (placeholder shell: fixed "Stable production shell." text, no data fetch) |
| `/sources/new` | CreateSourcePage | DEAD | none (placeholder shell: fixed "Stable production shell." text, no data fetch) |
| `/sources/:id` | SourceDetailPage | DEAD | none (placeholder shell: fixed "Stable production shell." text, no data fetch) |
| `/documents` | DocumentsPage | DEAD | none (placeholder shell: fixed "Stable production shell." text, no data fetch) |
| `/documents/new` | CreateDocumentPage | DEAD | none (placeholder shell: fixed "Stable production shell." text, no data fetch) |
| `/documents/:id` | DocumentDetailPage | DEAD | none (placeholder shell: fixed "Stable production shell." text, no data fetch) |
| `/evidence` | EvidencePage | DEAD | none (placeholder shell: fixed "Stable production shell." text, no data fetch) |
| `/evidence/new` | CreateEvidencePage | DEAD | none (placeholder shell: fixed "Stable production shell." text, no data fetch) |
| `/evidence/:id` | EvidenceDetailPage | DEAD | none (placeholder shell: fixed "Stable production shell." text, no data fetch) |
| `/statements` | StatementsPage | DEAD | none (placeholder shell: fixed "Stable production shell." text, no data fetch) |
| `/statements/new` | CreateStatementPage | DEAD | none (placeholder shell: fixed "Stable production shell." text, no data fetch) |
| `/statements/:id` | StatementDetailPage | DEAD | none (placeholder shell: fixed "Stable production shell." text, no data fetch) |
| `/claims` | ClaimsPage | REAL | /api/v1/claims |
| `/claims/:id` | ClaimDetailPage | REAL | /api/v1/claims/{id} |
| `/claims/workspace` | ClaimsWorkspacePage | DEAD | none (placeholder shell: fixed "Stable production shell." text, no data fetch) |
| `/contradictions` | ContradictionsPage | REAL | /api/v1/contradictions |
| `/contradictions/:id` | ContradictionDetailPage | REAL | /api/v1/contradictions/{id} |
| `/contradictions/workspace` | ContradictionsWorkspacePage | DEAD | none (placeholder shell: fixed "Stable production shell." text, no data fetch) |
| `/resolution-board` | ResolutionBoardPage | DEAD | none (placeholder shell: fixed "Stable production shell." text, no data fetch) |

## Decisions

- `/dashboard` is REAL for cases, claims, contradictions and workflow audit; its scenario Snapshot metric and panel are seeded content, so `DashboardPage` neither fetches nor renders them outside Development.
- Removed the unused Vite starter `src/App.tsx`, `src/App.css`, and `src/assets/{hero.png,react.svg,vite.svg}`; a repo-wide search found no reference outside `App.tsx`. The legacy root `frontend/` is untouched.
- DEAD and SEEDED-OR-DEMO pages stay in source (behind the Development-only mount) so the follow-up UI slice (#161) can replace or delete them one at a time with an audit row each; navigation shows only mounted routes.
- `/case-explorer`, `/case-scoreboard`: listed REAL because they call `/api/v1/cases`; neither reads seed modules (checked by search).
