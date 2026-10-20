import unittest

from generate import render


def module(module_id, name, text, category, tool_id, tools=None):
    ribbon = {"name": name, "text": text, "category": category}
    if tools is not None:
        ribbon["tools"] = tools
    return ({
        "id": module_id,
        "Ribbon": ribbon,
        "ToolId": tool_id,
        "entryClass": f"Example.{name}.Command",
        "dependencies": [],
    }, None)


class ModuleGenerationTests(unittest.TestCase):
    def test_agents_share_one_panel_and_each_gets_a_menu(self):
        source = render([
            module("review-agent", "ReviewAgent", "審查助理", "review", "DT_ReviewAgent"),
            module("drawing-agent", "DrawingAgent", "出圖助理", "drawing", "DT_DrawingAgent"),
        ])

        self.assertEqual(1, source.count("panels.Add(Panel(TabMain"))
        self.assertIn('Panel(TabMain, null, "AI Agents"', source)
        self.assertIn('One(Menu("ReviewAgent", "審查助理"', source)
        self.assertIn('One(Menu("DrawingAgent", "出圖助理"', source)
        self.assertIn('EnsureAuthorized("DT_ReviewAgent", "審查助理 / 審查助理")', source)

    def test_one_agent_can_expose_multiple_authorized_tools(self):
        source = render([
            module("review-agent", "ReviewAgent", "審查助理", "review", "DT_ReviewAgent", [
                {"name": "ReviewCapture", "text": "擷取", "entryClass": "Example.CaptureCommand"},
                {"name": "ReviewReport", "text": "報告", "entryClass": "Example.ReportCommand"},
            ])
        ])

        self.assertEqual(1, source.count('One(Menu("ReviewAgent"'))
        self.assertIn('Generated_ReviewCapture', source)
        self.assertIn('Generated_ReviewReport', source)
        self.assertEqual(2, source.count('EnsureAuthorized("DT_ReviewAgent"'))

    def test_category_controls_panel_order(self):
        source = render([
            module("review-agent", "ReviewAgent", "審查助理", "review", "DT_ReviewAgent"),
            module("model-agent", "ModelAgent", "建模助理", "model", "DT_ModelAgent"),
            module("drawing-agent", "DrawingAgent", "出圖助理", "drawing", "DT_DrawingAgent"),
        ])

        self.assertLess(source.index('Menu("DrawingAgent"'), source.index('Menu("ModelAgent"'))
        self.assertLess(source.index('Menu("ModelAgent"'), source.index('Menu("ReviewAgent"'))

    def test_duplicate_visible_agent_name_is_rejected(self):
        with self.assertRaisesRegex(ValueError, "Ribbon text"):
            render([
                module("agent-a", "AgentA", "相同名稱", "review", "DT_AgentA"),
                module("agent-b", "AgentB", "相同名稱", "review", "DT_AgentB"),
            ])


if __name__ == "__main__":
    unittest.main()
