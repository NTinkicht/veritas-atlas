# Backend Propagation Fix Bundle

Applied:
- patched WorkflowTransitionService.cs
- every case workflow action now:
  - validates transition
  - updates case entity status
  - updates scenario snapshot caseStatus immediately when the active snapshot matches the case
  - writes a workflow audit entry for the case transition
  - saves all changes in the same transaction scope

Expected result:
- /api/v1/workflow-audit/entries shows SubmitCase / ApproveCase / PreparePublication / PublishCase / HoldCase / RejectCase
- /api/v1/persistence/snapshot reflects caseStatus immediately after each case action