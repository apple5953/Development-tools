> 禁止建立第二套 Revit AI Bridge。所有 AI、MCP、分析、碰撞與未來模型修改能力，必須基於現有安全通用 Revit Bridge 持續重構與擴充。ChatGPT 只是 Client，不是 Core。Reality Bridge 才是 Revit 與外部 AI 世界之間唯一的安全邊界。

# Repository Rules

- Keep the bridge AI-vendor neutral.
- Revit API access must execute through `ExternalEvent` on the Revit UI thread.
- Production clients may call only explicitly registered structured tools.
- V0.1 is read-only. Do not open a Revit `Transaction` from Reality Bridge code.
- Keep existing HTTP endpoints compatible while clients migrate to `/api/v1/execute`.
- Bind local HTTP services to loopback only.

