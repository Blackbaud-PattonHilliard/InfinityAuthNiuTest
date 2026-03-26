using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;
using System.Web.Services.Protocols;
using Blackbaud.AppFx.WebAPI;
using Blackbaud.AppFx.WebAPI.ServiceProxy;

namespace InfinityAuthNiuTest
{
    class Program
    {
        static StreamWriter _log;

        static int Main(string[] args)
        {
            var settings = LoadSettings();

            // When using a proxy (Fiddler), trust its HTTPS interception certificate
            if (!string.IsNullOrEmpty(settings.ProxyUrl))
            {
                ServicePointManager.ServerCertificateValidationCallback =
                    (object sender, X509Certificate cert, X509Chain chain, SslPolicyErrors errors) => true;
            }

            var hostShort = new Uri(settings.ServiceUrl).Host.Split('.')[0];
            var logName = $"niu-test-{hostShort}-{DateTime.Now:yyyyMMdd-HHmmss}.log";
            var logPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, logName);
            _log = new StreamWriter(logPath, append: true) { AutoFlush = true };

            try
            {
                Log("════════════════════════════════════════════════════════════════");
                Log("InfinityAuthNiuTest — Preemptive Basic Auth Test");
                Log("════════════════════════════════════════════════════════════════");
                Log($"URL:        {settings.ServiceUrl}");
                Log($"Database:   {settings.Database}");
                Log($"User:       {FormatUser(settings)}");
                Log($"Auth:       Basic (preemptive)");
                if (!string.IsNullOrEmpty(settings.ProxyUrl))
                    Log($"Proxy:      {settings.ProxyUrl}");
                Log($"Log:        {logPath}");
                Log("");

                // Network info
                Log($"Public IP:   {GetPublicIp()}");
                Log($"Local IPs:   {GetLocalIps()}");
                Log($"Hostname:    {Dns.GetHostName()}");

                // Resolve server IP
                var serverHost = new Uri(settings.ServiceUrl).Host;
                Log($"Server Host: {serverHost}");
                Log($"Server IPs:  {ResolveHost(serverHost)}");

                // ServicePoint info
                var sp = ServicePointManager.FindServicePoint(new Uri(settings.ServiceUrl));
                Log($"TLS:         {ServicePointManager.SecurityProtocol}");
                Log($"Conn Limit:  {sp.ConnectionLimit}");
                Log("");

                // Capture server endpoint for source port lookups
                var serverEndpoint = ResolveServerEndpoint(serverHost);

                const int iterations = 10;
                const int delaySeconds = 5;
                int rawHttpPasses = 0, rawHttpFailures = 0;
                int webApiPasses = 0, webApiFailures = 0;

                Log($"Running {iterations} iterations with {delaySeconds}s delay between each");
                Log("────────────────────────────────────────────────────────────────");
                Log("");

                for (int i = 1; i <= iterations; i++)
                {
                    Log($"── Iteration {i}/{iterations} ──");
                    Log("");

                    // Test 1: Raw HTTP
                    Log("  Test 1: Raw HTTP with preemptive Basic auth");
                    if (RunRawHttpTest(settings, serverEndpoint) == 0)
                        rawHttpPasses++;
                    else
                        rawHttpFailures++;
                    Log("");

                    // Test 2: WebAPI subclass
                    Log("  Test 2: WebAPI (PreemptiveBasicAuthProvider subclass)");
                    if (RunWebApiSubclassTest(settings, serverEndpoint) == 0)
                        webApiPasses++;
                    else
                        webApiFailures++;
                    Log("");

                    if (i < iterations)
                    {
                        Log($"  Waiting {delaySeconds}s...");
                        Thread.Sleep(delaySeconds * 1000);
                    }
                }

                // Summary
                Log("════════════════════════════════════════════════════════════════");
                Log("SUMMARY");
                Log("════════════════════════════════════════════════════════════════");
                Log($"  Test 1 (Raw HTTP):      Passes: {rawHttpPasses}/{iterations}  Failures: {rawHttpFailures}/{iterations}");
                Log($"  Test 2 (WebAPI subclass): Passes: {webApiPasses}/{iterations}  Failures: {webApiFailures}/{iterations}");
                var totalFailures = rawHttpFailures + webApiFailures;
                Log($"  Result:   {(totalFailures == 0 ? "ALL PASSED" : $"{totalFailures} FAILED")}");
                Log("");

                return totalFailures > 0 ? 1 : 0;
            }
            finally
            {
                _log?.Dispose();
            }
        }

