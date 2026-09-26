# DMO Modular

DMO Modular is the clean construction base for the new BA DMO application.

The objective is to build **one application and one runtime**, but as a **modular monolith**: each operational module owns its own workflow and persistence, while shared identities and relationships live in a common backend.

This repository starts intentionally small. The current priority is to deliver the modules that are needed at work now, without forcing the full Armazém + Job On architecture before those modules exist.

> **Terminology.** `Module` means an assignable product access unit (the access model).
> A development/construction unit is a **phase**. A .NET project is neither.
> See `docs/ARCHITECTURE.md`.

## Current status

The application is an implemented modular monolith (a real ASP.NET Core host, PostgreSQL via a
single EF Core context, Supabase Auth, Razor Pages + minimal APIs). An early construction
skeleton (P1-T01) was followed by the delivered workstream phases. The current physical layout
— including the domain-grouped `Controlo/{Pesos,Settings,Approve,Comparacao}` split, the
`Persistence/{Core,Access,ToolJobOn,Controlo,Boquilhas}` grouping, and the domain-mirrored test
folders — is described in **`docs/CURRENT_REPOSITORY_STRUCTURE.md`** (authoritative current
structure). Architecture rules live in `docs/ARCHITECTURE.md`.

Delivered operational modules (workstream phases P2-T04…P2-T07 + P2-T08 Peso-PDF slices):

- **Admin** — users, access templates, module access, administration surfaces (D2).
- **Tools + Job On** — canonical Tool identity, Job On occurrence contexts, contextual
  Ferramentas (D4).
- **Boquilhas** — operational module with persistent records (D6).
- **Controlo** — Peso Criar, Peso Aprovar, Definições, Comparação service, and Peso PDF
  output (D5).

Deferred (identities only, no code/routes): **Armazém**, **Pegamentos**, **Reparação Interna**,
**Reparação Programada View/Create**, **Tampões**, **História**.

## Construction strategy

Each phase completes one usable module. Later phases add capabilities and relationships
**around** the identities already created, rather than rebuilding the backend. See
`docs/CREATION_AND_ASSOCIATION_LOGIC.md` for the settled creation/association logic, and
`docs/CURRENT_REPOSITORY_STRUCTURE.md` for where each module actually lives.

## Core architectural idea

```text
DMO Runtime
│
├─ Runtime boundary (application-level mechanics only)
│   ├─ startup
│   ├─ authentication/session wiring        (later Phase 1 slice)
│   ├─ Module Registry                      (later Phase 1 slice)
│   ├─ access resolution / navigation       (later Phase 1 slice)
│   └─ shared infrastructure registration
│
├─ Functional areas (Modules, by phase)
│   ├─ Admin
│   ├─ Tools
│   ├─ Boquilhas
│   └─ Controlo
│
└─ Infrastructure
    ├─ Database
    ├─ Files
    └─ Pdf
```

The runtime should remain deliberately small. It does not need to understand every industrial workflow or every relationship in the system. Its responsibility is to start the application, establish the current user/session, discover enabled modules and provide shared infrastructure.

Business meaning belongs to modules and to persisted backend relationships.

For the physical project layout, see `docs/CURRENT_REPOSITORY_STRUCTURE.md` (current
structure) and `docs/ARCHITECTURE.md` (architecture rules).

## Shared backend identities

Some identities are intentionally shared because multiple modules need the same real object.

### Tool

`tool_id` remains the canonical identity of a Tool. It is not a Boquilhas-specific identity.

Examples:

```text
tool_id T100 → type BQ
tool_id T200 → type CM
```

Boquilhas can create/select a BQ Tool. Peso can create/select a CM Tool. Later, Armazém and Job On can use those exact same Tool identities rather than replacing them.

### Production context before full Job On

Before Job On exists, the application may persist the minimum real production context required by the current modules, for example:

```text
reference          5447T137
production_number  202601
```

That context can group records such as:

```text
5447T137 / 202601
├── Boquilhas records
├── Peso
├── Pegamentos
├── Resumo
└── generated documents
```

This is **not a fake Job On**.

