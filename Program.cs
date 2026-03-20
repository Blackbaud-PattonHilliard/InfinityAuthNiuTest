using System;
using System.IO;
using System.Net;
using System.Text;
using System.Xml;

namespace InfinityAuthNiuTest
{
    class Program
    {
        static int Main(string[] args)
        {
            try
            {
                var settings = LoadSettings();

                Console.WriteLine($"URL:      {settings.ServiceUrl}");
                Console.WriteLine($"Database: {settings.Database}");
                Console.WriteLine($"User:     {FormatUser(settings)}");
                Console.WriteLine($"Auth:     Basic (preemptive)");
                Console.WriteLine();

                // Build SOAP envelope for DataListGetMetaData
                var soapBody = BuildDataListGetMetaDataEnvelope(settings.Database);

                Console.WriteLine("Calling DataListGetMetaData (raw HTTP, preemptive Basic auth)...");
                Console.WriteLine($"SOAPAction: Blackbaud.AppFx.WebService.API.1/DataListGetMetaData");
                Console.WriteLine();

                var request = (HttpWebRequest)WebRequest.Create(settings.ServiceUrl);
                request.Method = "POST";
                request.ContentType = "text/xml; charset=utf-8";
                request.Headers.Add("SOAPAction", "\"Blackbaud.AppFx.WebService.API.1/DataListGetMetaData\"");

                // Preemptive Basic auth — send credentials on first request, no challenge needed
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
                    Console.WriteLine();
                    Console.WriteLine("Response (first 2000 chars):");
                    Console.WriteLine(responseText.Substring(0, Math.Min(2000, responseText.Length)));
                }

                return 0;
            }
            catch (WebException webEx)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.Write("FAIL - ");
                if (webEx.Response is HttpWebResponse resp)
                {
                    Console.WriteLine($"HTTP {(int)resp.StatusCode} {resp.StatusDescription}");
                    using (var reader = new StreamReader(resp.GetResponseStream()))
                        Console.WriteLine(reader.ReadToEnd().Substring(0, Math.Min(2000, (int)resp.ContentLength > 0 ? (int)resp.ContentLength : 2000)));
                }
                else
                {
                    Console.WriteLine(webEx.Message);
                }
                Console.ResetColor();
                return 1;
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"FAIL - {ex.GetType().Name}: {ex.Message}");
                Console.ResetColor();
                return 1;
            }
        }

        static string BuildDataListGetMetaDataEnvelope(string database)
        {
            // Minimal DataListGetMetaData request — just needs a valid DataListID
            // Using a well-known list ID (constituent search) as a test
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