        // ─────────────────────────────────────────────────────────────
        // Test 1: Raw HTTP — no Blackbaud DLLs needed
        // ─────────────────────────────────────────────────────────────
        static int RunRawHttpTest(Settings settings, IPEndPoint serverEndpoint)
        {
            var sw = Stopwatch.StartNew();

            try
            {
                Log("  >> DataListGetMetaData (Phones List)");

                var soapBody = BuildDataListGetMetaDataEnvelope(settings.Database);

                var request = (HttpWebRequest)WebRequest.Create(settings.ServiceUrl);
                request.Method = "POST";
                request.ContentType = "text/xml; charset=utf-8";
                request.Headers.Add("SOAPAction", "\"Blackbaud.AppFx.WebService.API.1/DataListGetMetaData\"");

                if (!string.IsNullOrEmpty(settings.ProxyUrl))
                    request.Proxy = new WebProxy(settings.ProxyUrl);

                // Preemptive Basic auth — send credentials on first request
                var credentials = Convert.ToBase64String(Encoding.ASCII.GetBytes(
                    $"{settings.Username}:{settings.Password}"));
                request.Headers.Add("Authorization", $"Basic {credentials}");

                var bodyBytes = Encoding.UTF8.GetBytes(soapBody);
                request.ContentLength = bodyBytes.Length;
                using (var stream = request.GetRequestStream())
                    stream.Write(bodyBytes, 0, bodyBytes.Length);

                // Log request
                Log("  Request:");
                Log($"     {request.Method} {request.RequestUri}");
                foreach (string header in request.Headers)
                {
                    var val = request.Headers[header];
                    if (val != null && val.Length > 200)
                        val = val.Substring(0, 200) + "...";
                    Log($"     {header}: {val}");
                }

                using (var response = (HttpWebResponse)request.GetResponse())
                using (var reader = new StreamReader(response.GetResponseStream()))
                {
                    var responseText = reader.ReadToEnd();
                    sw.Stop();

                    Log($"  << 200 OK ({sw.ElapsedMilliseconds}ms)", ConsoleColor.Green);

                    // Log response
                    Log("  Response:");
                    Log($"     Status: {(int)response.StatusCode} {response.StatusDescription}");
                    if (!string.IsNullOrEmpty(response.Server))
                        Log($"     Server: {response.Server}");
                    foreach (string header in response.Headers)
                    {
                        var val = response.Headers[header];
                        if (val != null && val.Length > 200)
                            val = val.Substring(0, 200) + "...";
                        Log($"     {header}: {val}");
                    }

                    Log($"  Response Body (first 500 chars): {responseText.Substring(0, Math.Min(500, responseText.Length))}");
                    LogSourcePort(serverEndpoint);
                }
                return 0;
            }
            catch (WebException webEx)
            {
                sw.Stop();
                if (webEx.Response is HttpWebResponse resp)
                {
                    Log($"  << HTTP {(int)resp.StatusCode} {resp.StatusDescription} ({sw.ElapsedMilliseconds}ms)", ConsoleColor.Red);
                    Log("  Response:");
                    Log($"     Status: {(int)resp.StatusCode} {resp.StatusDescription}");
                    foreach (string header in resp.Headers)
                    {
                        var val = resp.Headers[header];
                        if (val != null && val.Length > 200)
                            val = val.Substring(0, 200) + "...";
                        Log($"     {header}: {val}");
                    }
                    try
                    {
                        using (var reader = new StreamReader(resp.GetResponseStream()))
                        {
                            var body = reader.ReadToEnd();
                            if (!string.IsNullOrWhiteSpace(body))
                                Log($"  Response Body: {body.Substring(0, Math.Min(500, body.Length))}");
                        }
                    }
                    catch { }
                }
                else
                {
                    Log($"  << WebException ({sw.ElapsedMilliseconds}ms): {webEx.Status} — {webEx.Message}", ConsoleColor.Red);
                    if (webEx.InnerException != null)
                        Log($"     Inner: {webEx.InnerException.GetType().Name}: {webEx.InnerException.Message}");
                }
                LogSourcePort(serverEndpoint);
                return 1;
            }
            catch (Exception ex)
            {
                sw.Stop();
                Log($"  << EXCEPTION ({sw.ElapsedMilliseconds}ms): {ex.GetType().Name}: {ex.Message}", ConsoleColor.Red);
                if (ex.InnerException != null)
                    Log($"     Inner: {ex.InnerException.GetType().Name}: {ex.InnerException.Message}");
                LogSourcePort(serverEndpoint);
                return 1;
            }
        }

