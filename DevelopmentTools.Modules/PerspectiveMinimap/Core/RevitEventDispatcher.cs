using System;
using System.Collections.Concurrent;
using Autodesk.Revit.UI;

namespace RevitPerspectiveMinimap.Core
{
    public class RevitEventDispatcher : IExternalEventHandler
    {
        private static RevitEventDispatcher _instance;
        private static ExternalEvent _externalEvent;
        private readonly ConcurrentQueue<Action<UIApplication>> _actionQueue = new ConcurrentQueue<Action<UIApplication>>();

        public static RevitEventDispatcher Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = new RevitEventDispatcher();
                }
                return _instance;
            }
        }

        public static ExternalEvent ExternalEvent
        {
            get
            {
                if (_externalEvent == null)
                {
                    _externalEvent = ExternalEvent.Create(Instance);
                }
                return _externalEvent;
            }
        }

        public void Execute(UIApplication app)
        {
            Action<UIApplication> action;
            while (_actionQueue.TryDequeue(out action))
            {
                try
                {
                    if (action != null)
                    {
                        action(app);
                    }
                }
                catch (Exception ex)
                {
                    TaskDialog.Show("透視圖索引面板錯誤", "執行操作時發生例外: " + ex.Message);
                }
            }
        }

        public string GetName()
        {
            return "RevitPerspectiveMinimap_EventDispatcher";
        }

        public void Enqueue(Action<UIApplication> action)
        {
            if (action == null) return;
            _actionQueue.Enqueue(action);
            ExternalEvent.Raise();
        }
    }
}