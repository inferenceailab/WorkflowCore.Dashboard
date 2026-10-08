# Agent instructions

Workflow Core Dashboard: an ASP.NET Core dashboard for [Workflow Core](https://github.com/danielgerlag/workflow-core), shipped as NuGet packages. These notes are for coding agents, such as FoundryLine's, working on Tickets in this repository. [CONTRIBUTING.md](CONTRIBUTING.md) is the full guide for people.

## Scope

- Backend and API changes only. The Angular UI in `src/WorkflowCore.Dashboard.UI` is not built for agent work and must not be changed.
- Keep a change to what the Ticket asks for. No refactoring beside it.

## Commands

The UI build is skipped with `-p:SkipDashboardUi=true`:

```bash
dotnet restore -p:SkipDashboardUi=true
dotnet build -c Release --no-restore -p:SkipDashboardUi=true
dotnet test -c Release --no-build -p:SkipDashboardUi=true
```

## Layout

| Path | What lives there |
|---|---|
| `src/WorkflowCore.Dashboard` | The core package: API endpoints, authorization, live updates |
| `src/WorkflowCore.Dashboard.EntityFramework` | The EF Core activity journal (optional package) |
| `src/WorkflowCore.Dashboard.Designer` | The workflow designer's catalog and validation (optional package) |
| `src/WorkflowCore.Dashboard.OpenIdConnect` | Sign-in with OpenID Connect providers (optional package) |
| `tests/WorkflowCore.Dashboard.Tests` | All tests (xUnit) |
| `samples/` | A sample host; not shipped |
| `docs/` | User documentation, published to the wiki |

## Conventions

- Add or change one test per acceptance criterion in `tests/WorkflowCore.Dashboard.Tests`, named as a sentence about the behaviour, for example `Viewers_can_read_while_only_admins_can_change`.
- Only Workflow Core's abstractions in the core package; it must not depend on EF Core, a persistence provider or the DSL package.
- Secure by default: new endpoints go through the dashboard's authorization filter, and state-changing endpoints use POST, PUT or DELETE so the CSRF check applies.
- Packages target .NET 8 and EF Core 8. Do not raise those, and add no new NuGet packages unless the Ticket asks for one.
- Update the page in `docs/` when an option or behaviour changes, and add a line under **Unreleased** in `CHANGELOG.md` for anything a user would notice.
- Do not change `.github/`, `.foundryline.yml` or this file.
