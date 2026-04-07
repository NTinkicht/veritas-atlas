# Phase 12.1 Auth DI Fix Summary

Applied:
- restored DevUserStore registration
- restored AuthRequestContext registration
- restored JwtTokenService registration

Purpose:
- fix runtime 500 errors on auth and auth diagnostics endpoints caused by missing DI registrations