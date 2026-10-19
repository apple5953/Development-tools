import assert from "node:assert/strict";
import test from "node:test";
import { BridgeGateway, BridgeGatewayError } from "./gateway.js";

test("gateway creates the canonical Reality Bridge envelope", async () => {
  let captured: Record<string, unknown> | undefined;
  const fetchImpl: typeof fetch = async (_input, init) => {
    captured = JSON.parse(String(init?.body)) as Record<string, unknown>;
    return Response.json({ requestId: captured.requestId, success: true, durationMs: 2, data: { bridge: "online" }, warnings: [], errors: [] });
  };
  const gateway = new BridgeGateway({ fetchImpl });
  const response = await gateway.call("system.health");

  assert.equal(captured?.version, "1.0");
  assert.equal(captured?.tool, "system.health");
  assert.deepEqual(captured?.context, { source: "mcp", client: "beai-local-mcp" });
  assert.deepEqual(response.data, { bridge: "online" });
});

test("gateway rejects non-loopback bridge URLs", () => {
  assert.throws(() => new BridgeGateway({ bridgeUrl: "http://0.0.0.0:8765/api/v1/execute" }), /loopback/);
});

test("gateway exposes stable bridge errors", async () => {
  const fetchImpl: typeof fetch = async () => Response.json(
    { requestId: "x", success: false, durationMs: 1, data: null, warnings: [], errors: [{ code: "BEAI_NO_DOCUMENT", message: "No model." }] },
    { status: 400 },
  );
  const gateway = new BridgeGateway({ fetchImpl });
  await assert.rejects(() => gateway.call("document.get_active"), (error: unknown) => {
    return error instanceof BridgeGatewayError && error.code === "BEAI_NO_DOCUMENT";
  });
});

