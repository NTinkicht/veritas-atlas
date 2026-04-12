# Frontend Links and Fetch Fix

Applied:
- AppSurface now accepts optional links prop for legacy placeholder pages
- frontend API base URL moved to http://localhost:5091
- shared http client normalizes absolute and relative API paths

After running:
1. make sure backend is running on http://localhost:5091
2. fully stop the frontend dev server
3. start it again with npm run dev
4. refresh the browser