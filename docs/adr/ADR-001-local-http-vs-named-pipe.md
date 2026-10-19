# ADR-001: Local HTTP Versus Named Pipe

## Context

The active DevelopmentTools integration already hosts QueryHost on loopback HTTP and safely transfers Revit API work through ExternalEvent. A separate, inactive Named Pipe implementation also exists.

## Options

1. Replace the active bridge with the older Named Pipe implementation.
2. Run both implementations.
3. Standardize and wrap the active loopback implementation.

## Decision

Use option 3 for V0.1. Bind to `127.0.0.1`, remove wildcard CORS, expose one versioned execution route and place the Local MCP adapter above it.

## Reason

This preserves the already integrated and tested ExternalEvent path, minimizes changes to Revit startup, and avoids two competing bridge lifecycles.

## Tradeoffs

- Local processes can reach the HTTP endpoint, so the bridge must enforce its own whitelist and kill switch.
- A future remote transport must authenticate outside the Revit process.
- Named Pipe can be reconsidered only if a measured security or deployment requirement justifies migration.

