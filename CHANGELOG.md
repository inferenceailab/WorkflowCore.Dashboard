# Changelog

All notable changes to this project are documented here. The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and the project uses [Semantic Versioning](https://semver.org/spec/v2.0.0.html). Until 1.0, minor versions may contain breaking changes.

## [Unreleased]

## [0.1.0] - 2026-10-04

First release.

### Added

- **Dashboard** (`WorkflowCore.Dashboard`)
  - Embeddable Angular UI served from the assembly under a configurable path, with light and dark themes.
  - Overview with live lifecycle events over SignalR and totals for the last hour, day or week.
  - Definitions list and detail page with a flowchart and a step table, for C# and JSON/YAML workflows.
  - Instances list with filters, newest first with totals when a persistent journal is configured.
  - Instance page: flowchart colored by step state with the paths taken, step timeline, data, payloads, activity and stack traces.
  - Start, suspend, resume and terminate workflows; publish events.
  - Activity journal with an in-memory default and a background writer that stores each event once across nodes.
  - Instance index, so Redis, Cosmos DB and DynamoDB providers get an instance list too.
  - `DashboardOptions`: title, read-only mode, authorization, framing, journal retention, step events, backfill, listing source.
- **Persistent journal** (`WorkflowCore.Dashboard.EntityFramework`)
  - Activity history and instance index in any EF Core relational database, creating its own `WfcDashboard_` tables.
  - One-time backfill of existing instances, and hourly retention purge.
  - Built against EF Core 8; tested on EF Core 8, 9 and 10.
- **Designer** (`WorkflowCore.Dashboard.Designer`)
  - Visual editor for Workflow Core JSON/YAML definitions: toolbox, canvas, inspector, container branches, conditional connections, undo/redo.
  - Validation with Workflow Core's own loader, with errors attached to the faulty steps.
  - Publishing as new versions, registered at once and synced to other nodes; drafts; JSON/YAML import and export.

### Security

- Local requests only by default, with Host checks against DNS rebinding.
- Changes require the `X-Wfc-Dashboard` header and are refused from other sites (CSRF protection).
- `X-Frame-Options`, `Content-Security-Policy: frame-ancestors` and `X-Content-Type-Options` on every response.
- YAML imports are parsed as plain data; type tags are refused.
- The designer only accepts step and data types from its catalog unless `AllowAnyType` is set.

[Unreleased]: https://github.com/inferenceailab/WorkflowCore.Dashboard/compare/v0.1.0...HEAD
[0.1.0]: https://github.com/inferenceailab/WorkflowCore.Dashboard/releases/tag/v0.1.0
