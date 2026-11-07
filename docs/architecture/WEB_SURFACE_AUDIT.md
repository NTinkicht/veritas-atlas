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
| `/public/publications/:id` | PublicPublicationPage | REAL | /api/v1/public/publications/{id} (anonymous, credential-free, fail-closed) |
| `/evidence-search` | EvidenceSearchPage | REAL | /api/v1/tenant-evidence/search |
| `/workflow-audit` | WorkflowAuditPage | REAL | /api/v1/workflow-audit/entries/clear |
| `/persistence` | PersistenceConsolePage | SEEDED-OR-DEMO | /api/v1/persistence/snapshot/reset: scenario snapshot of the seeded workflow scenario |
| `/persistence-console` | PersistenceConsolePage | SEEDED-OR-DEMO | /api/v1/persistence/snapshot/reset: scenario snapshot of the seeded workflow scenario |
| `/global-search` | GlobalSearchPage | DEAD | none (placeholder shell) |
| `/observability` | ObservabilityDashboardPage | DEAD | none (placeholder shell) |
| `/admin` | AdminControlTowerPage | DEAD | none (placeholder shell) |
| `/auth-diagnostics` | AuthDiagnosticsPage | DEAD | none (placeholder shell) |
| `/phase-12-data-center` | Phase12DataCenterPage | DEAD | none (placeholder shell) |
| `/auth-me` | AuthMePage | DEAD | none (placeholder shell) |
| `/phase-11-auth-center` | Phase11AuthCenterPage | DEAD | none (placeholder shell) |
| `/role-policy` | RolePolicyPage | DEAD | none (placeholder shell) |
| `/audit-persistence` | AuditPersistencePage | DEAD | none (placeholder shell) |
| `/phase-10-hardening-center` | Phase10HardeningCenterPage | DEAD | none (placeholder shell) |
| `/phase-9-integrity-center` | Phase9IntegrityCenterPage | DEAD | none (placeholder shell) |
| `/seeded-lifecycle-runner` | SeededLifecycleRunnerPage | DEAD | none (placeholder shell) |
| `/workflow-validation` | WorkflowValidationPage | DEAD | none (placeholder shell) |
| `/integration-test-center` | IntegrationTestCenterPage | DEAD | none (placeholder shell) |
| `/lifecycle-seed` | LifecycleSeedPage | DEAD | none (placeholder shell) |
| `/workflow-diagnostics` | WorkflowDiagnosticsPage | DEAD | none (placeholder shell) |
| `/mutation-playground` | MutationPlaygroundPage | DEAD | none (placeholder shell) |
| `/phase-7-closeout` | Phase7CloseoutPage | DEAD | none (placeholder shell) |
| `/workflow-console` | WorkflowConsolePage | DEAD | none (placeholder shell) |
| `/operational-actions` | OperationalActionsPage | SEEDED-OR-DEMO | SeededWorkflowHarness runs the scripted seeded workflow (api/seededWorkflow.ts, api/workflowSeed.ts) |
| `/decision-intelligence` | DecisionIntelligencePage | DEAD | none (placeholder shell) |
| `/knowledge-graph-hub` | KnowledgeGraphHubPage | DEAD | none (placeholder shell) |
| `/release-readiness-hub` | ReleaseReadinessHubPage | DEAD | none (placeholder shell) |
| `/system-readiness-map` | SystemReadinessMapPage | DEAD | none (placeholder shell) |
| `/platform-atlas` | PlatformAtlasPage | DEAD | none (placeholder shell) |
| `/app-surface-catalog` | AppSurfaceCatalogPage | DEAD | none (placeholder shell) |
| `/ui-completion-center` | UICompletionCenterPage | DEAD | none (placeholder shell) |
| `/navigation-index` | NavigationIndexPage | DEAD | none (placeholder shell) |
| `/frontend-closure` | FrontendClosurePage | DEAD | none (placeholder shell) |
| `/workspace-map` | WorkspaceMapPage | DEAD | none (placeholder shell) |
| `/route-registry` | RouteRegistryPage | DEAD | none (placeholder shell) |
| `/operator-cockpit` | OperatorCockpitPage | DEAD | none (placeholder shell) |
| `/final-control-center` | FinalControlCenterPage | DEAD | none (placeholder shell) |
| `/executive-readout-workspace` | ExecutiveReadoutWorkspacePage | DEAD | none (placeholder shell) |
| `/operational-portfolio` | OperationalPortfolioPage | DEAD | none (placeholder shell) |
| `/ops-finalization-workspace` | OpsFinalizationWorkspacePage | DEAD | none (placeholder shell) |
| `/delivery-closeout` | DeliveryCloseoutWorkspacePage | DEAD | none (placeholder shell) |
| `/ops-coordination-center` | OpsCoordinationCenterPage | DEAD | none (placeholder shell) |
| `/publication-governance` | PublicationGovernanceWorkspacePage | DEAD | none (placeholder shell) |
| `/readiness-radar-workspace` | ReadinessRadarWorkspacePage | DEAD | none (placeholder shell) |
| `/evidence-flow-studio` | EvidenceFlowStudioPage | DEAD | none (placeholder shell) |
| `/operational-handoff` | OperationalHandoffPage | DEAD | none (placeholder shell) |
| `/decision-queue` | DecisionQueuePage | DEAD | none (placeholder shell) |
| `/intelligence` | IntelligenceHubPage | DEAD | none (placeholder shell) |
| `/agents` | AgentRunsMonitorPage | DEAD | none (placeholder shell) |
| `/operations` | OperationsHubPage | DEAD | none (placeholder shell) |
| `/navigator` | InvestigationNavigatorPage | DEAD | none (placeholder shell) |
| `/operations-intelligence` | OperationsIntelligencePage | DEAD | none (placeholder shell) |
| `/investigation-navigator` | InvestigationNavigatorPage | DEAD | none (placeholder shell) |
| `/analytics-center` | AnalyticsCenterPage | DEAD | none (placeholder shell) |
| `/executive-overview` | ExecutiveOverviewPage | DEAD | none (placeholder shell) |
| `/delivery-control-tower` | DeliveryControlTowerPage | DEAD | none (placeholder shell) |
| `/workstream-board` | WorkstreamBoardPage | DEAD | none (placeholder shell) |
| `/escalation-center` | EscalationCenterPage | DEAD | none (placeholder shell) |
| `/agent-runs-board` | AgentRunsBoardPage | DEAD | none (placeholder shell) |
| `/case-flow-map` | CaseFlowMapPage | REAL | /api/v1/cases/status-counts |
| `/quality-radar` | QualityRadarPage | DEAD | none (placeholder shell) |
| `/persons` | PersonsPage | DEAD | none (placeholder shell) |
| `/persons/new` | CreatePersonPage | DEAD | none (placeholder shell) |
| `/persons/:id` | PersonDetailPage | DEAD | none (placeholder shell) |
| `/cases` | CasesPage | REAL | /api/v1/cases |
| `/cases/new` | CreateCasePage | DEAD | none (placeholder shell) |
| `/cases/:id` | CaseDetailPage | REAL | /api/v1/cases/{id} |
| `/case-explorer` | CaseExplorerPage | REAL | /api/v1/cases |
| `/case-explorer/:id` | CaseWorkbenchPage | DEAD | none (placeholder shell) |
| `/truth-review-studio` | TruthReviewStudioPage | DEAD | none (placeholder shell) |
| `/global-search-workspace` | UnifiedSearchWorkspacePage | DEAD | none (placeholder shell) |
| `/source-intelligence` | SourceIntelligencePage | DEAD | none (placeholder shell) |
| `/entity-graph` | EntityGraphWorkspacePage | DEAD | none (placeholder shell) |
| `/review-audit` | ReviewAuditWorkspacePage | DEAD | none (placeholder shell) |
| `/review-decision-board` | ReviewDecisionBoardPage | DEAD | none (placeholder shell) |
| `/publication-pipeline` | PublicationPipelinePage | DEAD | none (placeholder shell) |
| `/evidence-trace` | EvidenceTracePage | DEAD | none (placeholder shell) |
| `/narrative-builder` | NarrativeBuilderPage | DEAD | none (placeholder shell) |
| `/contradiction-resolution/:id` | ContradictionResolutionWorkspacePage | DEAD | none (placeholder shell) |
| `/case-scoreboard` | CaseScoreboardPage | REAL | /api/v1/cases |
| `/reviews` | ReviewsPage | DEAD | none (placeholder shell) |
| `/review-queue` | ReviewQueuePage | DEAD | none (placeholder shell) |
| `/review-workspace` | ReviewWorkspacePage | DEAD | none (placeholder shell) |
| `/publication-desk` | PublicationDeskPage | DEAD | none (placeholder shell) |
| `/governance-console` | GovernanceConsolePage | DEAD | none (placeholder shell) |
| `/publication-readiness` | PublicationReadinessBoardPage | DEAD | none (placeholder shell) |
| `/decision-log` | DecisionLogPage | DEAD | none (placeholder shell) |
| `/ingestion` | IngestionWorkspacePage | DEAD | none (placeholder shell) |
| `/sources` | SourcesPage | DEAD | none (placeholder shell) |
| `/sources/new` | CreateSourcePage | DEAD | none (placeholder shell) |
| `/sources/:id` | SourceDetailPage | DEAD | none (placeholder shell) |
| `/documents` | DocumentsPage | DEAD | none (placeholder shell) |
| `/documents/new` | CreateDocumentPage | DEAD | none (placeholder shell) |
| `/documents/:id` | DocumentDetailPage | DEAD | none (placeholder shell) |
| `/evidence` | EvidencePage | DEAD | none (placeholder shell) |
| `/evidence/new` | CreateEvidencePage | DEAD | none (placeholder shell) |
| `/evidence/:id` | EvidenceDetailPage | DEAD | none (placeholder shell) |
| `/statements` | StatementsPage | DEAD | none (placeholder shell) |
| `/statements/new` | CreateStatementPage | DEAD | none (placeholder shell) |
| `/statements/:id` | StatementDetailPage | DEAD | none (placeholder shell) |
| `/claims` | ClaimsPage | REAL | /api/v1/claims |
| `/claims/:id` | ClaimDetailPage | REAL | /api/v1/claims/{id} |
| `/claims/workspace` | ClaimsWorkspacePage | DEAD | none (placeholder shell) |
| `/contradictions` | ContradictionsPage | REAL | /api/v1/contradictions |
| `/contradictions/:id` | ContradictionDetailPage | REAL | /api/v1/contradictions/{id} |
| `/contradictions/workspace` | ContradictionsWorkspacePage | DEAD | none (placeholder shell) |
| `/resolution-board` | ResolutionBoardPage | DEAD | none (placeholder shell) |

## Decisions

- `/dashboard` is REAL for cases, claims, contradictions and workflow audit; its scenario Snapshot metric and panel are seeded content, so `DashboardPage` neither fetches nor renders them outside Development.
- Removed the unused Vite starter `src/App.tsx`, `src/App.css`, and `src/assets/{hero.png,react.svg,vite.svg}`; a repo-wide search found no reference outside `App.tsx`. The legacy root `frontend/` is untouched.
- DEAD and SEEDED-OR-DEMO pages stay in source (behind the Development-only mount) so the follow-up UI slice (#161) can replace or delete them one at a time with an audit row each; navigation shows only mounted routes.
- `/evidence-search` (#161) is the analyst search and citation inspector; it reads only `/api/v1/tenant-evidence/search` and has no seeded fallback. The search hit carries no parser name/version, so the inspector says so rather than inventing one.
- `/case-explorer`, `/case-scoreboard`: listed REAL because they call `/api/v1/cases`; neither reads seed modules (checked by search).
