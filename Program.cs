using System;
using System.IO;
using System.Net;
using System.Text;
using System.Web.Services.Protocols;
using Blackbaud.AppFx.WebAPI;
using Blackbaud.AppFx.WebAPI.ServiceProxy;

namespace InfinityAuthNiuTest
{
    class Program
    {
        static int Main(string[] args)
        {
            var settings = LoadSettings();

            Console.WriteLine($"URL:      {settings.ServiceUrl}");
            Console.WriteLine($"Database: {settings.Database}");
            Console.WriteLine($"User:     {FormatUser(settings)}");
            Console.WriteLine();

            int result = 0;

            // Test 1: Raw HTTP with preemptive Basic auth
            Console.WriteLine("════════════════════════════════════════════════════════════════");
            Console.WriteLine("Test 1: Raw HTTP with preemptive Basic auth");
            Console.WriteLine("════════════════════════════════════════════════════════════════");
            result |= RunRawHttpTest(settings);
            Console.WriteLine();

            // Test 2: WebAPI with PreemptiveBasicAuthProvider subclass
            Console.WriteLine("════════════════════════════════════════════════════════════════");
            Console.WriteLine("Test 2: WebAPI (PreemptiveBasicAuthProvider subclass)");
            Console.WriteLine("════════════════════════════════════════════════════════════════");
            result |= RunWebApiSubclassTest(settings);

            return result;
        }

        // ─────────────────────────────────────────────────────────────
        // Test 1: Raw HTTP — no Blackbaud DLLs needed
        // ─────────────────────────────────────────────────────────────
        static int RunRawHttpTest(Settings settings)
        {
            try
            {
                var soapBody = BuildDataListGetMetaDataEnvelope(settings.Database);

                Console.WriteLine("Calling DataListGetMetaData...");

                var request = (HttpWebRequest)WebRequest.Create(settings.ServiceUrl);
                request.Method = "POST";
                request.ContentType = "text/xml; charset=utf-8";
                request.Headers.Add("SOAPAction", "\"Blackbaud.AppFx.WebService.API.1/DataListGetMetaData\"");

                // Preemptive Basic auth — send credentials on first request
                var credentials = Convert.ToBase64String(Encoding.ASCII.GetBytes(
                    string.IsNullOrEmpty(settings.Domain)
                        ? $"{settings.Username}:{settings.Password}"
                        : $"{settings.Domain}\\{settings.Username}:{settings.Password}"));
                request.Headers.Add("Authorization", $"Basic {credentials}");

                var bodyBytes = Encoding.UTF8.GetBytes(soapBody);
                request.ContentLength = bodyBytes.Length;
                using (var stream = request.GetRequestStream())
                    stream.Write(bodyBytes, 0, bodyBytes.Length);

                using (var response = (HttpWebResponse)request.GetResponse())
                using (var reader = new StreamReader(response.GetResponseStream()))
                {
                    var responseText = reader.ReadToEnd();
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine($"PASS - HTTP {(int)response.StatusCode} {response.StatusDescription}");
                    Console.ResetColor();
                    Console.WriteLine($"Response (first 500 chars): {responseText.Substring(0, Math.Min(500, responseText.Length))}");
                }
                return 0;
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"FAIL - {FormatException(ex)}");
                Console.ResetColor();
                return 1;
            }
        }

        // ─────────────────────────────────────────────────────────────
        // Test 2: WebAPI with subclassed provider/proxy
        // ─────────────────────────────────────────────────────────────
        static int RunWebApiSubclassTest(Settings settings)
        {
            try
            {
                var provider = new PreemptiveBasicAuthProvider(
                    settings.Username, settings.Password, settings.Domain);
                provider.Url = settings.ServiceUrl;
                provider.Database = settings.Database;
                provider.ApplicationName = "InfinityAuthNiuTest";

                Console.WriteLine("Calling GetAvailableREDatabases via WebAPI...");

                var req = provider.CreateRequest<GetAvailableREDatabasesRequest>();
                var reply = provider.Service.GetAvailableREDatabases(req);

                if (reply.Databases == null || reply.Databases.Length == 0)
                {
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine("PASS - connected, but no databases returned.");
                    Console.ResetColor();
                    return 0;
                }

                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine($"PASS - {reply.Databases.Length} database(s):");
                Console.ResetColor();
                foreach (var db in reply.Databases)
                    Console.WriteLine($"  {db}");

                return 0;
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"FAIL - {FormatException(ex)}");
                Console.ResetColor();
                return 1;
            }
        }

        // ─────────────────────────────────────────────────────────────
        // Subclasses that inject preemptive Basic auth
        // ─────────────────────────────────────────────────────────────

        /// <summary>
        /// Subclass of AppFxWebService that overrides GetWebRequest to inject
        /// a preemptive Authorization: Basic header on every request.
        /// This avoids the challenge-response flow that fails through App Gateway v2.
        /// </summary>
        class PreemptiveBasicAuthWebService : AppFxWebService
        {
            readonly string _authHeader;

            public PreemptiveBasicAuthWebService(string username, string password, string domain)
            {
                var userPart = string.IsNullOrEmpty(domain)
                    ? username
                    : $"{domain}\\{username}";
                _authHeader = "Basic " + Convert.ToBase64String(
                    Encoding.ASCII.GetBytes($"{userPart}:{password}"));
            }

            protected override WebRequest GetWebRequest(Uri uri)
            {
                var request = base.GetWebRequest(uri);
                request.Headers["Authorization"] = _authHeader;
                return request;
            }
        }

        /// <summary>
        /// Subclass of AppFxWebServiceProvider that overrides CreateAppFxWebService
        /// to return a PreemptiveBasicAuthWebService instead of the default proxy.
        /// </summary>
        class PreemptiveBasicAuthProvider : AppFxWebServiceProvider
        {
            readonly string _username;
            readonly string _password;
            readonly string _domain;

            public PreemptiveBasicAuthProvider(string username, string password, string domain)
            {
                _username = username;
                _password = password;
                _domain = domain;
            }

            public override AppFxWebService CreateAppFxWebService()
            {
                var svc = new PreemptiveBasicAuthWebService(_username, _password, _domain);
                svc.Url = this.Url;
                return svc;
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

        static string FormatException(Exception ex)
        {
            if (ex is WebException webEx && webEx.Response is HttpWebResponse resp)
                return $"HTTP {(int)resp.StatusCode} {resp.StatusDescription}";
            if (ex is SoapException soapEx)
                return $"SOAP error: {soapEx.Message}";
            return $"{ex.GetType().Name}: {ex.Message}";
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

        static string FormatUser(Settings settings)
        {
            return string.IsNullOrEmpty(settings.Domain)
                ? settings.Username
                : $"{settings.Domain}\\{settings.Username}";
        }
    }

    class Settings
    {
        public string ServiceUrl { get; }
        public string Database { get; }
        public string Username { get; }
        public string Password { get; }
        public string Domain { get; }

        public Settings(string json)
        {
            ServiceUrl = Extract(json, "ServiceUrl");
            Database = Extract(json, "Database");
            Username = Extract(json, "Username");
            Password = Extract(json, "Password");
            Domain = Extract(json, "Domain");
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
