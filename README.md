# InfinityAuthNiuTest

Tests Basic authentication against a canary Infinity instance using the `Blackbaud.AppFx.WebAPI` library with NIU (Non Interactive User) credentials.

## What is an NIU / Proxy User?

An NIU (Non Interactive User), also called a proxy user, is a copy of an application user that inherits the application user's system roles and permissions but authenticates with a PAT (Personal Access Token) instead of traditional username/password credentials. Key characteristics:

- Authenticates using a **proxy username + PAT** via Basic auth (not Windows/NTLM credentials)
- Non-interactive — no email address, cannot access the webshell, but can access API endpoints
- Permissions cannot exceed their proxy owner's permissions
- Can only have 2 active PATs simultaneously
- Marked inactive after 5 consecutive failed authentication attempts

To create a proxy user: navigate to the Application Users page, select "Add Proxy", assign a proxy owner, then generate a PAT from the Personal Access Token tab. The token must be copied immediately — it is not retrievable later.

For full documentation, see: [CRM Service Pack 32 — Proxy User](https://webfiles-sc1.blackbaud.com/files/support/helpfiles/crm/us/40/Content/service_packs/crm-service-pack-32.html)

## What This App Does

Calls `GetAvailableREDatabases` via `AppFxWebServiceProvider` using Basic auth through a `CredentialCache` with NIU credentials.

The `appsettings.json` `Username` field is the proxy user name and `Password` is the PAT.

## Configuration

Edit `appsettings.json` with your NIU proxy username and PAT:

```json
{
  "ServiceUrl": "https://crm5740s29.sky.blackbaud.com/5740S29/appfxwebservice.asmx",
  "Database": "5740S29",
  "Username": "<proxy username>",
  "Password": "<PAT>",
  "Domain": ""
}
```

Note: `Domain` should be empty for NIU/proxy users — they authenticate via Basic auth with a PAT, not Windows domain credentials.

## Build & Run

```
MSBuild InfinityAuthNiuTest.csproj -t:Build -p:Configuration=Debug
bin\Debug\InfinityAuthNiuTest.exe
```
