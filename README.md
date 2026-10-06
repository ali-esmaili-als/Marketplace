# Marketplace

Secure marketplace platform built with Angular and ASP.NET Core.

## Authentication configuration

JWT signing is intentionally not stored in source control. Configure a secret of at least 32 characters through environment configuration before starting the API:

`Jwt__SigningKey`

The API exposes:
- `POST /api/auth/login`
- `POST /api/auth/refresh`
- `POST /api/auth/revoke`

Refresh tokens are stored only as SHA-256 hashes and are rotated on refresh.
