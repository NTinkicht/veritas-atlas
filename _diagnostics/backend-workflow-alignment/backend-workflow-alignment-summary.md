# Backend Workflow Alignment Bundle

Applied:
- aligned initial case state handling so Open can transition to InReview
- added canonical backend action endpoints:
  - POST /api/v1/actions/cases/{id}/submit
  - POST /api/v1/actions/cases/{id}/approve
  - POST /api/v1/actions/cases/{id}/reject
  - POST /api/v1/actions/cases/{id}/prepare
  - POST /api/v1/actions/cases/{id}/publish
  - POST /api/v1/actions/cases/{id}/hold
- aligned frontend workflowActions.ts to those canonical endpoints
- restored status-aware frontend actions for prepare publication / publish / hold