# Activity journal

Workflow Core keeps the current state of each workflow but no history of what happened. The dashboard records Workflow Core's lifecycle events (workflow started, step started, step completed, error, suspended, resumed, completed, terminated) in an **activity journal**, and keeps an **instance index** built from them.

## In memory or in a database

| | In-memory (default) | Entity Framework |
|---|---|---|
| Package | `WorkflowCore.Dashboard` | `WorkflowCore.Dashboard.EntityFramework` |
| Survives restarts | No | Yes |
| Activity kept | Last 1000 entries (`ActivityCapacity`) | 30 days (`JournalRetention`) |
| Overview totals | Since the app started | Last hour, day or week |
| Instance list | Workflow Core's listing (storage order) | Newest first, with totals |
| Designer drafts and versions | Lost on restart | Kept |

```csharp
builder.Services.AddWorkflowCoreDashboard()
    .UseEntityFrameworkJournal(db => db.UseSqlServer(connectionString));
```

## Tables

The EF journal creates three tables on first use, also in a database that already has tables (such as Workflow Core's):

| Table | Holds |
|---|---|
| `WfcDashboard_Activity` | One row per lifecycle event, including error messages and stack traces |
| `WfcDashboard_Instances` | One row per workflow instance: definition, reference, status, times, error count |
| `WfcDashboard_Metadata` | Bookkeeping (backfill state) and designer documents |

Pass `schema:` to `UseEntityFrameworkJournal` to put them in their own schema. The account needs permission to create tables on first start, or create them with a DBA from the script EF Core generates.

The package targets EF Core 8 and is tested on EF Core 8, 9 and 10, so it runs on whatever EF Core version your app uses.

## How events are recorded

1. Workflow Core raises a lifecycle event.
2. The dashboard pushes it to open browsers immediately (SignalR).
3. A background writer stores it in the journal in batches (every 500 ms, up to 500 entries). Workflows never wait for the journal.

If the database is unavailable, writes are retried three times and then dropped with an error in the log; the buffer holds at most 50,000 entries.

Error events are joined with Workflow Core's exception, so the stack trace is stored with the message.

### Several nodes

Each event gets an ID derived from its content. With a distributed lifecycle event hub (Redis, RabbitMQ, Azure Service Bus…), every node receives every event and tries to store it; the journal stores it once.

## Instance listing

The instances page can be filled from two sources:

| `InstanceListing` | Source | Order | Total |
|---|---|---|---|
| `Provider` | Workflow Core's persistence provider | Storage order (sorted within each page) | No |
| `Journal` | The journal's instance index | Newest first | Yes |
| `Auto` (default) | `Journal` with a persistent journal, otherwise `Provider` | | |

Redis, Cosmos DB and DynamoDB providers cannot list instances. With them, `Auto` falls back to the index, which only knows instances started while the dashboard was running, unless the journal is persistent.

The index decides which instances appear and in what order; their current state always comes from Workflow Core. Instances that Workflow Core has deleted (for example by its purger) drop out of the index when the list notices them.

### Backfill

On the first start with a persistent journal, the dashboard adds instances that already exist to the index, 200 at a time, once. Providers that cannot list instances are skipped. Set `BackfillIndex = false` to turn it off.

## Retention and size

Activity older than `JournalRetention` (30 days) is deleted every hour. The instance index is kept.

Step events are most of the volume. A workflow with 10 steps writes about 22 rows per run. To keep the table small, set `JournalStepEvents = false`: step events are still shown live, but only workflow-level events are stored.
