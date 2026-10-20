import { randomUUID } from "node:crypto";
import type { RealityRequest, RealityResponse, RealityTool } from "./contracts.js";

const ALLOWED_TOOLS = new Set<RealityTool>([
  "system.health",
  "document.get_active",
  "level.list",
  "element.query",
  "element.get_parameters",
]);

export interface GatewayOptions {
  bridgeUrl?: string;
  timeoutMs?: number;
  clientName?: string;
  fetchImpl?: typeof fetch;
}

export class BridgeGatewayError extends Error {
  constructor(
    message: string,
    public readonly code: string,
    public readonly response?: RealityResponse,
  ) {
    super(message);
    this.name = "BridgeGatewayError";
  }
}

export class BridgeGateway {
  private readonly bridgeUrl: string;
  private readonly timeoutMs: number;
  private readonly clientName: string;
  private readonly fetchImpl: typeof fetch;

  constructor(options: GatewayOptions = {}) {
    this.bridgeUrl = options.bridgeUrl ?? process.env.BEAI_BRIDGE_URL ?? "http://127.0.0.1:8765/api/v1/execute";
    this.timeoutMs = options.timeoutMs ?? Number(process.env.BEAI_BRIDGE_TIMEOUT_MS ?? 30_000);
    this.clientName = options.clientName ?? "beai-local-mcp";
    this.fetchImpl = options.fetchImpl ?? fetch;

    const url = new URL(this.bridgeUrl);
    if (url.protocol !== "http:" || !["127.0.0.1", "localhost", "[::1]"].includes(url.hostname)) {
      throw new Error("BEAI_BRIDGE_URL must use loopback HTTP in Local-First mode.");
    }
    if (url.pathname.replace(/\/$/, "") !== "/api/v1/execute" || url.username || url.password || url.search || url.hash) {
      throw new Error("BEAI_BRIDGE_URL must target the canonical /api/v1/execute endpoint without credentials, query, or fragment.");
    }
  }

  async call<T>(tool: RealityTool, args: Record<string, unknown> = {}): Promise<RealityResponse<T>> {
    if (!ALLOWED_TOOLS.has(tool)) {
      throw new BridgeGatewayError(`Tool '${String(tool)}' is not registered by the BEAI MCP server.`, "BEAI_NOT_SUPPORTED");
    }
    const request: RealityRequest = {
      requestId: randomUUID(),
      version: "1.0",
      tool,
      timestamp: new Date().toISOString(),
      args,
      context: { source: "mcp", client: this.clientName },
    };

    const controller = new AbortController();
    const timer = setTimeout(() => controller.abort(), this.timeoutMs);
    const started = performance.now();

    try {
      const response = await this.fetchImpl(this.bridgeUrl, {
        method: "POST",
        headers: { "content-type": "application/json", accept: "application/json" },
        body: JSON.stringify(request),
        signal: controller.signal,
      });

      const payload = (await response.json()) as RealityResponse<T>;
      const elapsed = Math.round(performance.now() - started);
      console.info(JSON.stringify({ level: "AUDIT", requestId: request.requestId, tool, durationMs: elapsed, success: payload.success }));

      if (payload.requestId !== request.requestId) {
        throw new BridgeGatewayError("Reality Bridge returned a mismatched request ID.", "BEAI_INVALID_RESPONSE");
      }

      if (!response.ok || !payload.success) {
        const error = payload.errors?.[0];
        throw new BridgeGatewayError(error?.message ?? `Bridge returned HTTP ${response.status}.`, error?.code ?? "BEAI_BRIDGE_ERROR", payload);
      }
      return payload;
    } catch (error) {
      if (error instanceof BridgeGatewayError) throw error;
      if (error instanceof Error && error.name === "AbortError") {
        throw new BridgeGatewayError(`Bridge request timed out after ${this.timeoutMs}ms.`, "BEAI_TIMEOUT");
      }
      throw new BridgeGatewayError("BEAI Reality Bridge is unavailable. Start Revit 2024 and verify the add-in is enabled.", "BEAI_BRIDGE_UNAVAILABLE");
    } finally {
      clearTimeout(timer);
    }
  }
}

