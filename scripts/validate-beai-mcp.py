"""Fail CI when the BEAI MCP / Reality Bridge safety boundary drifts."""
import json
import re
from pathlib import Path


ROOT = Path(__file__).resolve().parents[1]
MCP = ROOT / "BEAI" / "MCP" / "src"
BRIDGE = ROOT / "BEAI" / "RealityBridge" / "BEAI.QueryHost"

EXPECTED_MCP_TOOLS = {
    "system_health",
    "get_active_document",
    "list_levels",
    "query_elements",
    "get_element_parameters",
}


def read(path):
    return path.read_text(encoding="utf-8-sig")


def require(condition, message):
    if not condition:
        raise AssertionError(message)


def main():
    server = read(MCP / "server.ts")
    index = read(MCP / "index.ts")
    gateway = read(MCP / "gateway.ts")
    host = read(BRIDGE / "Core" / "RevitServerHost.cs")
    external_event = read(BRIDGE / "Core" / "BeaiQueryExternalEventHandler.cs")
    dispatcher = read(BRIDGE / "Core" / "QueryDispatcher.cs")
    addin_project = read(ROOT / "DevelopmentTools.Addin" / "DevelopmentTools.Addin.csproj")
    addin_app = read(ROOT / "DevelopmentTools.Addin" / "App.cs")
    flags = json.loads(read(ROOT / "DevelopmentTools.Addin" / "Config" / "feature-flags.json"))

    registered = set(re.findall(r'server\.registerTool\(\s*"([^"]+)"', server))
    require(registered == EXPECTED_MCP_TOOLS, f"MCP tool allowlist drifted: {sorted(registered)}")
    require(server.count("annotations: readOnlyAnnotations") == len(EXPECTED_MCP_TOOLS),
            "Every MCP tool must use the read-only annotations")
    require('const host = "127.0.0.1"' in index, "MCP server must bind IPv4 loopback only")
    require('"http://127.0.0.1:8765/api/v1/execute"' in gateway,
            "MCP Gateway must default to the canonical Reality Bridge endpoint")

    require('DefaultPrefix = "http://127.0.0.1:8765/"' in host,
            "Reality Bridge must bind IPv4 loopback only")
    require("BeaiQueryExternalEventHandler.EnqueueRequest(queryRequest)" in host,
            "HTTP requests must enter the existing ExternalEvent queue")
    require("ExternalEvent.Create(handler)" in external_event and "_externalEvent.Raise()" in external_event,
            "Reality Bridge Revit access must use ExternalEvent")

    bridge_sources = "\n".join(read(path) for path in BRIDGE.rglob("*.cs"))
    require("TransactionMode.Manual" not in bridge_sources,
            "Reality Bridge V0.1 must not declare a manual transaction command")
    require(not re.search(r"\bnew\s+Transaction\s*\(", bridge_sources),
            "Reality Bridge V0.1 must not open a Revit Transaction")

    require(addin_project.count("BEAI.QueryHost.csproj") == 1,
            "DevelopmentTools.Addin must reference exactly one Reality Bridge project")
    require("BEAI.RevitAdapter" not in addin_project, "Inactive legacy adapter must not be referenced")
    require(addin_app.count("EmbeddedBridge.Start(") == 1,
            "DevelopmentTools.Addin must start exactly one embedded Reality Bridge")

    require('/api/v1/execute' in dispatcher, "Canonical execute endpoint is required")
    for legacy_route in ("/api/status", "/api/model", "/api/categories", "/api/count", "/api/elements/search"):
        require(legacy_route in dispatcher, f"Legacy compatibility route missing: {legacy_route}")
    require(dispatcher.count("return MaintenanceDenied();") == 3,
            "All maintenance endpoints must be protected by the actions feature flag")

    require(flags.get("externalAccess") is True and flags.get("read") is True,
            "V0.1 read access flags must be explicit")
    for disabled in ("analysis", "navigation", "actions", "clash", "remoteTransport"):
        require(flags.get(disabled) is False, f"V0.1 feature must remain disabled: {disabled}")

    print("BEAI MCP compliance PASS")
    print("- one embedded Reality Bridge")
    print("- loopback-only MCP and HTTP bridge")
    print("- five explicitly registered read-only MCP tools")
    print("- Revit API access through the existing ExternalEvent queue")
    print("- no Reality Bridge Transaction or TransactionMode.Manual")
    print("- legacy read endpoints preserved; maintenance endpoints policy-gated")


if __name__ == "__main__":
    main()
