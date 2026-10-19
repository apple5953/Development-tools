using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using Autodesk.Revit.UI;

namespace BIMDrawingFactory.UI.Revit
{
    public class RevitExternalEventHandler : IExternalEventHandler
    {
        private readonly ConcurrentQueue<Action<UIApplication>> _executionQueue = new ConcurrentQueue<Action<UIApplication>>();

        public void Execute(UIApplication app)
        {
            Action<UIApplication> action;
            while (_executionQueue.TryDequeue(out action))
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
                    System.Diagnostics.Debug.WriteLine(string.Format("[RevitExternalEventHandler Exception] {0}", ex));
                }
            }
        }

        public string GetName()
        {
            return "BIMDrawingFactory_ExternalEventHandler";
        }

        public Task RunAsync(Action<UIApplication> action)
        {
            if (action == null) throw new ArgumentNullException("action");
            var tcs = new TaskCompletionSource<bool>();
            if (RevitApp.ExternalEvent != null)
            {
                _executionQueue.Enqueue(app =>
                {
                    if (tcs.Task.IsCompleted) return;
                    try
                    {
                        action(app);
                        tcs.TrySetResult(true);
                    }
                    catch (Exception ex)
                    {
                        tcs.TrySetException(ex);
                    }
                });

                var raiseResult = RevitApp.ExternalEvent.Raise();
                if (raiseResult == ExternalEventRequest.Denied)
                {
                    tcs.TrySetException(new InvalidOperationException("Revit 拒絕執行外部事件；請關閉目前的 Revit 對話框後重試。"));
                }
            }
            else
            {
                tcs.TrySetException(new InvalidOperationException("Revit ExternalEvent 尚未初始化。"));
            }
            return tcs.Task;
        }

        public Task<T> RunAsync<T>(Func<UIApplication, T> func)
        {
            return RunAsync(func, CancellationToken.None);
        }

        public Task<T> RunAsync<T>(Func<UIApplication, T> func, CancellationToken cancellationToken)
        {
            if (func == null) throw new ArgumentNullException("func");
            var tcs = new TaskCompletionSource<T>();
            int executionState = 0; // 0 queued, 1 running, -1 cancelled before start
            CancellationTokenRegistration registration = default(CancellationTokenRegistration);
            if (cancellationToken.CanBeCanceled)
            {
                registration = cancellationToken.Register(() =>
                {
                    if (Interlocked.CompareExchange(ref executionState, -1, 0) == 0)
                        tcs.TrySetCanceled();
                });
            }
            if (RevitApp.ExternalEvent != null)
            {
                _executionQueue.Enqueue(app =>
                {
                    if (Interlocked.CompareExchange(ref executionState, 1, 0) != 0)
                    {
                        registration.Dispose();
                        return;
                    }
                    try
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        var result = func(app);
                        tcs.TrySetResult(result);
                    }
                    catch (OperationCanceledException) { tcs.TrySetCanceled(); }
                    catch (Exception ex)
                    {
                        tcs.TrySetException(ex);
                    }
                    finally { registration.Dispose(); }
                });

                var raiseResult = RevitApp.ExternalEvent.Raise();
                if (raiseResult == ExternalEventRequest.Denied)
                {
                    Interlocked.CompareExchange(ref executionState, -1, 0);
                    registration.Dispose();
                    tcs.TrySetException(new InvalidOperationException("Revit 拒絕執行外部事件；請關閉目前的 Revit 對話框後重試。"));
                }
            }
            else
            {
                Interlocked.CompareExchange(ref executionState, -1, 0);
                registration.Dispose();
                tcs.TrySetException(new InvalidOperationException("Revit ExternalEvent 尚未初始化。"));
            }
            return tcs.Task;
        }
    }
}
