# TEAMMY API — Student Project Group Formation & Management

TEAMMY is a backend API that helps universities manage **student project groups** for coursework/capstone:
- Students can **create/join groups**, post recruitment, apply to teams.
- Mentors/Admins can **monitor progress** and **evaluate** groups.
- Teams get **Kanban boards**, **tasks**, **chat**, and file attachments.
- Enforced **business rule**: *one student can belong to at most one group per term*.

This repository implements the API using **.NET 8**, **Clean Architecture** (Domain–Application–Infrastructure–API), and **PostgreSQL**. Authentication defaults to **Google Sign-In (OpenID Connect)**; the frontend consumes JWTs issued by the API.

---

## Project Goals

1. **Fair & Transparent Grouping**
   - Guarantee one-member-one-group-per-term at the **database** level.
   - Clear recruiting flow: recruitment posts, invitations, applications.

2. **Productivity for Teams**
   - Board/Column model for customizable workflows (Kanban).
   - Tasks with assignees, labels, comments, attachments.
   - Real-time updates and notifications (design-ready).

3. **Mentorship & Evaluation**
   - Mentor/admin evaluations with rubric JSON and scores per milestone.
   - Reporting hooks (export/analytics) and non-screen batch jobs.

4. **Security & Reliability**
   - Google SSO by default; domain allow-list.
   - Measurable non-functional targets (availability, performance).
   - Backups & migrations; observability hooks.

---

## High-Level Architecture

```
Teammy/
├─ src/
│  ├─ Teammy.Domain/               # Entities, Value Objects, domain events
│  ├─ Teammy.Application/          # Use cases (CQRS), DTOs, validation
│  ├─ Teammy.Infrastructure/       # EF Core (PostgreSQL), OAuth/JWT, external services
│  └─ Teammy.Api/                  # Presentation (Controllers/Minimal APIs), DI, Swagger
```

**Key Technology**
- .NET 8, ASP.NET Core
- PostgreSQL 14+ (EF Core provider: Npgsql)
- Google OpenID Connect (id_token verification) → JWT for FE
- Optional: Redis (cache/queues), SignalR (real-time), object storage (files)

---

## Core Business Rules (Summary)

- **One user–one group per term** (active statuses: `pending`, `member`, `leader`). Enforced via **partial unique index** on `(user_id, term_id)`.
- A group has **exactly one leader** at a time.
- Recruitment post, invitations, applications are **scoped to a term**.
- Default auth is **Google**; only **verified** emails (optionally allow-listed by domain) can sign in.

---

## Run Locally (Development)

> This is a high-level guide. Commands/scripts may vary based on how your solution is organized.

1. **Requirements**
   - .NET 8 SDK
   - PostgreSQL 14+ (create an empty DB, e.g., `teammy`)
   - `dotnet-ef`: `dotnet tool update --global dotnet-ef`

2. **Configuration**
   - Set a connection string in `appsettings.Development.json` (in `Teammy.Api`):
     ```json
     {
       "ConnectionStrings": {
         "Default": "Host=localhost;Database=teammy;Username=postgres;Password=postgres"
       },
       "Jwt": {
         "Issuer": "teammy-api",
         "Audience": "teammy-client",
         "Key": "dev-only-change-me-please-very-long-secret"
       },
       "Auth": {
         "GoogleAllowedDomains": [ "student.university.edu" ]
       }
     }
     ```

3. **Database**
   - _Code-first path_: run EF migrations (if the repo includes them):
     ```bash
     dotnet ef database update --project src/Teammy.Infrastructure --startup-project src/Teammy.Api
     ```
   - _DB-first path_: scaffold entities from an existing database:
     ```bash
     dotnet ef dbcontext scaffold "Host=localhost;Database=teammy;Username=postgres;Password=postgres"        Npgsql.EntityFrameworkCore.PostgreSQL        --project src/Teammy.Infrastructure        --startup-project src/Teammy.Api        --context TeammyDbContext        --context-dir Persistence        --output-dir Persistence/Models        --schema teammy        --data-annotations        --no-onconfiguring
     ```

4. **Run API**
   ```bash
   dotnet run --project src/Teammy.Api
   ```
   - Swagger UI available at `https://localhost:xxxx/swagger`

---

## Authentication Flow (Default)

- Frontend obtains **Google ID Token** (One Tap / button).
- API verifies `id_token` (issuer, audience, signature, expiration).
- If `oauth_accounts(provider='google', provider_uid=sub)` exists → log in user.
- Else if `email_verified` and `users.email` exists → **link** OAuth to existing user.
- Else create a new user (role = `student`), link OAuth account.
- API returns **JWT** to the frontend. Admin/mentor roles are assigned by admins.

---

## Non-Functional Targets (Snapshot)

- Availability ≥ **99.5%** monthly (maintenance ≤ 2h/month).
- Auth callback ≤ **1.2s** (95p). Board load (≤500 tasks) ≤ **2.0s**.
- Backups: nightly full + 15-min WAL; **RPO ≤ 15m**, **RTO ≤ 2h**.
- Security: TLS 1.2+, domain allow-list, OWASP Top 10 mitigations.

---

## Environments & Secrets

- Keep secrets in environment variables or a secret manager (Azure/GCP/AWS/Vault).
- Do **not** commit secrets. Local development can use `dotnet user-secrets` or `.env` kept out of VCS.

---

## License

Internal academic project material for the TEAMMY system. Update this section as needed for your distribution model.
