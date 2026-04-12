# CORS Fix

Applied:
- added ASP.NET Core CORS policy named FrontendDev
- allowed Vite dev origins on localhost:5173 and 127.0.0.1:5173
- inserted app.UseCors("FrontendDev") before auth and controller mapping

After running:
1. rebuild backend
2. restart backend
3. restart frontend
4. refresh browser