A Job On reference/production may be recorded as identifying information where useful. Early
phases **may** create and reuse the real canonical identities — `tool_id`, a minimal
`jobon_id`, and the `cm_id` / `mf_id` / `bq_id` occurrence contexts — as soon as a real
current workflow needs them. They are the real identities defined by the Information Web,
not temporary adapter IDs. Later modules reuse and enrich those same identities instead of
replacing them.

What must not happen is inventing workflow fields or relationships merely to imitate a
future module. `docs/CREATION_AND_ASSOCIATION_LOGIC.md` is the current reference for this
logic.

## Module boundaries

A module owns its own workflow, rules and module-specific persistence.

Modules should not reach directly into another module's internal tables or implementation.

Preferred direction:

```text
Controlo
→ shared contract / context provider
→ backend
```

Not:

```text
Peso service
→ arbitrary Job On tables
→ arbitrary Armazém tables
→ arbitrary Boquilhas tables
```

Modules may meet through stable shared identities such as `tool_id`, user/access identities and production context, or through explicit contracts exposed for that purpose.

## Immediate module scope

### Admin

Initial reusable foundation:

- users;
- access templates;
- module access;
- authentication/session integration;
- module registry / navigation permissions.

Admin should remain minimal. Do not build settings for modules that do not yet exist.

### Boquilhas

Boquilhas should be able to operate without Armazém or Job On being implemented.

It can create/select the real BQ Tool identity, persist its own records and optionally associate those records with the current production context.

Later, Armazém can add physical positions/movements around the same `tool_id`, and Job On can add the formal `bq_id -> tool_id + jobon_id` context.

### Controlo

Initial scope:

- Peso Criar;
- Peso Aprovar;
- Pegamentos;
- Resumo;
- persistent history;
- PDF output;
- final document-directory convention.

Peso uses the real CM `tool_id`. Before Job On exists, its production context is stored directly as the minimum real context needed by this standalone version.

Later, the production-bound form can evolve to the final relation through `cm_id` without replacing the underlying Tool identity.

## Documents

Persisted records, generated PDFs and filesystem paths are different things.

```text
database record != generated PDF != file path
```

Use the planned final directory convention from the start so documents created during the standalone period do not need to be physically migrated later:

```text
<reference>/
└── <production-number>/
    ├── Peso_<reference>_<machine>.pdf
    ├── Pegamentos_<reference>_<machine>.pdf
    └── Resume_<reference>_<machine>.pdf
```

Example:

```text
5447T137/
└── 202601/
    ├── Peso_5447T137_B3.pdf
    ├── Pegamentos_5447T137_B3.pdf
    └── Resume_5447T137_B3.pdf
```

## Expansion model

The intended evolution is additive:

```text
Admin + Tools
      ↓
Boquilhas
      ↓
Controlo
      ↓
usable work version
      ↓
+ Armazém
      ↓
+ Job On
      ↓
+ remaining modules
```

Adding Armazém should add positions and physical movements around existing Tools.

Adding Job On should add the full production lifecycle and formal CM/MF/BQ occurrence contexts around existing Tools and production data.

The goal is to **add relationships and capabilities**, not rebuild the backend every time a new module appears.

## Rules for adapting dmo-master

The existing `diogo-o/dmo-master` remains the source material for the settled operational/domain model. It will be adapted to this construction model later.

When adapting it:

- preserve settled domain truth;
- separate module-owned data from shared identities;
- keep the runtime ignorant of module-specific industrial rules;
- do not create dependencies on modules that are not yet implemented;
- do not create fake future IDs or relationships;
- prefer real stable identities that can survive future expansion;
- use minimum relation, minimum snapshot and minimum documentation;
- let later modules enrich the graph instead of forcing the complete graph on day one.

## Current status

The application is a delivered modular monolith (workstream phases P2-T01…P2-T08, repartitioned
through Stages 1–4). The single runtime, the four `src/` projects, the two `tests/` projects,
the single `DmoDbContext`, and the centralized 10-migration stream are in place. See
`docs/CURRENT_REPOSITORY_STRUCTURE.md` for the authoritative physical layout.

Remaining open work (not implemented): P2-T10 module-availability/route registration.
Deferred modules (Armazém, Pegamentos, Reparação, Tampões, História) remain identities only.

The detailed domain contracts from `dmo-master` should be brought in deliberately, module by module, rather than copied wholesale into the runtime.
