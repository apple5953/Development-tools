using System;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BEAI.QueryHost.DTO;
using Newtonsoft.Json;

namespace BEAI.QueryHost.Core
{
    public class RevitServerHost : IDisposable
    {
        private static readonly Lazy<RevitServerHost> _lazy = new Lazy<RevitServerHost>(delegate { return new RevitServerHost(); });
        public static RevitServerHost Instance { get { return _lazy.Value; } }

        private const string DefaultPrefix = "http://127.0.0.1:8765/";
        private const int RequestTimeoutSeconds = 30;
        private const int MaxPayloadBytes = 1024 * 1024;

        private HttpListener _listener;
        private CancellationTokenSource _listenerCts;
        private readonly object _stateLock = new object();
        private bool _isRunning;

        public bool IsRunning { get { return _isRunning; } }

        public bool Start()
        {
            lock (_stateLock)
            {
                if (_isRunning) return true;

                try
                {
                    _listenerCts = new CancellationTokenSource();
                    _listener = new HttpListener();
                    _listener.Prefixes.Add(DefaultPrefix);
                    _listener.Start();

                    _isRunning = true;
                    RuntimeState.Instance.ServerRunning = true;
                    RuntimeState.Instance.ServerAddress = DefaultPrefix;

                    Task.Factory.StartNew(delegate { ListenLoopAsync(_listener, _listenerCts.Token); }, TaskCreationOptions.LongRunning);

                    Logger.Info(string.Format("[RevitServerHost] Query Server started on {0}", DefaultPrefix));
                    return true;
                }
                catch (Exception ex)
                {
                    _isRunning = false;
                    RuntimeState.Instance.ServerRunning = false;
                    Logger.Error(string.Format("[RevitServerHost] Failed to start HTTP server on {0}: {1}", DefaultPrefix, ex.Message), ex);
                    RuntimeState.Instance.SetLastError(string.Format("SERVER_START_FAILED: {0}", ex.Message));
                    return false;
                }
            }
        }

        public void Stop()
        {
            lock (_stateLock)
            {
                if (!_isRunning) return;

                try
                {
                    _isRunning = false;
                    RuntimeState.Instance.ServerRunning = false;

                    if (_listenerCts != null)
                    {
                        _listenerCts.Cancel();
                    }
                    if (_listener != null && _listener.IsListening)
                    {
                        _listener.Stop();
                        _listener.Close();
                    }

                    Logger.Info("[RevitServerHost] Query Server stopped.");
                }
                catch (Exception ex)
                {
                    Logger.Error(string.Format("[RevitServerHost] Error during server stop: {0}", ex.Message), ex);
                }
                finally
                {
                    _listener = null;
                    if (_listenerCts != null)
                    {
                        _listenerCts.Dispose();
                        _listenerCts = null;
                    }
                }
            }
        }

        private async void ListenLoopAsync(HttpListener listener, CancellationToken ct)
        {
            while (!ct.IsCancellationRequested && listener != null && listener.IsListening)
            {
                try
                {
                    HttpListenerContext context = await listener.GetContextAsync();
                    Task.Run(delegate { HandleContext(context); });
                }
                catch (HttpListenerException)
                {
                    break;
                }
                catch (ObjectDisposedException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    if (!ct.IsCancellationRequested)
                    {
                        Logger.Error(string.Format("[RevitServerHost] Error accepting HTTP connection: {0}", ex.Message), ex);
                    }
                }
            }
        }

        private void HandleContext(HttpListenerContext context)
        {
            HttpListenerRequest request = context.Request;
            HttpListenerResponse response = context.Response;

            try
            {
                if (request.HttpMethod.Equals("OPTIONS", StringComparison.OrdinalIgnoreCase))
                {
                    response.StatusCode = (int)HttpStatusCode.NoContent;
                    response.Close();
                    return;
                }

                string body = "";
                if (request.HasEntityBody)
                {
                    if (request.ContentLength64 > MaxPayloadBytes)
                    {
                        response.StatusCode = (int)HttpStatusCode.RequestEntityTooLarge;
                        response.Close();
                        return;
                    }
                    using (var reader = new StreamReader(request.InputStream, request.ContentEncoding))
                    {
                        body = reader.ReadToEnd();
                    }
                }

                var queryRequest = new BeaiQueryRequest();
                queryRequest.HttpMethod = request.HttpMethod;
                queryRequest.Path = request.Url.AbsolutePath;
                queryRequest.Body = body;
                queryRequest.Cts = new CancellationTokenSource(TimeSpan.FromSeconds(RequestTimeoutSeconds));

                foreach (string key in request.QueryString.AllKeys)
                {
                    if (key != null)
                    {
                        queryRequest.QueryParams[key] = request.QueryString[key];
                    }
                }

                BeaiQueryResponse queryResponse;

                try
                {
                    BeaiQueryExternalEventHandler.EnqueueRequest(queryRequest);

                    // Wait synchronously on ThreadPool thread
                    bool completed = queryRequest.CompletionSource.Task.Wait(TimeSpan.FromSeconds(RequestTimeoutSeconds));
                    if (completed)
                    {
                        queryResponse = queryRequest.CompletionSource.Task.Result;
                    }
                    else
                    {
                        queryRequest.Cts.Cancel();
                        queryResponse = BeaiQueryResponse.Json(
                            JsonConvert.SerializeObject(ApiResponse<object>.Fail(ErrorCodes.QueryTimeout,
                                "Request timed out (30s). Revit may be busy, displaying a modal dialog, or in element edit mode.")),
                            (int)HttpStatusCode.RequestTimeout);
                    }
                }
                catch (Exception ex)
                {
                    queryResponse = BeaiQueryResponse.Json(
                        JsonConvert.SerializeObject(ApiResponse<object>.Fail(ErrorCodes.InternalError, string.Format("Server internal error: {0}", ex.Message))),
                        (int)HttpStatusCode.InternalServerError);
                }

                try
                {
                    response.StatusCode = queryResponse.StatusCode;
                    response.ContentType = queryResponse.ContentType;

                    byte[] buffer = Encoding.UTF8.GetBytes(queryResponse.Body != null ? queryResponse.Body : "");
                    response.ContentLength64 = buffer.Length;
                    response.OutputStream.Write(buffer, 0, buffer.Length);
                }
                catch (Exception ex)
                {
                    Logger.Warn(string.Format("[RevitServerHost] Failed to write HTTP response: {0}", ex.Message));
                }
                finally
                {
                    try { response.Close(); } catch { }
                    if (queryRequest.Cts != null)
                    {
                        queryRequest.Cts.Dispose();
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Error(string.Format("[RevitServerHost] Exception in HandleContext: {0}", ex.Message), ex);
                try { response.Close(); } catch { }
            }
        }

        public void Dispose()
        {
            Stop();
        }
    }
}