        // ─────────────────────────────────────────────────────────────
        // Test 2: WebAPI with subclassed provider/proxy
        // ─────────────────────────────────────────────────────────────
        static int RunWebApiSubclassTest(Settings settings, IPEndPoint serverEndpoint)
        {
            var sw = Stopwatch.StartNew();

            try
            {
                Log("  >> GetAvailableREDatabases via WebAPI");

                var diagService = new DiagnosticPreemptiveBasicAuthWebService(
                    settings.Username, settings.Password);

                var provider = new DiagnosticPreemptiveBasicAuthProvider(diagService);
                provider.Url = settings.ServiceUrl;
                provider.Database = settings.Database;
                provider.ApplicationName = "InfinityAuthNiuTest";
                provider.ProxyUrl = settings.ProxyUrl;

                var req = provider.CreateRequest<GetAvailableREDatabasesRequest>();
                var reply = provider.Service.GetAvailableREDatabases(req);
                sw.Stop();

                if (reply.Databases == null || reply.Databases.Length == 0)
                {
                    Log($"  << 200 OK — no databases returned ({sw.ElapsedMilliseconds}ms)", ConsoleColor.Yellow);
                }
                else
                {
                    Log($"  << 200 OK — {reply.Databases.Length} database(s) ({sw.ElapsedMilliseconds}ms)", ConsoleColor.Green);
                    foreach (var db in reply.Databases)
                        Log($"     {db}");
                }

                LogDiagRequest(diagService);
                LogDiagResponse(diagService);
                LogSourcePort(serverEndpoint);
                return 0;
            }
            catch (SoapException soapEx)
            {
                sw.Stop();
                Log($"  << SOAP ERROR ({sw.ElapsedMilliseconds}ms): {soapEx.Message}", ConsoleColor.Red);
                LogSourcePort(serverEndpoint);
                return 1;
            }
            catch (WebException webEx)
            {
                sw.Stop();
                if (webEx.Response is HttpWebResponse resp)
                {
                    Log($"  << HTTP {(int)resp.StatusCode} {resp.StatusDescription} ({sw.ElapsedMilliseconds}ms)", ConsoleColor.Red);
                    Log("  Response:");
                    Log($"     Status: {(int)resp.StatusCode} {resp.StatusDescription}");
                    foreach (string header in resp.Headers)
                    {
                        var val = resp.Headers[header];
                        if (val != null && val.Length > 200)
                            val = val.Substring(0, 200) + "...";
                        Log($"     {header}: {val}");
                    }
                    try
                    {
                        using (var reader = new StreamReader(resp.GetResponseStream()))
                        {
                            var body = reader.ReadToEnd();
                            if (!string.IsNullOrWhiteSpace(body))
                                Log($"  Response Body: {body.Substring(0, Math.Min(500, body.Length))}");
                        }
                    }
                    catch { }
                }
                else
                {
                    Log($"  << WebException ({sw.ElapsedMilliseconds}ms): {webEx.Status} — {webEx.Message}", ConsoleColor.Red);
                    if (webEx.InnerException != null)
                        Log($"     Inner: {webEx.InnerException.GetType().Name}: {webEx.InnerException.Message}");
                }
                LogSourcePort(serverEndpoint);
                return 1;
            }
            catch (Exception ex)
            {
                sw.Stop();
                Log($"  << EXCEPTION ({sw.ElapsedMilliseconds}ms): {ex.GetType().Name}: {ex.Message}", ConsoleColor.Red);
                if (ex.InnerException != null)
                    Log($"     Inner: {ex.InnerException.GetType().Name}: {ex.InnerException.Message}");
                LogSourcePort(serverEndpoint);
                return 1;
            }
        }

        // ─────────────────────────────────────────────────────────────
        // Diagnostic subclass — preemptive Basic auth + header capture
        // ─────────────────────────────────────────────────────────────

        class DiagnosticPreemptiveBasicAuthWebService : AppFxWebService
        {
            readonly string _authHeader;

            public WebHeaderCollection LastRequestHeaders { get; private set; }
            public WebHeaderCollection LastResponseHeaders { get; private set; }
            public string LastRequestMethod { get; private set; }
            public Uri LastRequestUri { get; private set; }
            public HttpStatusCode? LastResponseStatus { get; private set; }
            public string LastResponseServer { get; private set; }

