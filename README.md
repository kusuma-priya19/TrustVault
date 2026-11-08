# TrustVault – Secure Document Sharing (starter MVP)

A local full-stack starter using ASP.NET Core 10, React/Vite, PostgreSQL and Docker.

## Prerequisites
- .NET 10 SDK
- Node.js 22.12+ (or 20.19+)
- Docker Desktop

## 1. Start PostgreSQL
From the project root:

```powershell
docker compose up -d
```

## 2. Configure backend secrets
Edit `backend/TrustVault.Api/appsettings.json` and replace the database password and JWT key. Use a unique random JWT key of at least 32 characters. Do not commit real secrets to GitHub.

## 3. Start the API
Open PowerShell window 1:

```powershell
cd backend/TrustVault.Api
dotnet restore
dotnet run --urls http://localhost:5000
```

The first run creates the database tables. Health check: http://localhost:5000/api/health

## 4. Create the first administrator
Public registration creates Viewer accounts only. Create the first admin through the database after the first API run:

```powershell
docker exec -it trustvault-db psql -U trustvault -d trustvault
```

Create a normal Viewer account first by calling the registration endpoint (for example, from PowerShell):

```powershell
$body = @{ name="TrustVault Admin"; email="admin@example.com"; password="Choose-A-Unique-Long-Password" } | ConvertTo-Json
Invoke-RestMethod -Method Post -Uri http://localhost:5000/api/auth/register -ContentType "application/json" -Body $body
```

Then promote that account to Admin using this SQL. Run it inside the `psql` prompt opened above:

```sql
UPDATE "Users" SET "Role"='Admin' WHERE "Email"='admin@example.com';
```

Type `\q` to exit `psql`. The example password above is only a placeholder; choose a unique password for your local test account.

## 5. Start frontend
Open PowerShell window 2:

```powershell
cd frontend
npm install
npm run dev
```

Open http://localhost:5173 and sign in with the administrator account.

## Current MVP functionality
- Login with JWT; public self-registration is restricted to Viewer.
- Admin can list users and view audit records.
- Owner/Admin can upload documents (10 MB limit and extension allowlist).
- Owners can share read/edit permissions with named users and revoke them through the API.
- Every document download/edit/delete/share decision is checked by the backend.
- Document upload/download/edit/delete and denied download attempts are audited.

## Important limitations before production
This is a learning MVP, not a production security certification. Before real deployment, add database migrations, refresh-token/session revocation, rate limiting, email verification, malware scanning, content-signature validation, stronger file quotas, encrypted object storage, audit-log tamper protection, pagination, robust error handling, backup/restore, HTTPS, CSRF protections appropriate to the chosen auth model, and comprehensive integration tests. Add a dedicated admin bootstrap command instead of manual SQL. Test every route with users of every role. Never store secrets in Git.

## API quick reference
- `POST /api/auth/register` – create Viewer account
- `POST /api/auth/login` – receive JWT
- `GET /api/me`
- `GET /api/users` – Admin
- `POST /api/users` – Admin creates Admin/Owner/Editor/Viewer
- `GET /api/documents`
- `POST /api/documents` – Owner/Admin upload multipart `file` and optional `category`
- `GET /api/documents/{id}/download`
- `PUT /api/documents/{id}` – update name/category if editable
- `DELETE /api/documents/{id}` – owner/Admin
- `POST /api/documents/{id}/permissions` – owner/Admin; JSON `{ "userId":"GUID", "canRead":true, "canEdit":false }`
- `DELETE /api/documents/{id}/permissions/{userId}` – owner/Admin
- `GET /api/audit` – Admin
