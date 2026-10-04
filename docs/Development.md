# Development

## Prerequisites

- .NET 10 SDK (the packages target .NET 8; the sample and tests run on .NET 10)
- Node.js 22.22+ or 24.15+ (Angular 22)

## Layout

```
src/WorkflowCore.Dashboard/                 API, SignalR hub, journal, embedded UI host
src/WorkflowCore.Dashboard.EntityFramework/ EF Core journal
src/WorkflowCore.Dashboard.Designer/        Designer API
src/WorkflowCore.Dashboard.UI/              Angular app
samples/WorkflowCore.Dashboard.Sample/      Demo host on SQLite, port 5290
tests/WorkflowCore.Dashboard.Tests/         xUnit tests
docs/                                       Documentation, mirrored to the wiki
```

## Build and run

```bash
dotnet build                                   # builds the UI too when wwwroot/ is missing
dotnet run --project samples/WorkflowCore.Dashboard.Sample
```

- `-p:BuildDashboardUi=true` rebuilds the UI on every `dotnet build`.
- `-p:SkipDashboardUi=true` builds the .NET code without the UI.

## UI with hot reload

```bash
# terminal 1
dotnet run --project samples/WorkflowCore.Dashboard.Sample

# terminal 2
cd src/WorkflowCore.Dashboard.UI
npm start                 # http://localhost:4200/workflows, API proxied to :5290
```

`npm run build` writes the production UI into `src/WorkflowCore.Dashboard/wwwroot`, which the next `dotnet build` embeds.

## Tests

```bash
dotnet test
dotnet test -p:EfVersion=8.0.*
dotnet test -p:EfVersion=10.0.*
```

The EF journal is built against EF Core 8. EF Core 9 moved some APIs, so a call that compiles against 8 can fail at runtime on 9 or 10; CI runs the tests on all three. `JournalContractTests` run the same behaviour against both journals.

`SecurityTests` host the dashboard on an in-memory test server to check the CSRF, framing and local-request rules.

## Adding an endpoint

- Map it on the `api` group (or in an `IDashboardExtension`), so authorization and the change filter apply.
- Use GET only for reads; changes must be POST, PUT or DELETE.
- Return errors as `{ code, message }`.
- Send requests from the UI through `HttpClient`, which adds the `X-Wfc-Dashboard` header.

## Documentation

Edit the pages in `docs/`. On every push to `main`, the **Publish wiki** workflow copies them to the wiki, rewriting `Page.md` links to wiki links. Do not edit the wiki directly; it is overwritten.

## Releasing

1. Move the **Unreleased** entries in `CHANGELOG.md` under a new version heading, e.g. `## [0.2.0] - 2026-11-01`, and update the compare links.
2. Merge that change to `main`.
3. Tag and push: `git tag v0.2.0 && git push origin v0.2.0`.

The **Release** workflow runs the tests, packs the three packages with that version, and creates a GitHub release with the changelog section as notes and the `.nupkg` and `.snupkg` files attached. Tags with a hyphen (`v0.2.0-beta.1`) become prereleases.
