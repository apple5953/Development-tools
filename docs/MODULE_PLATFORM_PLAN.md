# Module platform plan

Status: design and proof of concept; no production loader switch.

The existing Reality Bridge remains the only external AI boundary. Its registered
read-only tools and ExternalEvent dispatch are unchanged. Ribbon commands continue
to pass through CommandAccessGuard before invoking module code.

## Migration gates

1. Freeze existing Ribbon names, authorization ToolIds, command classes, and DLL
   names as a compatibility baseline. Two PerspectiveMinimap buttons intentionally
   share one authorization ToolId.
2. Validate module manifests and generate build-time references and registration
   descriptors. The generated output must match the baseline before replacing any
   hand-written registration.
3. Build on a licensed Windows Revit 2024 API runner. Publish candidate artifacts
   from pull requests only; never invoke release.ps1 without NoPublish.
4. Test an isolated host using an approved manifest and hashes. Keep the current
   static loader in production until Revit 2024 acceptance is complete.

## Rollback

Keep the last verified full ZIP and manifest. On registration or startup failure,
disable the candidate module manifest and restart Revit. Restore the previous full
package through the existing installer/updater path. Before enabling module-only
updates, make rollback remove files introduced by the failed candidate, since the
current updater copies backups over the installation without deleting added files.

Revit 2025 and 2026 are unverified. No production credential or release changes.

## Current dependency map

`App.OnStartup` creates the existing ExternalEvents and invokes `RibbonCatalog.Build`.
`Cmd_IntegratedModules` wraps the four original module commands with
`CommandAccessGuard`; two PerspectiveMinimap buttons deliberately share one
authorization ToolId. `DevelopmentTools.Addin.csproj` references the four net48
modules and `BEAI.QueryHost`. The module directory's `Directory.Build.props`
points at Revit 2024 assemblies. The embedded Reality Bridge dispatches only
registered read tools through its ExternalEvent handler. The independent BIM MCP
Ribbon entries point to the installed Horizun.Revit assembly when present.

## Current implementation in this branch

The add-in now collects only immediate `DevelopmentTools.Modules/*/*.csproj`
projects. Before compilation, the manifest validator and generator create
authorized command wrappers and append their Ribbon descriptors to existing
categories. Existing names and commands remain in their current catalog.
The example-agent is a read-only Revit 2024 pilot. Its command is entered through
the generated wrapper, which calls `CommandAccessGuard` before the module.

The `host-poc` project demonstrates an `IPlugin` contract and approved DLL hash,
version, path, and authorization checks. Production does not reference it. It
does not yet provide dependency isolation or a module-only update path; .NET
Framework assembly loading and Revit API boundaries need an isolated Revit 2024
test before enabling it.

## Release and core compatibility gates

`release.ps1`, Inno Setup, and `obfuscar.xml` still enumerate the four original
module DLLs. Obfuscar also contains absolute source-workspace paths. Therefore
this branch does not claim a deployable Release package. A follow-up change must
generate the owned-DLL list and Obfuscar configuration from approved manifests,
verify every DLL in the candidate package, and compare the existing package
layout before any production release. The existing Updater verifies a full ZIP
hash and backs up the installation, but rollback copies old files over new ones;
module-only rollback must remove newly added files as well.

## Test record, 2026-10-19

- PASS: validator accepts the template and the pilot manifest.
- PASS: tests reject existing Ribbon name reuse, Bridge permission without
  separate review, and a future host requirement (4 unit tests).
- PASS: MSBuild evaluates exactly the four existing module references plus the
  pilot; `GenerateModuleRegistration` target executes successfully.
- BLOCKED: full net48 compile on this machine lacks .NET Framework 4.8 reference
  assemblies, although Revit 2024 API DLLs are present.
- NOT RUN: Revit 2024 UI, authorization, update, rollback, and regression tests.
- UNVERIFIED: Revit 2025 and 2026.
- BLOCKED: PR workflow on a shared self-hosted Revit runner was rejected by
  automatic approval review due to untrusted PR code execution and artifact
  exposure. A separate credential-free GitHub-hosted workflow now checks only
  manifests and generated registration; it never builds or runs a module DLL.
  Use an isolated Windows runner with no production credentials before adding
  the Revit build and candidate artifact job.

## Proposed release packaging change for separate approval

The current Release build uses absolute paths in `obfuscar.xml`, and
`release.ps1` and Inno Setup each enumerate the four original owned DLLs.
To package colleague modules without editing these files per module, generate
a portable Obfuscar configuration and owned-DLL inventory after manifest
validation. Release builds must require every owned DLL to appear in the
obfuscated output. Both ZIP and installer must use the same inventory to
exclude raw owned DLLs and include their obfuscated counterparts. In a dry run,
compare the package file list against the existing four-module package plus
the approved pilot. Preserve the previous full ZIP for rollback. Do not alter
`update_manifest.json`, push tags, or create a GitHub Release in the candidate
path. Revert the packaging script changes and restore the previous ZIP if the
Revit 2024 smoke test or rollback check fails.

The automatic approval review rejected a direct change to formal release and
installer inclusion/exclusion logic pending separate approval of this plan.
