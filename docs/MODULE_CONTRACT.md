# BEAI Module Contract v0.1

This is an integration checklist, not a claim that all existing modules already satisfy a common runtime plugin interface. Do not invent or insert a new loader.

## Per-task boundaries
Each issue/PR must document: module name; owner; feature branch; purpose; directories allowed to change; input/output; Revit categories/parameters; exact ToolId; supported Revit versions; expected side effects; permissions; sample fixture; test instructions; rollback plan.

## Add a module
1. Inspect `DevelopmentTools.Modules/README.md`, current module `.csproj` files, and `DevelopmentTools.Addin/Commands/Cmd_IntegratedModules.cs`.
2. Prefer a separate `DevelopmentTools.Modules/<PascalCaseName>/` SDK-style .NET Framework 4.8 class library, with a distinct namespace.
3. Reuse common props and existing dependencies where compatible; do not duplicate Revit Bridge, authentication, ribbon bootstrap, or update subsystem.
4. Prepare a module README defining contracts, exposed command classes, permissions and manual Revit acceptance steps.
5. If a ribbon entry is required, propose an intentionally small integration change to the central wrapper and App.cs, with owner approval, and use `CommandAccessGuard.EnsureAuthorized` (or the approved equivalent) before execution. Do not bypass Google Sheets/Apps Script authorization.
6. Propose a unique ToolId following `DT_<ModuleName>`. Check the entire repo for collisions. Keep an explicit mapping to ribbon display name and authorization registration/config, subject to owner approval.
7. Any Revit API work triggered by modeless UI or external requests must reach the Revit UI thread through the approved ExternalEvent pattern. Respect Transaction rules and the read-only RealityBridge v0.1 boundary.
8. Keep computation independent of Revit API where feasible to allow unit tests without Revit.
9. UI/WPF and mixed-mode constraints must match existing net48 modules. Avoid writing machine-specific paths or secrets.
10. CI static pass + any independent tests are not Revit acceptance. Attach a real Revit test report for features that manipulate a model.

## Interface descriptions required per module
```yaml
module: ExampleModule
owner: github-username
tool_id: DT_ExampleModule
inputs: [validated request, model context]
outputs: [structured result, diagnostics]
side_effects: [none or specific Revit modifications]
authorized_calls: [approved existing bridge tools]
revit_versions: [2024]
tests:
  static: required
  unit: when feasible
  build: Windows environment required
  live_revit: manual signoff required
```

This YAML is a **documentation example**, not a runtime manifest supported by the current add-in.

## Protected interfaces
Do not change `App.cs`, solution/project references, `BEAI/RealityBridge`, licensing, updater, installer, or release tooling as an incidental edit. Propose such changes explicitly and isolate them for maintainer review. Preserve existing settings/contracts/data formats and release rollback compatibility.
