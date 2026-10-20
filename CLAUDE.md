# BEAI Claude Code Instructions

Read and obey [AGENTS.md](AGENTS.md) first, then [docs/MODULE_CONTRACT.md](docs/MODULE_CONTRACT.md) and [docs/CONTRIBUTING.md](docs/CONTRIBUTING.md).

For independently updateable AI/analysis workers, also read [docs/HOT_SWAPPABLE_AGENT_ARCHITECTURE.zh-TW.md](docs/HOT_SWAPPABLE_AGENT_ARCHITECTURE.zh-TW.md) before editing `DevelopmentTools.AgentWorkers/`.

## Non-negotiable
- Never create another Revit Reality Bridge or parallel MCP/Revit integration.
- Existing RealityBridge is the only secure boundary. Revit API calls must use ExternalEvent on the Revit UI thread.
- Preserve existing endpoints; V0.1 RealityBridge is read-only: do not introduce Revit Transactions there.
- Do not change authorization, updater, release, production configurations, or protected core without explicitly identifying it in the PR and asking for approval.
- Work only on the assigned feature branch and permitted module files. Never push to main, merge, publish a release, or use force push.
- Do not commit credentials, tokens, private BIM models or unauthorized Autodesk DLLs.
- Before coding, inspect current files and describe affected paths; prefer independent class libraries under DevelopmentTools.Modules where technically appropriate.
- Report test commands verbatim with actual result. Never claim Revit integration works based on compilation alone.
- Submit a PR and wait for human approval. See docs/TEAM_AI_PLAYBOOK.md.
