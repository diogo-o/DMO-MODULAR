# DMO.Web

Application host: startup/composition root, HTTP pipeline, configuration binding,
dependency registration, the technical health/startup surface, the runtime
authentication/account boundary, the ADMIN-only administration surfaces, the account-aware
root router and the accepted shared frontend shell.

It owns no industrial business rule.

## Contents

| Concern | File |
| --- | --- |
| Composition root | `Program.cs` |
| Technical endpoints | `Endpoints/Core/TechnicalEndpoints.cs` |
| Authentication + current-account endpoints | `Endpoints/Access/AuthEndpoints.cs` |
| ADMIN-only Template/USER administration endpoints | `Endpoints/Administration/{TemplateAdministrationEndpoints,UserAdministrationEndpoints}.cs` |
| Job On + contextual Ferramentas endpoints | `Endpoints/ToolJobOn/{JobOnEndpoints,FerramentasEndpoints}.cs` |
| Controlo endpoints | `Endpoints/Controlo/{ControloCreateEndpoints,ControloDefinicoesEndpoints,ControloApproveEndpoints}.cs` |
| Boquilhas endpoints | `Endpoints/Boquilhas/{BoquilhasEndpoints,BoquilhasDefinicoesEndpoints}.cs` |
| Peso PDF / documents endpoints | `Endpoints/Documents/DocumentsEndpoints.cs` |
| Technical startup commands (migrate / bootstrap-admin / run) | `Startup/StartupCommands.cs` |
| Supabase Auth configuration (DEV/TEST) | `Auth/SupabaseOptions.cs`, `Auth/SupabaseAdminOptions.cs` |
| Production authentication boundary | `Auth/SupabaseAuthenticationService.cs` |
| Runtime session element (cookie scheme) | `Auth/SessionAuthentication.cs` |
| Real current-account context | `Auth/CurrentAccountContext.cs` |
| Shared frontend registration seam (A2/A3) | `Frontend/Shared/SharedFrontendExtensions.cs` |
| Shared presentation contracts (A3 P2-T01) | `Frontend/Shared/Contracts/` |
| Shared component partials (A3 P2-T01) | `Pages/Shared/Components/` |
| Shared shell presentation | `Frontend/Shell/ShellPresentationService.cs`, `ShellPresentationModels.cs` |
| Navigation projection + route seam | `Frontend/Shell/NavigationProjectionService.cs`, `DestinationRoutes.cs` |
| Account-aware root routing / landing | `Pages/Index.cshtml.cs`, `Navigation/UserLandingService.cs`, `Navigation/LandingSelector.cs` |
| No-access USER surface | `Pages/AccessDenied.cshtml(.cs)` |
| ADMIN-only Template administration | `Pages/Administration/Templates/` |
| ADMIN-only USER administration | `Pages/Administration/Users/` |
| Module authorization gate | `Authorization/ModuleAuthorization*.cs` |

## Technical endpoint

```text
GET /health  ->  {"status":"ok","environment":"<host environment>"}
```

Startup/liveness only. It reads no product data and does not touch the database.

## Authentication + current-account surface

```text
POST /auth/login    ADMIN: {"email", "password"}          -> 200 (session) | 401 | 502 | 503 | 403 (fail closed)
                    USER:  {"companyNumber", "password"}  -> 200 (session) | 401 | 502 | 503 | 403 (fail closed)
POST /auth/logout   clears only the runtime session state
GET  /auth/me       {"accountType": "admin"|"user"|"none", ...} — read-only, never grants access
```

- ADMIN authentication is **real Supabase Auth** against the DEV/TEST project
  (`jixnteypqqrltsxgwzpv`). The **publishable key** travels only in the `apikey` header;
  no `service_role` and no secret key is used. This project is DEV/TEST only — the future
  production backend is a separate clean Supabase project.
- A session is established only when authentication succeeds **and** account resolution
  returns the active ADMIN/USER.
- USER login contract is `company_number + password`; the persisted carrier email is resolved
  through the narrow lookup and the same Supabase password grant is used.
- Provider identity is internal linkage only. Provider claims/roles never classify access.

## Routing

```text
GET /           ADMIN  -> 302 /Administration
                USER   -> 302 valid landing route, or 302 /AccessDenied when resolution fails closed / no routable destination
                none   -> 302 /Login
GET /AccessDenied  ADMIN -> 302 /Administration; USER -> 403 generic no-access in the shared shell
```

Invalid explicit persisted landing fails closed to `/AccessDenied`; it never falls back to the
first valid destination.

## Configuration

| Key | Environment variable | Purpose |
| --- | --- | --- |
| `Database:ConnectionString` | `Database__ConnectionString` | PostgreSQL connection string |
| `Supabase:ProjectUrl` | `Supabase__ProjectUrl` | DEV/TEST Supabase project URL |
| `Supabase:PublishableKey` | `Supabase__PublishableKey` | Supabase publishable key (ADMIN/USER Auth) |
| `SupabaseAdmin:ServiceRoleKey` | `SupabaseAdmin__ServiceRoleKey` | service-role secret (ADMIN-only USER administration; server-only) |

There are **no defaults** and no committed credentials. Missing or invalid values fail
startup loudly (`DatabaseConfigurationException` / `SupabaseConfigurationException`); the
host never substitutes a production-looking default and never degrades to an
unauthenticated mode.

Local development uses user-secrets (already initialised for this project):

```pwsh
dotnet user-secrets set "Database:ConnectionString" "<postgres connection string>" --project src/DMO.Web
dotnet user-secrets set "Supabase:ProjectUrl" "https://jixnteypqqrltsxgwzpv.supabase.co" --project src/DMO.Web
dotnet user-secrets set "Supabase:PublishableKey" "<publishable key>" --project src/DMO.Web
```

The ignored `*.env` file is **never read** by the application; no `.env` loader is added.
It is a developer-side scratch file whose values are copied into user-secrets or exported
to environment variables.

## Running

```pwsh
dotnet restore
dotnet build
dotnet run --project src/DMO.Web                            # start the host
dotnet run --project src/DMO.Web -- migrate                 # apply pending migrations and exit
dotnet run --project src/DMO.Web -- bootstrap-admin         # deployment-only single-ADMIN bootstrap
```

## Shared frontend notes

- `Frontend/Shared/Contracts/` (P2-T01/A3) holds the frozen presentation vocabulary: the ten
  common states, `RecordStatus`, `AvailabilityState` and the generic action carrier. These are
  presentation-only and domain-neutral; they never decide access or persist anything.
- `wwwroot/css/dmo-components.css` is the A-owned component stylesheet (new file; existing
  selectors are not rewritten).
- Build availability stays honest: `ModuleRegistrations.CurrentBuildAvailable` is empty, so no
  operational destination is advertised until its real surface and route exist.

## Cross-cutting boundaries

Run as a host process only. Module Registry, navigation composition, Template/Module access
resolution, the shared shell, the ADMIN-only administration surfaces, the P2-T01 shared
component layer, and the domain endpoint groups (Core/Access/Administration/ToolJobOn/Controlo/
Boquilhas/Documents) are present. Operational Modules are implemented but are **not** registered
"available" yet — `ModuleRegistrations.CurrentBuildAvailable` stays `[]` until P2-T10 registers
their real surfaces and routes.