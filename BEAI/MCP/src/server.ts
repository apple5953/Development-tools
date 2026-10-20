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

  return server;
}

