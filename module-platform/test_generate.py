import unittest

from generate import render


def module(module_id, name, text, category, tool_id):
    return ({
        "id": module_id,
        "Ribbon": {"name": name, "text": text, "category": category},
        "ToolId": tool_id,
        "entryClass": f"Example.{name}.Command",
        "dependencies": [],
    }, None)


class ModuleGenerationTests(unittest.TestCase):
    def test_each_module_gets_an_independent_panel(self):
        source = render([
            module("review-agent", "ReviewAgent", "審查助理", "review", "DT_ReviewAgent"),
            module("drawing-agent", "DrawingAgent", "出圖助理", "drawing", "DT_DrawingAgent"),
        ])

        self.assertEqual(2, source.count("panels.Add(Panel(TabMain"))
        self.assertNotIn("Children.Add", source)
        self.assertIn('Panel(TabMain, null, "審查助理"', source)
        self.assertIn('EnsureAuthorized("DT_ReviewAgent", "審查助理")', source)

    def test_category_controls_panel_order(self):
        source = render([
            module("review-agent", "ReviewAgent", "審查助理", "review", "DT_ReviewAgent"),
            module("model-agent", "ModelAgent", "建模助理", "model", "DT_ModelAgent"),
            module("drawing-agent", "DrawingAgent", "出圖助理", "drawing", "DT_DrawingAgent"),
        ])

        self.assertLess(source.index('null, "出圖助理"'), source.index('null, "建模助理"'))
        self.assertLess(source.index('null, "建模助理"'), source.index('null, "審查助理"'))

    def test_duplicate_visible_panel_name_is_rejected(self):
        with self.assertRaisesRegex(ValueError, "Ribbon text"):
            render([
                module("agent-a", "AgentA", "相同名稱", "review", "DT_AgentA"),
                module("agent-b", "AgentB", "相同名稱", "review", "DT_AgentB"),
            ])


if __name__ == "__main__":
    unittest.main()
