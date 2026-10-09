# REST API

The UI uses a JSON API under `{prefix}/api` (for example `/workflows/api`). You can call it from scripts too. It is not versioned yet and may change between 0.x releases.

## Conventions

- Every endpoint goes through the dashboard's `Authorization` check, plus any conventions on the mapped group (`RequireAuthorization`).
- **Changes (POST, PUT, DELETE) must send the header `X-Wfc-Dashboard: 1`**, and are refused when `AllowActions` is `false`.
- JSON uses camelCase; times are UTC ISO 8601. Workflow and designer documents keep Workflow Core's own PascalCase names.
- Errors return `{ "code": "...", "message": "..." }` with a matching status code.

```bash
curl -X POST https://host/workflows/api/events \
  -H "X-Wfc-Dashboard: 1" -H "Content-Type: application/json" \
  -d '{ "eventName": "OrderShipped", "eventKey": "ORD-1042", "eventData": { "carrier": "UPS" } }'
```

## Dashboard

| Method and path | Purpose |
|---|---|
| `GET /config` | Title, whether this user may change things (`allowActions`), persistence provider, journal info, installed features, the signed-in `user` (`name`, `email`, `role`) and `signOutPath` |
| `GET /definitions` | Registered definitions (every version) |
| `GET /definitions/{id}/{version}` | One definition with its steps and default data |
| `GET /instances` | Instances. Query: `status`, `definitionId`, `createdFrom`, `createdTo`, `skip`, `take` |
| `GET /instances/{id}` | One instance with execution pointers and data |
| `POST /instances` | Start a workflow: `{ definitionId, version?, data?, reference? }` → `{ id }` |
| `POST /instances/{id}/suspend` | → `{ success }` |
| `POST /instances/{id}/resume` | → `{ success }` |
| `POST /instances/{id}/terminate` | → `{ success }` |
| `POST /events` | Publish an event: `{ eventName, eventKey, eventData?, effectiveDate? }` |
| `GET /activity` | Activity, newest first. Query: `instanceId`, `before`, `take` (max 1000), `steps=false` |
| `GET /activity/totals` | Event counts by type. Query: `hours` (default 24) → `{ since, counts }` |

`GET /instances` returns `{ items, skip, take, hasMore, total, source }`. `total` is `null` and `source` is `"provider"` when the list comes from Workflow Core's own listing; see [Activity journal](Activity-Journal.md#instance-listing). Providers that cannot list return `501` with code `listing-not-supported` when no journal index is available.

## Designer

Available when the designer is installed (`features` in `/config` contains `"designer"`). Paths are under `/designer`.

| Method and path | Purpose |
|---|---|
| `GET /designer/catalog` | Step types (with inputs and outputs) and data types the designer offers |
| `GET /designer/definitions` | Designs: ID, description, draft flag, latest version |
| `GET /designer/definitions/{id}` | One design: draft and every published version, with layouts |
| `PUT /designer/definitions/{id}/draft` | Save a draft: `{ source, layout }` |
| `DELETE /designer/definitions/{id}/draft` | Discard the draft; a never-published design is deleted |
| `POST /designer/validate` | `{ source }` → `{ valid, issues: [{ severity, message, stepId }] }` |
| `POST /designer/definitions/{id}/publish` | `{ source, layout }` → `{ version, validation }`; `422` with the issues when invalid |
| `POST /designer/import` | `{ text }` (JSON or YAML) → `{ source }` in canonical JSON |

`source` is a Workflow Core DSL definition (`DefinitionSourceV1`) in JSON.

## Live updates

`{prefix}/hub` is a SignalR hub. It sends one message type, `activity`, with an activity entry:

```json
{
  "id": "4f011cff79f8b33595dfa7e8d33fefd6",
  "type": "WorkflowError",
  "time": "2026-10-04T09:17:22.91Z",
  "instanceId": "a1100e15-…",
  "definitionId": "ShipOrder",
  "version": 1,
  "reference": "ORD-1042",
  "executionPointerId": "…",
  "stepId": 2,
  "stepName": "Charge payment",
  "message": "Payment gateway timed out",
  "details": "System.TimeoutException: …"
}
```

An entry can be sent twice with the same `id`: the second time with `details` (the stack trace) filled in. Replace entries by `id`.

## Error codes

| Code | Status | Meaning |
|---|---|---|
| `forbidden-origin` | 403 | A change without the `X-Wfc-Dashboard` header, or from another site |
| `read-only` | 403 | `AllowActions` is `false` |
| `not-allowed` | 403 | `ActionAuthorization` refused this user a change (a viewer) |
| `not-found`, `definition-not-found`, `instance-not-found` | 404 | Unknown route, definition or instance |
| `invalid-json` | 400 | The body of `POST /instances` or `POST /events` is not valid JSON or a field has the wrong type; the message names the JSON path, such as `$.version` |
| `invalid-request`, `invalid-status`, `invalid-date`, `invalid-date-range`, `invalid-data`, `invalid-id`, `invalid-definition`, `too-large` | 400 | The request could not be used |
| `listing-not-supported` | 501 | The provider cannot list instances and there is no index |
