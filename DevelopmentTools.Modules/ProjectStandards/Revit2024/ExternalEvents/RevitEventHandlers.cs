using System;
using Autodesk.Revit.UI;

namespace DevelopmentTools.ProjectStandards.Revit2024.ExternalEvents
{
    public class SimpleExternalEventHandler : IExternalEventHandler
    {
        private Action<UIApplication> _action;

        public void SetAction(Action<UIApplication> action)
        {
            _action = action;
        }

        public void Execute(UIApplication app)
        {
            if (_action != null)
            {
                try
                {
                    _action(app);
                }
                catch (Exception ex)
                {
                    TaskDialog.Show("Project Standards Manager Error", ex.ToString());
                }
            }
        }

        public string GetName()
        {
            return "ProjectStandardsExternalEventHandler";
        }
    }
}
