# DMO.Domain

Domain primitives: value objects, entities and identities for the implemented domains. No
orchestration, no persistence, no HTTP — this project has no dependencies on the other projects'
implementation details.

## Contents (domain-grouped)

| Area | Location |
| --- | --- |
| Tool identity + types | `Tools/` (`Tool`, `ToolId`, `ToolType`, `ToolContext`, machine codes) |
| Job On occurrence | `JobOn/` (`JobOn`, `JobOnId`) |
| Controlo (Peso + Definições + Approve) | `Controlo/` — one folder holding all Controlo primitives: `Peso`, `PesoId`, `PesoMeasurementRow(Id)`, `PesoStatus`, `PesoReviewDecision*`; `Repairer`, `RepairerId`, `MachineRepairerAssignment`; `EmailList*`, `EmailTemplate*`, `EmailRecipient`, `EmailMachineGroup`, `GlassDensitySetting`, `PdfDirectorySettings` |
| Controlo — Comparação | `ControloComparacao/` (`Comparacao`, `ComparacaoId`, `ComparacaoCmSubject`, `ComparacaoCmDecisionKind`, `ComparacaoMeasurementRow`) |
| Boquilhas | `Boquilhas/` |

## Note on the Comparação placement

The Comparação domain lives in `DMO.Domain.ControloComparacao` — a **sibling** of
`DMO.Domain.Controlo`, not a child `Controlo/Comparacao` folder. This matches its production
namespace and was intentionally retained through the repartition (the Application-layer
`Controlo/Comparacao` naming is a separate, physically-mirrored layer — see
`docs/CURRENT_REPOSITORY_STRUCTURE.md`).

## Boundaries

Domain types are added only when a concrete task genuinely requires them. No infrastructure
detail, no DI, no persistence behaviour, and no future-module (Armazém/Reparação/Pegamentos)
pre-modelling.