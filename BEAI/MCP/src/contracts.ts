export type RealityTool =
  | "system.health"
  | "document.get_active"
  | "level.list"
  | "element.query"
  | "element.get_parameters";

export interface RealityRequest {
  requestId: string;
  version: "1.0";
  tool: RealityTool;
  timestamp: string;
  args: Record<string, unknown>;
  context: {
    source: "mcp";
    client: string;
  };
}

export interface RealityError {
  code: string;
  message: string;
}

export interface RealityResponse<T = unknown> {
  requestId: string;
  success: boolean;
  durationMs: number;
  data: T | null;
  warnings: string[];
  errors: RealityError[];
}

