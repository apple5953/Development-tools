using System;
using System.Windows.Threading;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitPerspectiveMinimap.UI;

namespace RevitPerspectiveMinimap
{
    public static class CoreApp
    {
        private static Dispatcher _uiThreadDispatcher;
        public static Dispatcher UIThreadDispatcher
        {
            get
            {
                if (_uiThreadDispatcher == null)
                {
                    _uiThreadDispatcher = Dispatcher.CurrentDispatcher;
                }
                return _uiThreadDispatcher;
            }
            set { _uiThreadDispatcher = value; }
        }
    }
}