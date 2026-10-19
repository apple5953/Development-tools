import { createServer } from "node:http";
import { createMcpHandler } from "@modelcontextprotocol/server";
import { localhostHostValidation, localhostOriginValidation, toNodeHandler } from "@modelcontextprotocol/node";
import { createBeaiServer } from "./server.js";

const host = "127.0.0.1";
const port = Number(process.env.BEAI_MCP_PORT ?? 3000);
const validateHost = localhostHostValidation();
const validateOrigin = localhostOriginValidation();
const mcpHandler = createMcpHandler(() => createBeaiServer(), {
  responseMode: "json",
  onerror: (error) => console.error(JSON.stringify({ level: "ERROR", source: "mcp", message: error.message })),
});
const nodeHandler = toNodeHandler(mcpHandler, {
  onerror: (error) => console.error(JSON.stringify({ level: "ERROR", source: "http", message: error.message })),
});

const server = createServer(async (request, response) => {
  const path = new URL(request.url ?? "/", `http://${request.headers.host ?? host}`).pathname;
  if (path === "/health" && request.method === "GET") {
    response.writeHead(200, { "content-type": "application/json" });
    response.end(JSON.stringify({ service: "BEAI MCP Server", status: "online", mode: "READ" }));
    return;
  }
  if (path !== "/mcp") {
    response.writeHead(404, { "content-type": "application/json" });
    response.end(JSON.stringify({ error: "Not found" }));
    return;
  }
  if (!validateHost(request, response) || !validateOrigin(request, response)) return;
  await nodeHandler(request, response);
});

server.listen(port, host, () => {
  console.info(`BEAI MCP Server listening at http://${host}:${port}/mcp`);
});

function shutdown() {
  void mcpHandler.close();
  server.close();
}

process.on("SIGINT", shutdown);
process.on("SIGTERM", shutdown);

