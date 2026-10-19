# BEAI Reality Bridge Migration Plan

## Decision

The active loopback QueryHost remains the one Reality Bridge. V0.1 adds a standard execution endpoint and a Local MCP adapter. The older Named Pipe adapter is reference material only and is not started as another bridge.

## KEEP

- `EmbeddedBridge` startup/shutdown integration in DevelopmentTools.Addin.
- ExternalEvent queue and Revit UI-thread boundary.
- Existing read-only endpoints during migration.
- Generic model, element and parameter readers.
- Existing Room Tile Ribbon and business modules.

## WRAP

- Wrap existing reader operations with `RealityRequest` and `RealityResponse`.
- Wrap the loopback endpoint with an AI-vendor-neutral MCP adapter.
- Wrap errors with stable BEAI error codes and keep full exceptions in logs.

## REFACTOR

- Move V0.1 operation selection into `RealityDispatcher`.
- Expose document, level, element query and parameter operations through canonical tool names.
- Add an external-access kill switch and feature flags.

## MOVE

- Treat the QueryHost source tree as the canonical bridge implementation.
- Package the built QueryHost DLL and Local MCP files with DevelopmentTools.

## ADD

- `/api/v1/execute` with versioned request and response envelopes.
- SYSTEM and READ operation catalog.
- Local MCP Server at `127.0.0.1:3000/mcp`.
- Local MCP test client, validation, timeout and payload limits.
- Architecture, security, testing and V0.1 reports.

## DEPRECATE

- Direct use of legacy endpoint-specific client logic. Endpoints remain available for compatibility.
- The inactive standalone `BEAI.RevitAdapter.2024` runtime.

## DELETE

Nothing is deleted in V0.1. Removing the inactive adapter or legacy endpoints requires a separate compatibility review after MCP acceptance.

## Milestones

1. Documentation and architecture decision.
2. Standard contracts and SYSTEM/READ dispatcher.
3. Local MCP adapter and automated tests.
4. Build and regression verification.
5. Real Revit 2024 and MCP Inspector verification.

