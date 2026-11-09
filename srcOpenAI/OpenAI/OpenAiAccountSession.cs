using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text.Json.Nodes;

namespace LocalDub.OpenAI;

/// <summary>Manages the application's single protected OpenAI registration and renewable session.</summary>
public sealed class OpenAiAccountSession
{
    private static readonly SemaphoreSlim SessionGate = new(1, 1);
    private readonly HttpClient http;
    private readonly ISecretVault vault;
    private readonly Func<string, Task> openBrowser;
    private readonly string reference;

    /// <summary>Creates an isolated, injectable public-client session service.</summary>
    public OpenAiAccountSession(HttpClient http, ISecretVault vault, string reference = "openai-account-session", Func<string, Task>? openBrowser = null)
    {
        this.http = http;
        this.vault = vault;
        this.reference = reference;
        this.openBrowser = openBrowser ?? (url =>
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            return Task.CompletedTask;
        });
    }

    /// <summary>Prevents concurrent CLI processes from rotating the same refresh token or rendering to the same output.</summary>
    public static FileStream LockUserSession()
    {
        Directory.CreateDirectory(DpapiSecretVault.UserDirectory);
        try
        {
            return new FileStream(Path.Combine(DpapiSecretVault.UserDirectory, "session.lock"),
                FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        }
        catch (IOException error)
        {
            throw new IOException("The LocalDub OpenAI session is in use or its lock file is unavailable.", error);
        }
    }

    /// <summary>Reads only a safe account status without contacting the provider.</summary>
    public string StatusKey
    {
        get
        {
            try
            {
                JsonObject saved = Read();
                return saved["access_token"] is null ? "OpenAiDisconnected" : HasPermission(saved) ? "OpenAiConnected" : "OpenAiPermissionMissing";
            }
            catch { return "OpenAiReconnect"; }
        }
    }

    /// <summary>Authenticates in the system browser, accepting credentials only after signed identity validation.</summary>
    public async Task SignInAsync(IProgress<string>? progress, CancellationToken cancellationToken)
    {
        await SessionGate.WaitAsync(cancellationToken);
        try
        {
            JsonObject previous = Read();
            if (previous["host_id"] is null)
            {
                previous["host_id"] = "urn:uuid:" + Guid.NewGuid();
                Save(previous);
            }
            JsonObject next = previous.DeepClone().AsObject();
            string state = OpenAiAccountProtocol.RandomValue();
            string nonce = OpenAiAccountProtocol.RandomValue();
            string verifier = OpenAiAccountProtocol.RandomValue();
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromMinutes(15));
            using var callback = new OpenAiLoopbackCallback();
            Task<Uri> pending = callback.ReceiveAsync(timeout.Token);
            try
            {
                progress?.Report("OpenAiWaiting");
                string url = OpenAiAccountProtocol.AuthorizationUrl(callback.RedirectUri, (string)next["host_id"]!, state, nonce, verifier, (string?)next["client_id"]);
                if (next["id_token"] is not null) url += "&id_token_hint=" + Uri.EscapeDataString((string)next["id_token"]!);
                if (next["client_id"] is not null && !HasPermission(next)) url += "&prompt=consent";
                await openBrowser(url);
                Uri returned = await pending;
                progress?.Report("OpenAiVerifying");
                var result = OpenAiAccountProtocol.ValidateCallback(returned, callback.RedirectUri, state, (string?)next["client_id"]);
                if (next["client_id"] is null)
                {
                    next["client_id"] = result.ClientId;
                    Save(next);
                }
                JsonObject tokens = await ExchangeAsync(new()
                {
                    ["grant_type"] = "authorization_code", ["client_id"] = result.ClientId,
                    ["code"] = result.Code, ["code_verifier"] = verifier,
                    ["redirect_uri"] = callback.RedirectUri, ["resource"] = OpenAiAccountProtocol.Resource
                }, timeout.Token);
                JsonObject keys = await ReadJsonAsync(OpenAiAccountProtocol.Issuer + "/.well-known/jwks.json", timeout.Token);
                var identity = OpenAiAccountProtocol.ValidateIdToken((string)tokens["id_token"]!, keys, result.ClientId, nonce, DateTimeOffset.UtcNow);
                if (next["subject"] is not null && (string?)next["subject"] != identity.Subject)
                    throw new OpenAiAccountException("OpenAiIdentityMismatch");
                ClearTokens(next);
                ApplyTokens(next, tokens);
                next["client_id"] = result.ClientId;
                next["subject"] = identity.Subject;
                timeout.Token.ThrowIfCancellationRequested();
                Save(next);
                progress?.Report(HasPermission(next) ? "OpenAiConnected" : "OpenAiPermissionMissing");
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                throw new OpenAiAccountException("OpenAiSignInTimeout");
            }
            finally
            {
                timeout.Cancel();
                callback.Dispose();
                try { await pending; } catch { }
            }
        }
        finally { SessionGate.Release(); }
    }

    /// <summary>Serializes refresh and atomically persists rotated credentials before exposing an access token.</summary>
    public async Task<string> GetAccessTokenAsync(CancellationToken token)
    {
        await SessionGate.WaitAsync(token);
        try
        {
            JsonObject saved = Read();
            if (saved["access_token"] is null) throw new OpenAiAccountException("OpenAiReconnect");
            if (!HasPermission(saved)) throw new OpenAiAccountException("OpenAiPermissionMissing");
            if ((long?)saved["expires_at"] is not long expiry || expiry <= DateTimeOffset.UtcNow.AddSeconds(60).ToUnixTimeSeconds())
            {
                if (saved["refresh_token"] is null) throw new OpenAiAccountException("OpenAiReconnect");
                try
                {
                    JsonObject tokens = await ExchangeAsync(new()
                    {
                        ["grant_type"] = "refresh_token", ["client_id"] = (string)saved["client_id"]!,
                        ["refresh_token"] = (string)saved["refresh_token"]!, ["resource"] = OpenAiAccountProtocol.Resource
                    }, token);
                    if (tokens["id_token"] is not null)
                    {
                        JsonObject keys = await ReadJsonAsync(OpenAiAccountProtocol.Issuer + "/.well-known/jwks.json", token);
                        var identity = OpenAiAccountProtocol.ValidateIdToken((string)tokens["id_token"]!, keys, (string)saved["client_id"]!, null, DateTimeOffset.UtcNow);
                        if ((string?)saved["subject"] != identity.Subject) throw new OpenAiAccountException("OpenAiIdentityMismatch");
                    }
                    ApplyTokens(saved, tokens);
                    Save(saved);
                }
                catch (OpenAiAccountException error) when (error.Code is "invalid_grant" or "invalid_refresh_token" or "token_expired" or "refresh_token_expired" or "refresh_token_invalidated" or "refresh_token_reused")
                {
                    ClearTokens(saved);
                    Save(saved);
                    throw new OpenAiAccountException("OpenAiReconnect");
                }
                if (!HasPermission(saved)) throw new OpenAiAccountException("OpenAiPermissionMissing");
            }
            token.ThrowIfCancellationRequested();
            return (string)saved["access_token"]!;
        }
        finally { SessionGate.Release(); }
    }

    /// <summary>Attempts official revocation and removes the local registration and credentials.</summary>
    public async Task<bool> ResetAsync(CancellationToken token)
    {
        await SessionGate.WaitAsync(CancellationToken.None);
        try
        {
            JsonObject saved;
            try { saved = Read(); }
            catch { vault.RemoveSecret(reference); return false; }
            var revocationToken = (string?)(saved["refresh_token"] ?? saved["access_token"]);
            bool confirmed = revocationToken is null;
            try
            {
                if (revocationToken is not null)
                {
                    JsonObject metadata = await ReadJsonAsync(OpenAiAccountProtocol.Issuer + "/.well-known/openid-configuration", token);
                    var endpoint = new Uri((string)metadata["revocation_endpoint"]!);
                    if (endpoint.Scheme != "https" || endpoint.Host != "auth.openai.com" || !endpoint.IsDefaultPort || endpoint.UserInfo.Length != 0)
                        throw new OpenAiAccountException("OpenAiRequestFailed");
                    using var body = new FormUrlEncodedContent(new Dictionary<string, string>
                    {
                        ["token"] = revocationToken,
                        ["token_type_hint"] = saved["refresh_token"] is not null ? "refresh_token" : "access_token",
                        ["client_id"] = (string)saved["client_id"]!
                    });
                    using var response = await http.PostAsync(endpoint, body, token);
                    confirmed = response.StatusCode == System.Net.HttpStatusCode.OK;
                }
            }
            catch { confirmed = false; }
            finally
            {
                vault.RemoveSecret(reference);
            }
            return confirmed;
        }
        finally { SessionGate.Release(); }
    }

    /// <summary>Discovers the account's offered models without generating any text.</summary>
    public async Task<IReadOnlyList<OpenAiAccountModel>> GetModelsAsync(CancellationToken token)
    {
        string accessToken = await GetAccessTokenAsync(token);
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://api.openai.com/v1/models");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        using var response = await http.SendAsync(request, token);
        if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
            await RequireReconnectAsync(accessToken);
        if (!response.IsSuccessStatusCode) throw new OpenAiAccountException("OpenAiModelsFailed");
        JsonObject json = JsonNode.Parse(await response.Content.ReadAsStringAsync(token))!.AsObject();
        return json["models"]!.AsArray().OfType<JsonObject>().Where(model => (string?)model["visibility"] == "list")
            .Select(model => new OpenAiAccountModel((string)model["slug"]!, (string)model["display_name"]!)).ToArray();
    }

    /// <summary>Disables a rejected session without invalidating a concurrently renewed access token.</summary>
    public async Task RequireReconnectAsync(string rejectedToken)
    {
        await SessionGate.WaitAsync(CancellationToken.None);
        try
        {
            JsonObject saved = Read();
            if ((string?)saved["access_token"] != rejectedToken) return;
            ClearTokens(saved);
            try { Save(saved); }
            catch { vault.RemoveSecret(reference); throw; }
        }
        finally { SessionGate.Release(); }
    }

    private JsonObject Read() => vault.TryGetSecret(reference, out string value) ? JsonNode.Parse(value)!.AsObject() : new();

    private void Save(JsonObject saved)
    {
        string value = saved.ToJsonString();
        bool hadPrevious = vault.TryGetSecret(reference, out string previous);
        try
        {
            vault.SetSecret(reference, value);
            if (!vault.TryGetSecret(reference, out string verified) || verified != value)
                throw new OpenAiAccountException("OpenAiStorageFailed");
        }
        catch
        {
            if (hadPrevious) vault.SetSecret(reference, previous);
            else vault.RemoveSecret(reference);
            throw new OpenAiAccountException("OpenAiStorageFailed");
        }
    }

    private static bool HasPermission(JsonObject saved) => OpenAiAccountProtocol.HasPlanScope((string?)saved["scope"]!);

    private static void ClearTokens(JsonObject saved)
    {
        foreach (string name in new[] { "access_token", "refresh_token", "id_token", "scope", "expires_at" }) saved.Remove(name);
    }

    private static void ApplyTokens(JsonObject saved, JsonObject tokens)
    {
        if (string.IsNullOrWhiteSpace((string?)tokens["access_token"]) || (string?)tokens["token_type"] is not string type ||
            !type.Equals("Bearer", StringComparison.OrdinalIgnoreCase) || (long?)tokens["expires_in"] is not > 0)
            throw new OpenAiAccountException("OpenAiInvalidSession");
        foreach (string name in new[] { "access_token", "refresh_token", "id_token", "scope" })
            if (tokens[name] is not null) saved[name] = tokens[name]!.DeepClone();
        saved["expires_at"] = DateTimeOffset.UtcNow.ToUnixTimeSeconds() + (long)tokens["expires_in"]!;
    }

    private async Task<JsonObject> ReadJsonAsync(string url, CancellationToken token)
    {
        using var response = await http.GetAsync(url, token);
        if (!response.IsSuccessStatusCode) throw new OpenAiAccountException("OpenAiRequestFailed");
        return JsonNode.Parse(await response.Content.ReadAsStringAsync(token))!.AsObject();
    }

    private async Task<JsonObject> ExchangeAsync(Dictionary<string, string> form, CancellationToken token)
    {
        using var body = new FormUrlEncodedContent(form);
        using var response = await http.PostAsync(OpenAiAccountProtocol.Issuer + "/api/accounts/oauth/token", body, token);
        if (!response.IsSuccessStatusCode)
        {
            string? code = null;
            try
            {
                JsonNode? error = JsonNode.Parse(await response.Content.ReadAsStringAsync(token))?["error"];
                string? value = error is JsonObject ? (string?)error["code"] : (string?)error;
                if (value is "invalid_grant" or "invalid_refresh_token" or "token_expired" or "refresh_token_expired" or "refresh_token_invalidated" or "refresh_token_reused") code = value;
            }
            catch (System.Text.Json.JsonException) { }
            throw new OpenAiAccountException("OpenAiReconnect", code);
        }
        return JsonNode.Parse(await response.Content.ReadAsStringAsync(token))!.AsObject();
    }
}

/// <summary>Identifies an offered account model without exposing credentials.</summary>
public sealed record OpenAiAccountModel(string Slug, string DisplayName);
