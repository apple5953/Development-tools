# BEAI Existing Architecture

## Current Architecture

```text
Room Tile / DevelopmentTools.Addin
  -> BEAI.QueryHost.EmbeddedBridge
  -> HttpListener (127.0.0.1:8765)
  -> BeaiQueryExternalEventHandler queue
  -> Autodesk Revit ExternalEvent
  -> QueryDispatcher
  -> GenericRevitReader
  -> Revit API
```

The repository also contains an older `BEAI.RevitAdapter.2024` Named Pipe implementation. It is not part of the active `BEAI.QueryBridge.sln` runtime and must not be started beside QueryHost as a second bridge.

## Current Data Flow

1. A loopback HTTP request reaches `RevitServerHost`.
2. The listener creates a `BeaiQueryRequest` and enqueues it.
3. `ExternalEvent.Raise()` schedules work on the Revit UI thread.
4. `QueryDispatcher` reads the active document through `GenericRevitReader`.
5. The result returns to the waiting HTTP request.

## Entry Points And Thread Model

- Bridge entry: `EmbeddedBridge.Start` from `DevelopmentTools.Addin.App.OnStartup`.
- Revit API entry: `BeaiQueryExternalEventHandler.Execute`.
- HTTP and future MCP work run outside the Revit API thread.
- Revit API calls run only inside the ExternalEvent handler.
- The queue processes at most ten requests per ExternalEvent callback.

## Existing Capabilities

- Status, active model, categories, element count and element search.
- Element summary and instance/type parameters.
- Parameter search and query-plan endpoints.
- Rule reload and worker restart/rollback development endpoints.
- Page size is capped at 500 by the current element reader.

## Existing Security

- The listener is bound to `127.0.0.1`.
- The query dispatcher rejects PUT, PATCH and DELETE.
- No Revit model transaction is opened by QueryHost.
- Current CORS is permissive and there is no caller authentication.
- The older Named Pipe implementation does not apply an explicit current-user ACL.

## Existing Config And Logs

- Rules are loaded from `Runtime/Rules`.
- Runtime logs are written under the add-in `Logs` directory with rotation at 10 MB.
- Runtime state records server, document, worker, rule generation and last error data.
- Configuration is currently fragmented and does not yet provide a single access kill switch.

## Reusable Components

- `EmbeddedBridge` lifecycle and failure isolation.
- `RevitServerHost` loopback listener.
- `BeaiQueryExternalEventHandler` and its concurrent queue.
- `QueryDispatcher` legacy endpoint compatibility.
- `GenericRevitReader` model, element and parameter extraction.
- `HotReloadManager`, `WorkerManager`, logging and status UI.

## Technical Debt And Risks

- Transport routes and Revit dispatch logic are coupled in `QueryDispatcher`.
- Responses do not share one request/response envelope.
- CORS allows every origin even though the service is local-only.
- Exceptions can leak implementation details in some error responses.
- No Local MCP Server or MCP integration test exists.
- Levels are not exposed by the active QueryHost generation.
- The inactive Named Pipe generation and active HTTP generation have drifted.
- There is no queue capacity limit, caller identity policy or complete cancellation propagation.

