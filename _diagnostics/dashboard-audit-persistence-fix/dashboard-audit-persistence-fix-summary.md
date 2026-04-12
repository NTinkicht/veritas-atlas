# Dashboard, Workflow Audit, Persistence Fix

Applied:
- dashboard now safely handles audit responses that are not shaped as expected
- workflow audit page now safely handles undefined data and 401s
- added /persistence alias when persistence route exists under another path