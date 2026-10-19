using System;
using System.Collections.Concurrent;
using Autodesk.Revit.UI;

namespace BEAI.QueryHost.Core
{
    public class BeaiQueryExternalEventHandler : IExternalEventHandler
    {
        private static readonly ConcurrentQueue<BeaiQueryRequest> _queue = new ConcurrentQueue<BeaiQueryRequest>();
        private static ExternalEvent _externalEvent;

        public static void Initialize(BeaiQueryExternalEventHandler handler)
        {
            _externalEvent = ExternalEvent.Create(handler);
        }

        public static void EnqueueRequest(BeaiQueryRequest request)
        {
            _queue.Enqueue(request);
            if (_externalEvent != null)
            {
                _externalEvent.Raise();
            }
        }

        public void Execute(UIApplication app)
        {
            int processedCount = 0;
            const int maxBatchSize = 10;

            BeaiQueryRequest request;
            while (processedCount < maxBatchSize && _queue.TryDequeue(out request))
            {
                processedCount++;

                if (request.Cts != null && request.Cts.IsCancellationRequested)
                {
                    continue;
                }

                try
                {
                    var response = QueryDispatcher.Dispatch(app, request);
                    request.CompletionSource.TrySetResult(response);
                }
                catch (Exception ex)
                {
                    Logger.Error(string.Format("ExternalEvent handler exception: {0}", ex.Message), ex);
                    request.CompletionSource.TrySetException(ex);
                }
            }

            if (!_queue.IsEmpty && _externalEvent != null)
            {
                _externalEvent.Raise();
            }
        }

        public string GetName()
        {
            return "BEAI Query External Event Handler";
        }
    }
}
