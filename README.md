# InfinityAuthNiuTest

Demonstrates two workarounds for the NTLM/Basic auth failure through Azure App Gateway v2, both using preemptive `Authorization: Basic` headers.

## The Problem

`SoapHttpClientProtocol` (the base class of `AppFxWebService`) uses challenge-response auth — it sends the first request with no credentials, waits for a 401 + `WWW-Authenticate` challenge, then retries. App Gateway v2 sends `Connection: close` after the 401, breaking the retry. The proxy gives up and the call fails.

Sending the `Authorization: Basic` header preemptively on the first request — as Postman does — bypasses the challenge-response flow entirely and succeeds.

## What This App Does

Runs two tests against the same endpoint:

### Test 1: Raw HTTP (no Blackbaud DLLs)

Sends a hand-built SOAP envelope (`DataListGetMetaData` for the Phones List) via `HttpWebRequest` with a preemptive Basic auth header. This isolates the fix to pure .NET Framework classes with no dependencies.

### Test 2: WebAPI with subclassed provider

Uses the `Blackbaud.AppFx.WebAPI` library with two subclasses that inject preemptive Basic auth:

- **`PreemptiveBasicAuthWebService`** — subclasses `AppFxWebService` (the generated SOAP proxy) and overrides `GetWebRequest()` to add the `Authorization: Basic` header on every request.
- **`PreemptiveBasicAuthProvider`** — subclasses `AppFxWebServiceProvider` and overrides the virtual `CreateAppFxWebService()` to return the custom proxy.

This approach preserves the full WebAPI experience (`CreateRequest<T>`, session management, typed requests/replies) while fixing the auth problem. It demonstrates a drop-in fix for SDK consumers.

```csharp
// The fix — two small subclasses:
class PreemptiveBasicAuthWebService : AppFxWebService
{
    readonly string _authHeader;

    public PreemptiveBasicAuthWebService(string username, string password, string domain)
    {
        var userPart = string.IsNullOrEmpty(domain) ? username : $"{domain}\\{username}";
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

class PreemptiveBasicAuthProvider : AppFxWebServiceProvider
{
    readonly string _username, _password, _domain;

    public PreemptiveBasicAuthProvider(string username, string password, string domain)
    {
        _username = username; _password = password; _domain = domain;
    }

    public override AppFxWebService CreateAppFxWebService()
    {
        var svc = new PreemptiveBasicAuthWebService(_username, _password, _domain);
        svc.Url = this.Url;
        return svc;
    }
}
```

## Prerequisites

Basic authentication must be enabled in IIS on the target AppFxWebService endpoint. If only Windows authentication (NTLM/Negotiate) is enabled, the server will ignore the Basic header.

## What is an NIU / Proxy User?

An NIU (Non Interactive User), also called a proxy user, is a copy of an application user that inherits the application user's system roles and permissions but authenticates with a PAT (Personal Access Token) instead of traditional username/password credentials. Key characteristics:

- Authenticates using a **proxy username + PAT** via Basic auth (not Windows/NTLM credentials)
- Non-interactive — no email address, cannot access the webshell, but can access API endpoints
- Permissions cannot exceed their proxy owner's permissions
- Can only have 2 active PATs simultaneously
- Marked inactive after 5 consecutive failed authentication attempts

To create a proxy user: navigate to the Application Users page, select "Add Proxy", assign a proxy owner, then generate a PAT from the Personal Access Token tab. The token must be copied immediately — it is not retrievable later.

For full documentation, see: [CRM Service Pack 32 — Proxy User](https://webfiles-sc1.blackbaud.com/files/support/helpfiles/crm/us/40/Content/service_packs/crm-service-pack-32.html)

## Configuration

Edit `appsettings.json`:

```json
{
  "ServiceUrl": "https://crm5740s29.sky.blackbaud.com/5740S29/appfxwebservice.asmx",
  "Database": "5740S29",
  "Username": "<username>",
  "Password": "<password or PAT>",
  "Domain": "<domain or empty for NIU>"
}
```

Works with both domain credentials (`Domain` = `"s29"`) and NIU/proxy credentials (`Domain` = `""`).

## Build & Run

```
MSBuild InfinityAuthNiuTest.csproj -t:Build -p:Configuration=Debug
bin\Debug\InfinityAuthNiuTest.exe
```