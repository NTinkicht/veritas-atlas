# Status-Aware Workflow Hardening Bundle

Applied:
- workflow actions now use multi-endpoint fallback to avoid 404-only routing assumptions
- only valid actions are shown for the current case status
- destructive actions now require confirmation
- success and error toast feedback added
- automatic refresh after successful action
- case detail page passes case status into workflow action panel
- operational actions page now allows current status selection

Note:
- the browser console showed all current workflow action calls returning 404 on /api/v1/workflow/cases/... routes
- this bundle adds fallback candidates for common action route variants