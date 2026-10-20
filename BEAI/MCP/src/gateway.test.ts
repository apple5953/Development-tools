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

test("gateway rejects non-canonical loopback endpoints", () => {
  assert.throws(() => new BridgeGateway({ bridgeUrl: "http://127.0.0.1:8765/api/status" }), /canonical/);
  assert.throws(() => new BridgeGateway({ bridgeUrl: "http://127.0.0.1:8765/api/v1/execute?tool=anything" }), /canonical/);
});

test("gateway rejects tools outside the runtime allowlist", async () => {
  const gateway = new BridgeGateway();
  await assert.rejects(
    () => gateway.call("revit.transaction" as never),
    (error: unknown) => error instanceof BridgeGatewayError && error.code === "BEAI_NOT_SUPPORTED",
  );
});

test("gateway exposes stable bridge errors", async () => {
  const fetchImpl: typeof fetch = async (_input, init) => {
    const request = JSON.parse(String(init?.body)) as { requestId: string };
    return Response.json(
      { requestId: request.requestId, success: false, durationMs: 1, data: null, warnings: [], errors: [{ code: "BEAI_NO_DOCUMENT", message: "No model." }] },
      { status: 400 },
    );
  };
  const gateway = new BridgeGateway({ fetchImpl });
  await assert.rejects(() => gateway.call("document.get_active"), (error: unknown) => {
    return error instanceof BridgeGatewayError && error.code === "BEAI_NO_DOCUMENT";
  });
});

test("gateway rejects mismatched response identities", async () => {
  const fetchImpl: typeof fetch = async () => Response.json(
    { requestId: "wrong", success: true, durationMs: 1, data: {}, warnings: [], errors: [] },
  );
  const gateway = new BridgeGateway({ fetchImpl });
  await assert.rejects(
    () => gateway.call("system.health"),
    (error: unknown) => error instanceof BridgeGatewayError && error.code === "BEAI_INVALID_RESPONSE",
  );
});

