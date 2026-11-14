using System;
using System.IO;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using YTShortsManager.Models;

namespace YTShortsManager.Services
{
    /// <summary>
    /// Локальный HTTP-мост для браузерного расширения.
    /// Слушает только 127.0.0.1, принимает только запросы от расширений,
    /// каждый запрос обрабатывается в отдельной задаче (раньше один запрос блокировал сервер).
    /// </summary>
    public sealed class HttpBridgeServer : IDisposable
    {
        public const int Port = 5000;

        private const int MaxBodyChars = 2 * 1024 * 1024;

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true
        };

        private readonly HttpListener _listener = new();
        private readonly Func<DownloadRequest, Task<bool>> _onDownload;
        private readonly Func<DownloadRequest, Task<int>> _onInfo;
        private readonly Action<string> _log;
        private readonly CancellationTokenSource _cts = new();

        private Task? _acceptLoop;
        private bool _disposed;

        public HttpBridgeServer(
            Func<DownloadRequest, Task<bool>> onDownload,
            Func<DownloadRequest, Task<int>> onInfo,
            Action<string> log)
        {
            _onDownload = onDownload;
            _onInfo = onInfo;
            _log = log;
        }

        public bool Start()
        {
            string lastError = "";

            foreach (string prefix in new[]
                     {
                         $"http://127.0.0.1:{Port}/",
                         $"http://localhost:{Port}/"
                     })
            {
                try
                {
                    if (_listener.Prefixes.Count > 0) _listener.Prefixes.Clear();
                    _listener.Prefixes.Add(prefix);
                    _listener.Start();

                    _log($"🔒 Локальный сервер запущен: {prefix} (только запросы от расширений)");
                    _acceptLoop = Task.Run(AcceptLoopAsync);
                    return true;
                }
                catch (Exception ex) when (ex is HttpListenerException or UnauthorizedAccessException or InvalidOperationException)
                {
                    lastError = ex.Message;
                }
            }

            _log($"❌ Не удалось запустить локальный сервер на порту {Port}: {lastError}");
            _log("   Кнопка в расширении не будет работать. Попробуйте запустить программу от имени администратора.");
            return false;
        }

        private async Task AcceptLoopAsync()
        {
            while (!_cts.IsCancellationRequested)
            {
                HttpListenerContext context;
                try
                {
                    context = await _listener.GetContextAsync().ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is HttpListenerException or ObjectDisposedException or InvalidOperationException)
                {
                    break;
                }

                _ = Task.Run(() => HandleRequestAsync(context));
            }
        }

        private async Task HandleRequestAsync(HttpListenerContext context)
        {
            try
            {
                HttpListenerRequest request = context.Request;
                HttpListenerResponse response = context.Response;

                string origin = request.Headers["Origin"] ?? "";
                bool originAllowed =
                    origin.StartsWith("chrome-extension://", StringComparison.OrdinalIgnoreCase) ||
                    origin.StartsWith("moz-extension://", StringComparison.OrdinalIgnoreCase);

                if (!originAllowed)
                {
                    await WriteJsonAsync(response, 403, "{\"status\":\"forbidden\"}", null).ConfigureAwait(false);
                    return;
                }

                if (request.HttpMethod == "OPTIONS")
                {
                    response.StatusCode = 204;
                    ApplyCors(response, origin);
                    response.Close();
                    return;
                }

                if (request.HttpMethod != "POST")
                {
                    await WriteJsonAsync(response, 405, "{\"status\":\"method_not_allowed\"}", origin).ConfigureAwait(false);
                    return;
                }

                string path = request.Url?.AbsolutePath ?? "";
                if (path != "/download" && path != "/get_info")
                {
                    await WriteJsonAsync(response, 404, "{\"status\":\"not_found\"}", origin).ConfigureAwait(false);
                    return;
                }

                string? body = await ReadBodyAsync(request).ConfigureAwait(false);
                if (body == null)
                {
                    await WriteJsonAsync(response, 413, "{\"status\":\"payload_too_large\"}", origin).ConfigureAwait(false);
                    return;
                }

                DownloadRequest? downloadRequest;
                try
                {
                    downloadRequest = JsonSerializer.Deserialize<DownloadRequest>(body, JsonOptions);
                }
                catch (JsonException)
                {
                    await WriteJsonAsync(response, 400, "{\"status\":\"bad_json\"}", origin).ConfigureAwait(false);
                    return;
                }

                if (downloadRequest == null || !VideoUtils.IsValidYouTubeUrl(downloadRequest.Url))
                {
                    await WriteJsonAsync(response, 400, "{\"status\":\"bad_url\"}", origin).ConfigureAwait(false);
                    return;
                }

                if (path == "/download")
                {
                    bool accepted = await _onDownload(downloadRequest).ConfigureAwait(false);

                    if (accepted)
                    {
                        await WriteJsonAsync(response, 200, "{\"status\":\"success\"}", origin).ConfigureAwait(false);
                    }
                    else
                    {
                        await WriteJsonAsync(response, 429, "{\"status\":\"queue_full\"}", origin).ConfigureAwait(false);
                    }
                }
                else
                {
                    int duration = await _onInfo(downloadRequest).ConfigureAwait(false);
                    string json = JsonSerializer.Serialize(new { status = "success", duration });
                    await WriteJsonAsync(response, 200, json, origin).ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                _log("Ошибка обработки запроса: " + ex.Message);
                try
                {
                    context.Response.StatusCode = 500;
                    context.Response.Close();
                }
                catch
                {
                    // ignore
                }
            }
        }

        private static async Task<string?> ReadBodyAsync(HttpListenerRequest request)
        {
            if (request.ContentLength64 > MaxBodyChars) return null;

            using StreamReader reader = new(request.InputStream, request.ContentEncoding ?? Encoding.UTF8);
            StringBuilder builder = new();
            char[] buffer = new char[8192];

            int read;
            while ((read = await reader.ReadAsync(buffer.AsMemory(0, buffer.Length)).ConfigureAwait(false)) > 0)
            {
                builder.Append(buffer, 0, read);
                if (builder.Length > MaxBodyChars) return null;
            }

            return builder.ToString();
        }

        private static async Task WriteJsonAsync(
            HttpListenerResponse response,
            int statusCode,
            string json,
            string? origin)
        {
            try
            {
                byte[] buffer = Encoding.UTF8.GetBytes(json);
                response.StatusCode = statusCode;
                response.ContentType = "application/json; charset=utf-8";
                response.ContentLength64 = buffer.Length;

                ApplyCors(response, origin);

                await response.OutputStream.WriteAsync(buffer).ConfigureAwait(false);
                response.Close();
            }
            catch
            {
                try
                {
                    response.Abort();
                }
                catch
                {
                    // ignore
                }
            }
        }

        private static void ApplyCors(HttpListenerResponse response, string? origin)
        {
            if (string.IsNullOrEmpty(origin)) return;

            response.Headers["Access-Control-Allow-Origin"] = origin;
            response.Headers["Vary"] = "Origin";
            response.Headers["Access-Control-Allow-Methods"] = "POST, OPTIONS";
            response.Headers["Access-Control-Allow-Headers"] = "Content-Type";
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            try
            {
                _cts.Cancel();
                _listener.Stop();
                _listener.Close();
            }
            catch
            {
                // ignore
            }

            _cts.Dispose();
            _acceptLoop = null;
        }
    }
}
