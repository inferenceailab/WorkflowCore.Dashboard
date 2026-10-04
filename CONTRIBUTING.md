# Contributing

Thanks for helping. This page covers how to propose changes and what a pull request needs.

## Before you start

- **Bugs**: search the [issues](https://github.com/inferenceailab/WorkflowCore.Dashboard/issues) first, then open one with the bug template.
- **Features**: open a feature request before writing a large change, so we can agree on the approach.
- **Security problems**: never in a public issue. See [SECURITY.md](SECURITY.md).
- **Questions**: see [SUPPORT.md](SUPPORT.md).

## Set up

You need the .NET 10 SDK and Node.js 22.22+ or 24.15+ (required by Angular 22).

```bash
git clone https://github.com/inferenceailab/WorkflowCore.Dashboard.git
cd WorkflowCore.Dashboard
dotnet build                     # also builds the Angular UI on the first run
dotnet test
dotnet run --project samples/WorkflowCore.Dashboard.Sample
```

The sample serves the dashboard at http://localhost:5290/workflows. For UI work with hot reload, see [docs/Development.md](docs/Development.md).

## Making a change

1. Fork the repository and branch from `main`.
2. Keep the change focused: one fix or feature per pull request.
3. Follow the style of the surrounding code. `.editorconfig` covers formatting; Angular code follows `src/WorkflowCore.Dashboard.UI/.prettierrc`.
4. Add or update tests in `tests/WorkflowCore.Dashboard.Tests` for behaviour changes.
5. Run the checks below.
6. Update the docs in `docs/` if behaviour or options change. They are published to the wiki automatically.
7. Add a line under **Unreleased** in [CHANGELOG.md](CHANGELOG.md) for anything a user would notice.

### Checks

```bash
dotnet build -c Release
dotnet test -c Release
dotnet test -c Release -p:EfVersion=8.0.*     # the EF journal must work on EF Core 8, 9 and 10
dotnet test -c Release -p:EfVersion=10.0.*
cd src/WorkflowCore.Dashboard.UI && npm run build
```

CI runs the same checks on every pull request; it must pass before merging.

## Pull requests

- Describe what changed and why, and how you tested it. The template has the checklist.
- Add screenshots for visible UI changes, in light and dark themes.
- `main` is protected: changes land through reviewed pull requests with passing CI.
- We squash-merge, so the pull request title becomes the commit message. Write it in the imperative ("Add retention option"), not as a description ("Added…").

## Design principles

These keep the project usable with every Workflow Core setup. Please keep them in mind:

- **Only Workflow Core's abstractions.** The core package must not depend on a specific persistence provider.
- **Optional extras are separate packages.** The core package must not need EF Core or the DSL package.
- **Secure by default.** New endpoints go through the dashboard's authorization filter; state-changing endpoints use POST, PUT or DELETE so the CSRF check applies.
- **Lowest supported versions.** Packages target .NET 8 and EF Core 8, and are tested on newer versions.

## Code of conduct

Everyone taking part follows the [code of conduct](CODE_OF_CONDUCT.md).
