using System;
using System.IO;
using System.Net;
using System.Web.Services.Protocols;
using Blackbaud.AppFx.WebAPI;
using Blackbaud.AppFx.WebAPI.ServiceProxy;

namespace InfinityAuthNiuTest
{
    class Program
    {
        static int Main(string[] args)
        {
            try
            {
                var settings = LoadSettings();
                var provider = CreateProvider(settings);

                Console.WriteLine($"URL:      {settings.ServiceUrl}");
                Console.WriteLine($"Database: {settings.Database}");
                Console.WriteLine($"User:     {FormatUser(settings)}");
                Console.WriteLine($"Auth:     Basic");
                Console.WriteLine();

                Console.WriteLine("Calling GetAvailableREDatabases...");
                var req = provider.CreateRequest<GetAvailableREDatabasesRequest>();
                var reply = provider.Service.GetAvailableREDatabases(req);

                if (reply.Databases == null || reply.Databases.Length == 0)
                {
                    Console.WriteLine("PASS - connected, but no databases returned.");
                    return 0;
                }

                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine($"PASS - {reply.Databases.Length} database(s):");
                Console.ResetColor();
                foreach (var db in reply.Databases)
                    Console.WriteLine($"  {db}");

                return 0;
            }
            catch (SoapException soapEx)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"FAIL - SOAP error: {soapEx.Message}");
                Console.ResetColor();
                return 1;
            }
            catch (WebException webEx)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.Write("FAIL - ");
                if (webEx.Response is HttpWebResponse resp)
                    Console.WriteLine($"HTTP {(int)resp.StatusCode} {resp.StatusDescription}");
                else
                    Console.WriteLine(webEx.Message);
                Console.ResetColor();
                return 1;
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"FAIL - {ex.Message}");
                Console.ResetColor();
                return 1;
            }
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

        static AppFxWebServiceProvider CreateProvider(Settings settings)
        {
            var provider = new AppFxWebServiceProvider();
            provider.Url = settings.ServiceUrl;
            provider.Database = settings.Database;
            provider.ApplicationName = "InfinityAuthNiuTest";

            var cache = new CredentialCache();
            cache.Add(new Uri(settings.ServiceUrl), "Basic",
                string.IsNullOrEmpty(settings.Domain)
                    ? new NetworkCredential(settings.Username, settings.Password)
                    : new NetworkCredential(settings.Username, settings.Password, settings.Domain));
            provider.Credentials = cache;

            return provider;
        }

        static string FormatUser(Settings settings)
        {
            return string.IsNullOrEmpty(settings.Domain)
                ? settings.Username
                : $"{settings.Domain}\\{settings.Username}";
        }
    }

    /// <summary>
    /// Minimal JSON settings reader — avoids Newtonsoft dependency.
    /// </summary>
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
