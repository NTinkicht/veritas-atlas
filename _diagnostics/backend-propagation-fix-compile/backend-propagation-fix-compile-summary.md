# Backend Propagation Fix Compile Patch

Applied:
- removed AuthRequestContext dependency from WorkflowTransitionService
- kept case status propagation into scenario snapshot
- kept workflow audit creation for case transitions
- uses role "admin" for these transition audit rows to restore compilation quickly