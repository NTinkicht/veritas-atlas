# Workflow Total Fix

Applied:
- rewrote WorkflowTransitionService.cs to restore orchestrator-compatible signatures
- restored all methods required by WorkflowOrchestratorService
- case, claim, contradiction, and review transitions now compile cleanly
- case and contradiction transitions now update scenario snapshot through ScenarioPersistenceService
- seed lifecycle now creates case, claims, contradiction, and saves a snapshot
- WorkflowActionsController now uses WorkflowOrchestratorService instead of WorkflowTransitionService
- case actions should now write workflow audit entries through the orchestrator