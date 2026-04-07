# Phase 12.2 Auth Alignment Fix

Applied:
- aligned Program.cs JWT validation config with JwtTokenService
- middleware now reads:
  - Auth:Jwt:Secret
  - Auth:Jwt:Issuer
  - Auth:Jwt:Audience
- aligned fallback values with token issuer defaults
- corrected the runtime auth smoke script login payload to use username/password

Expected:
- login succeeds
- /api/v1/auth/me succeeds with bearer token
- protected workflow endpoints stop returning 401