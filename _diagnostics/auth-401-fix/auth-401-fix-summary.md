# Auth 401 Fix

Applied:
- reinforced token storage and auth header generation
- forced workflow audit and persistence clients to use auth
- hardened LoginPage to store token and reload to dashboard
- made Dashboard resilient when protected panels return 401

After running:
1. stop frontend dev server
2. clear browser localStorage for localhost:5173
3. start frontend again
4. log in again
5. open dashboard