            public DiagnosticPreemptiveBasicAuthWebService(string username, string password)
            {
                _authHeader = "Basic " + Convert.ToBase64String(
                    Encoding.ASCII.GetBytes($"{username}:{password}"));
            }

            protected override WebRequest GetWebRequest(Uri uri)
            {
                var request = base.GetWebRequest(uri);
                request.Headers["Authorization"] = _authHeader;
                LastRequestUri = uri;
                LastResponseHeaders = null;
                LastResponseStatus = null;
                LastResponseServer = null;

                if (request is HttpWebRequest httpReq)
                {
                    LastRequestMethod = httpReq.Method;
                    LastRequestHeaders = httpReq.Headers;
                }

                return request;
            }

            protected override WebResponse GetWebResponse(WebRequest request)
            {
                var response = base.GetWebResponse(request);
                CaptureResponse(response);
                return response;
            }

            protected override WebResponse GetWebResponse(WebRequest request, IAsyncResult result)
            {
                var response = base.GetWebResponse(request, result);
                CaptureResponse(response);
                return response;
            }

            void CaptureResponse(WebResponse response)
            {
                if (response is HttpWebResponse httpResp)
                {
                    LastResponseHeaders = httpResp.Headers;
                    LastResponseStatus = httpResp.StatusCode;
                    LastResponseServer = httpResp.Server;
                }
            }
        }

        class DiagnosticPreemptiveBasicAuthProvider : AppFxWebServiceProvider
        {
            readonly DiagnosticPreemptiveBasicAuthWebService _svc;

            public DiagnosticPreemptiveBasicAuthProvider(DiagnosticPreemptiveBasicAuthWebService svc)
            {
                _svc = svc;
            }

            public string ProxyUrl { get; set; }

            public override AppFxWebService CreateAppFxWebService()
            {
                _svc.Url = this.Url;

                if (!string.IsNullOrEmpty(ProxyUrl))
                    _svc.Proxy = new WebProxy(ProxyUrl);

                return _svc;
            }
        }

        // ─────────────────────────────────────────────────────────────
        // Unified logging — same format as InfinityAuthWebApiTest
        // ─────────────────────────────────────────────────────────────

        static void LogDiagRequest(DiagnosticPreemptiveBasicAuthWebService svc)
        {
            Log("  Request:");
            if (svc.LastRequestUri != null)
                Log($"     {svc.LastRequestMethod ?? "POST"} {svc.LastRequestUri}");

            if (svc.LastRequestHeaders != null)
            {
                foreach (string header in svc.LastRequestHeaders)
                {
                    var val = svc.LastRequestHeaders[header];
                    if (val != null && val.Length > 200)
                        val = val.Substring(0, 200) + "...";
                    Log($"     {header}: {val}");
                }
            }
            else
            {
                Log("     (request headers not captured)");
            }
        }

        static void LogDiagResponse(DiagnosticPreemptiveBasicAuthWebService svc)
        {
            Log("  Response:");
            if (svc.LastResponseHeaders != null)
            {
                if (svc.LastResponseStatus.HasValue)
                    Log($"     Status: {(int)svc.LastResponseStatus.Value} {svc.LastResponseStatus.Value}");
                if (!string.IsNullOrEmpty(svc.LastResponseServer))
                    Log($"     Server: {svc.LastResponseServer}");
                foreach (string header in svc.LastResponseHeaders)
                {
                    var val = svc.LastResponseHeaders[header];
                    if (val != null && val.Length > 200)
                        val = val.Substring(0, 200) + "...";
                    Log($"     {header}: {val}");
                }
            }
            else
            {
                Log("     (response headers not captured via diagnostic proxy — see above for WebException details)");
            }
        }

        // ─────────────────────────────────────────────────────────────
        // Source port logging — find active TCP connection to server
        // ─────────────────────────────────────────────────────────────

        static IPEndPoint ResolveServerEndpoint(string hostname)
        {
            try
            {
                var entry = Dns.GetHostEntry(hostname);
                var ip = entry.AddressList.FirstOrDefault();
                if (ip != null)
                    return new IPEndPoint(ip, 443);
            }
            catch { }
            return null;
        }

