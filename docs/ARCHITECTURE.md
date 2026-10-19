# BEAI Architecture — Collaboration Baseline

**Status:** documentation of current baseline; not a request to redesign existing code.

## Repository
- `DevelopmentTools.Addin/`: Revit ribbon, commands, UI, authorization and application integration.
- `DevelopmentTools.Modules/`: independent net48 / WPF module class libraries. The existing module README documents `PerspectiveMinimap`, `OpeningDetail`, `DrawingFactory` and `ProjectStandards`.
- `BEAI/RealityBridge/BEAI.QueryHost/`: existing centralized safe Revit bridge.
- `BEAI/MCP/`: existing AI/MCP client/server components; must not become a separate Revit bridge.
- `DevelopmentTools.Test/`: existing test project; coverage must be evaluated, not assumed.
- `DevelopmentTools.Updater/`, `release/`, `installer/`: controlled delivery boundary.

The `DevelopmentTools.Addin.csproj` references the existing modules through ProjectReference. The wrapper `DevelopmentTools.Addin/Commands/Cmd_IntegratedModules.cs` uses `CommandAccessGuard.EnsureAuthorized` before delegating to module commands. Keep this authorization path.

## Boundary
```text
Codex / Claude (developer tools) -> feature branch -> PR -> CI + human Revit checks -> main
AI clients -> existing BEAI/MCP -> existing RealityBridge -> ExternalEvent -> Revit API
DevelopmentTools.Modules -> approved wrapper/authorization -> Revit ribbon
main -> owner-controlled release -> installer/updater
```

A future agent may provide planning and structured input, but model creation, modification, analysis and verification must be performed by deterministic Revit API tooling under authorized security boundaries. Do not let arbitrary LLM outputs execute code.

## Environment & testing reality
- Existing projects target .NET Framework 4.8 and reference Revit 2024 DLLs from `C:\Program Files\Autodesk\Revit 2024\`. This is an environment dependency.
- Hosted CI must not assume Autodesk Revit is installed, and must not redistribute proprietary DLLs.
- CI in this rollout runs repository-policy/static checks. Build and Revit acceptance remain explicitly separate until a licensed Windows development/runner environment is validated.
- There is no automatic proof of Revit compatibility just because a PR has green static checks.

## Change control
Core changes require explicit PR disclosure and maintainer review: `DevelopmentTools.Addin/App.cs`, project/solution references, `DevelopmentTools.Addin/Core/`, `BEAI/RealityBridge/`, `BEAI/MCP/`, `DevelopmentTools.Updater/`, `installer/`, `release/` and any authentication/configuration/manifest logic.

See MODULE_CONTRACT.md for the requirements of adding a module.
