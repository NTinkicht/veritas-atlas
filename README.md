# Veritas Atlas

AI-powered public record intelligence platform focused on evidence ingestion,
contradiction detection, confidence scoring, governance, and publication safety.

## Canonical repository structure

- `apps/api/VeritasAtlas.Api` - ASP.NET Core API
- `apps/api/VeritasAtlas.Application` - application contracts and use cases
- `apps/api/VeritasAtlas.Domain` - domain model
- `apps/api/VeritasAtlas.Infrastructure` - EF Core/PostgreSQL persistence and services
- `apps/web/veritas-atlas-web` - canonical React/TypeScript/Vite frontend
- `tests/VeritasAtlas.Api.Tests` - automated .NET tests
- `tests/smoke` - local/API smoke-test assets
- `_diagnostics` - historical diagnostic evidence; not current runtime authority

The root `frontend/` directory is a partial legacy source tree. It is not the
canonical runnable frontend because it has no package manifest. Do not add new
product work there. Removal or archival should happen only in a separately
reviewed cleanup change after any still-useful source is reconciled.

## Development configuration

Local development settings live in
`apps/api/VeritasAtlas.Api/appsettings.Development.json`.

Outside the Development environment, the API fails closed unless a database
connection string and `Auth:Jwt:Secret` are supplied through external runtime
configuration. Development login users and auth-diagnostic endpoints are not
available outside Development.

Cross-origin access outside Development is denied unless
`Cors:AllowedOrigins` is explicitly configured.

## Health

The API exposes:

- `/health`
- `/health/live`

These endpoints are intended for repository/runtime readiness checks and do not
grant deployment or publication authority.
