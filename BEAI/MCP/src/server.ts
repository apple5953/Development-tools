import { McpServer } from "@modelcontextprotocol/server";
import { z } from "zod";
import { BridgeGateway, BridgeGatewayError } from "./gateway.js";

const readOnlyAnnotations = {
  readOnlyHint: true,
  destructiveHint: false,
  idempotentHint: true,
  openWorldHint: false,
};

function toolResult(data: unknown) {
  return {
    content: [{ type: "text" as const, text: JSON.stringify(data, null, 2) }],
    structuredContent: data as Record<string, unknown>,
  };
}

function toolError(error: unknown) {
  const code = error instanceof BridgeGatewayError ? error.code : "BEAI_INTERNAL_ERROR";
  const message = error instanceof Error ? error.message : "Unknown BEAI error.";
  return {
    isError: true,
    content: [{ type: "text" as const, text: JSON.stringify({ code, message }) }],
  };
}

export function createBeaiServer(gateway = new BridgeGateway()): McpServer {
  const server = new McpServer(
    { name: "BEAI Reality Bridge", version: "0.1.0" },
    { capabilities: { tools: {} } },
  );

  server.registerTool(
    "system_health",
    {
      title: "BEAI 系統狀態",
      description: "Check the local BEAI Reality Bridge, Revit 2024, active document, ExternalEvent access, and read-only mode.",
      inputSchema: z.object({}),
      annotations: readOnlyAnnotations,
    },
    async () => {
      try { return toolResult((await gateway.call("system.health")).data); }
      catch (error) { return toolError(error); }
    },
  );

  server.registerTool(
    "get_active_document",
    {
      title: "取得目前 Revit 模型",
      description: "Get identity and status information for the currently active Autodesk Revit model. This tool is read-only.",
      inputSchema: z.object({}),
      annotations: readOnlyAnnotations,
    },
    async () => {
      try { return toolResult((await gateway.call("document.get_active")).data); }
      catch (error) { return toolError(error); }
    },
  );

  server.registerTool(
    "list_levels",
    {
      title: "列出樓層",
      description: "List all levels in the active Revit model with element IDs and elevations in millimetres. This tool is read-only.",
      inputSchema: z.object({}),
      annotations: readOnlyAnnotations,
    },
    async () => {
      try { return toolResult((await gateway.call("level.list")).data); }
      catch (error) { return toolError(error); }
    },
  );

  server.registerTool(
    "query_elements",
    {
      title: "查詢 Revit 元件",
      description: "Query real elements from the active Autodesk Revit model by category, level, family, type, workset, ID, or name. Results are read-only and limited to 500 per page.",
      inputSchema: z.object({
        scope: z.enum(["host"]).default("host"),
        category: z.string().min(1).optional(),
        categoryId: z.number().int().optional(),
        level: z.string().optional(),
        levelId: z.number().int().optional(),
        family: z.string().optional(),
        type: z.string().optional(),
        workset: z.string().optional(),
        worksetId: z.number().int().optional(),
        nameContains: z.string().optional(),
        limit: z.number().int().min(1).max(500).default(100),
        cursor: z.number().int().min(1).default(1),
      }),
      annotations: readOnlyAnnotations,
    },
    async (args) => {
      try { return toolResult((await gateway.call("element.query", args)).data); }
      catch (error) { return toolError(error); }
    },
  );

  server.registerTool(
    "get_element_parameters",
    {
      title: "取得元件參數",
      description: "Read instance and type parameters for up to 50 elements in the active Revit model. This tool never modifies the model.",
      inputSchema: z.object({
        elementIds: z.array(z.number().int().positive()).min(1).max(50),
        parameters: z.array(z.string()).max(100).optional(),
      }),
      annotations: readOnlyAnnotations,
    },
    async (args) => {
      try { return toolResult((await gateway.call("element.get_parameters", args)).data); }
      catch (error) { return toolError(error); }
    },
  );

  server.registerTool(
    "run_code_check",
    {
      title: "法規檢討（全條文）",
      description:
        "Run the full Taiwan building-code check (建築技術規則、無障礙、消防、建築法) on the active Revit model and return failed, warning and no-data items with element IDs. " +
        "Temporary views and links are created inside a Revit transaction that is always rolled back, so the saved model is unchanged. " +
        "Project conditions come from the saved 法規檢討 settings; pass settings to override them for this run only. Can take several minutes on large models. " +
        "Results support, but never replace, the registered architect's review and signature.",
      inputSchema: z.object({
        maxItems: z.number().int().min(1).max(2000).default(200),
        writeReport: z.boolean().default(false),
        settings: z
          .object({
            useGroups: z.array(z.string().min(1)).max(20).optional(),
            zone: z.string().optional(),
            inUrbanPlan: z.boolean().optional(),
            roadWidthM: z.number().nonnegative().optional(),
            siteAreaM2: z.number().nonnegative().optional(),
            buildingAreaM2: z.number().nonnegative().optional(),
            maxCoverageRatio: z.number().nonnegative().optional(),
            maxFarRatio: z.number().nonnegative().optional(),
            fireproof: z.boolean().optional(),
            sprinklerMode: z.enum(["auto", "yes", "no"]).optional(),
            accessibilityApplies: z.boolean().optional(),
            travelDistance: z.boolean().optional(),
          })
          .passthrough()
          .optional(),
      }),
      annotations: readOnlyAnnotations,
    },
    async (args) => {
      try {
        const response = await gateway.call("code_check.run", args);
        return toolResult({ ...(response.data as Record<string, unknown>), bridgeWarnings: response.warnings });
      }
      catch (error) { return toolError(error); }
    },
  );

  server.registerTool(
    "check_rebar",
    {
      title: "鋼筋檢核",
      description:
        "Check rebar in the given beams, columns or rebar elements (or the whole model when elementIds is omitted) and return issues with element IDs. " +
        "Read-only. Results support, but never replace, the structural engineer's review and signature.",
      inputSchema: z.object({
        elementIds: z.array(z.number().int().positive()).max(2000).optional(),
        maxItems: z.number().int().min(1).max(2000).default(200),
        writeReport: z.boolean().default(false),
      }),
      annotations: readOnlyAnnotations,
    },
    async (args) => {
      try {
        const response = await gateway.call("rebar.check", args);
        return toolResult({ ...(response.data as Record<string, unknown>), bridgeWarnings: response.warnings });
      }
      catch (error) { return toolError(error); }
    },
  );

  server.registerTool(
    "compare_project_standards",
    {
      title: "專案標準比對",
      description:
        "Compare project standards (shared/project parameters, view templates, view filters, line styles and patterns, families, schedules, levels, worksets, …) " +
        "between a master standards document and a target document. Both must already be open in Revit; target defaults to the active document. " +
        "Returns per-category counts and differing items, highest risk first. Read-only: it never opens, closes or changes files.",
      inputSchema: z.object({
        master: z.string().min(1),
        target: z.string().min(1).optional(),
        maxItems: z.number().int().min(1).max(2000).default(200),
        includeEqual: z.boolean().default(false),
      }),
      annotations: readOnlyAnnotations,
    },
    async (args) => {
      try {
        const response = await gateway.call("project_standards.compare", args);
        return toolResult({ ...(response.data as Record<string, unknown>), bridgeWarnings: response.warnings });
      }
      catch (error) { return toolError(error); }
    },
  );

  server.registerTool(
    "compare_parameters",
    {
      title: "參數比對",
      description:
        "Compare view templates, schedules, project parameters, line styles and materials from a source document to a target document. " +
        "Both must already be open in Revit; target defaults to the active document. Returns items only in the source and items whose parameters differ. Read-only.",
      inputSchema: z.object({
        source: z.string().min(1),
        target: z.string().min(1).optional(),
        maxItems: z.number().int().min(1).max(2000).default(200),
        includeIdentical: z.boolean().default(false),
      }),
      annotations: readOnlyAnnotations,
    },
    async (args) => {
      try {
        const response = await gateway.call("parameters.compare", args);
        return toolResult({ ...(response.data as Record<string, unknown>), bridgeWarnings: response.warnings });
      }
      catch (error) { return toolError(error); }
    },
  );

  return server;
}