        static void LogSourcePort(IPEndPoint serverEndpoint)
        {
            if (serverEndpoint == null)
            {
                Log("  Source Port: (server endpoint unknown)");
                return;
            }

            try
            {
                var connections = IPGlobalProperties.GetIPGlobalProperties()
                    .GetActiveTcpConnections()
                    .Where(c => c.RemoteEndPoint.Address.Equals(serverEndpoint.Address)
                             && c.RemoteEndPoint.Port == serverEndpoint.Port)
                    .ToArray();

                if (connections.Length > 0)
                {
                    foreach (var conn in connections)
                    {
                        Log($"  Source Port: {conn.LocalEndPoint.Port} → {conn.RemoteEndPoint} (State: {conn.State})");
                    }
                }
                else
                {
                    Log("  Source Port: (no active TCP connection to server — connection already closed)");
                }
            }
            catch (Exception ex)
            {
                Log($"  Source Port: (unable to determine: {ex.Message})");
            }
        }

        // ─────────────────────────────────────────────────────────────
        // Helpers
        // ─────────────────────────────────────────────────────────────

        static string BuildDataListGetMetaDataEnvelope(string database)
        {
            return $@"<?xml version=""1.0"" encoding=""utf-8""?>
<soap:Envelope xmlns:soap=""http://schemas.xmlsoap.org/soap/envelope/""
               xmlns:bb=""Blackbaud.AppFx.WebService.API.1"">
  <soap:Body>
    <bb:DataListGetMetaDataRequest>
      <bb:ClientAppInfo
        REDatabaseToUse=""{database}""
        ClientAppName=""InfinityAuthNiuTest""
        TimeOutSeconds=""30"" />
      <bb:DataListID>846E0526-3B1E-4D4E-A973-1BE20ECB8288</bb:DataListID>
    </bb:DataListGetMetaDataRequest>
  </soap:Body>
</soap:Envelope>";
        }

        static void Log(string message, ConsoleColor? color = null)
        {
            var timestamped = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {message}";

            _log.WriteLine(timestamped);

            if (color.HasValue)
                Console.ForegroundColor = color.Value;
            Console.WriteLine(timestamped);
            if (color.HasValue)
                Console.ResetColor();
        }

        static string GetPublicIp()
        {
            try
            {
                using (var client = new WebClient())
                    return client.DownloadString("https://api.ipify.org").Trim();
            }
            catch (Exception ex)
            {
                return $"(unable to determine: {ex.Message})";
            }
        }

        static string GetLocalIps()
        {
            try
            {
                var host = Dns.GetHostEntry(Dns.GetHostName());
                var ips = host.AddressList
                    .Where(a => a.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                    .Select(a => a.ToString())
                    .ToArray();
                return ips.Length > 0 ? string.Join(", ", ips) : "(none)";
            }
            catch
            {
                return "(unable to determine)";
            }
        }

        static string ResolveHost(string hostname)
        {
            try
            {
                var entry = Dns.GetHostEntry(hostname);
                var ips = entry.AddressList
                    .Select(a => a.ToString())
                    .ToArray();
                return ips.Length > 0 ? string.Join(", ", ips) : "(no addresses)";
            }
            catch (Exception ex)
            {
                return $"(unable to resolve: {ex.Message})";
            }
        }

        static string FormatUser(Settings settings)
        {
            return settings.Username;
        }

        static Settings LoadSettings()
        {
            var configPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "appsettings.json");
            if (!File.Exists(configPath))
            {
                Console.WriteLine($"Missing: {configPath}");
                Environment.Exit(1);
            }

            var json = File.ReadAllText(configPath);
            return new Settings(json);
        }
    }

    class Settings
    {
        public string ServiceUrl { get; }
        public string Database { get; }
        public string Username { get; }
        public string Password { get; }
        public string ProxyUrl { get; }

        public Settings(string json)
        {
            ServiceUrl = Extract(json, "ServiceUrl");
            Database = Extract(json, "Database");
            Username = Extract(json, "Username");
            Password = Extract(json, "Password");
            ProxyUrl = Extract(json, "ProxyUrl");
        }

        static string Extract(string json, string key)
        {
            var search = $"\"{key}\"";
            var idx = json.IndexOf(search, StringComparison.OrdinalIgnoreCase);
            if (idx < 0) return "";

            var colonIdx = json.IndexOf(':', idx + search.Length);
            if (colonIdx < 0) return "";

            var quoteStart = json.IndexOf('"', colonIdx + 1);
            if (quoteStart < 0) return "";

            var quoteEnd = json.IndexOf('"', quoteStart + 1);
            if (quoteEnd < 0) return "";

            return json.Substring(quoteStart + 1, quoteEnd - quoteStart - 1);
        }
    }
}
