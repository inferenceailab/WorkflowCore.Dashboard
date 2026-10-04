# FAQ

## Setup

**The page says "The UI was not built".**
The assembly was built without the Angular UI. Install Node.js 22.22+ or 24.15+ and rebuild with `dotnet build -p:BuildDashboardUi=true`, or run `npm ci && npm run build` in `src/WorkflowCore.Dashboard.UI`. Packages from releases always include the UI.

**I get 403 when opening the dashboard from another machine.**
By default only local requests are allowed. Add sign-in with your [identity provider](Identity-Providers.md), or use your app's own; see [Security](Security.md#exposing-the-dashboard).

**After signing in, the page says "You don't have access".**
Your account has no dashboard role. The page lists the groups your identity provider sent; compare them with `AdminGroups` and `ViewerGroups`. See [Troubleshooting](Identity-Providers.md#troubleshooting).

**I get 403 locally behind a custom host name.**
The local-only default accepts IP addresses, `localhost` and the machine name in the `Host` header, to block DNS rebinding. Use one of those, or configure `Authorization`.

**My script gets `403 forbidden-origin`.**
Changes need the header `X-Wfc-Dashboard: 1`. See [REST API](REST-API.md).

**The dashboard does not show in my portal's iframe.**
Framing is blocked by default. Set `FrameAncestors`, e.g. `"'self' https://portal.example.com"`.

**The sample fails with "access permissions" on port 5080 or similar.**
Windows reserves some port ranges. Check them with `netsh interface ipv4 show excludedportrange protocol=tcp` and pick another port: `dotnet run --project samples/WorkflowCore.Dashboard.Sample --urls http://localhost:5290`.

## Instances and history

**"The persistence provider cannot list workflow instances."**
Redis, Cosmos DB and DynamoDB providers cannot list instances. Configure a persistent journal; see [Activity journal](Activity-Journal.md#instance-listing). Opening an instance by ID always works.

**The activity feed is empty after a restart.**
The default journal is in memory. Use `UseEntityFrameworkJournal` to keep history.

**Older instances are missing from the list.**
With the journal index, instances that existed before the first start are added once by the backfill. Providers that cannot list instances cannot be backfilled.

**The instance list is in a strange order.**
Without a persistent journal, the list comes from Workflow Core in storage order; each page is sorted newest first. A persistent journal gives true newest-first order.

**The database journal grows quickly.**
Set `JournalStepEvents = false` to store only workflow-level events, or lower `JournalRetention`.

## Designer

**Can I edit my C# workflows in the designer?**
No. C# workflows are compiled code. They have a read-only flowchart on the definition page. The designer edits JSON/YAML definitions.

**My step class is not in the toolbox.**
The catalog scans loaded assemblies that reference Workflow Core. If yours loads later, add it: `designer.StepAssemblies.Add(typeof(MyStep).Assembly)`.

**Validation says a type "is not offered by the designer".**
Only types in the catalog are accepted. Add the assembly as above, or set `AllowAnyType = true` if you trust everyone with dashboard access.

**I published, but another server does not know the new version.**
Nodes check every 30 seconds (`SyncInterval`) and must share the same journal database.

**Where are my designs stored?**
In the journal's `WfcDashboard_Metadata` table, or in memory without a persistent journal.
