// All comments in English as requested
using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using Spirit.Core.Config;
using Spirit.Core.Utils;

namespace Spirit.Core.Services
{
    public sealed class DiscordService
    {
        private readonly AppConfig _cfg;
        private readonly HttpClient _http;

        public DiscordService(AppConfig cfg)
        {
            _cfg = cfg;

            // Use SocketsHttpHandler to keep long-running server connections healthy
            var handler = new SocketsHttpHandler
            {
                // recycle pooled connections regularly to avoid stale keep-alives
                PooledConnectionLifetime = TimeSpan.FromMinutes(2),
                PooledConnectionIdleTimeout = TimeSpan.FromSeconds(30),
                MaxConnectionsPerServer = 16,
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
            };

            _http = new HttpClient(handler)
            {
                BaseAddress = new Uri("https://discord.com/api/"),
                Timeout = TimeSpan.FromSeconds(15)
            };

            // Helpful default headers
            _http.DefaultRequestHeaders.UserAgent.ParseAdd("Spirit-RL/0.1 (+https://spirit.local)");
            _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        }

        public sealed class TokenResponse
        {
            [JsonPropertyName("access_token")] public string AccessToken { get; set; } = "";
            [JsonPropertyName("token_type")] public string TokenType { get; set; } = "";
            [JsonPropertyName("expires_in")] public int ExpiresIn { get; set; }
            [JsonPropertyName("refresh_token")] public string? RefreshToken { get; set; }
            [JsonPropertyName("scope")] public string Scope { get; set; } = "";
        }

        public sealed class MeResponse
        {
            [JsonPropertyName("id")] public string Id { get; set; } = "";
            [JsonPropertyName("username")] public string Username { get; set; } = "";
            [JsonPropertyName("global_name")] public string? GlobalName { get; set; }
        }

        // ====== Transport helpers ======

        // Format nested transport exceptions for logs
        private static string FormatTransportException(Exception ex)
        {
            var type = ex.GetType().Name;
            var msg = ex.Message;
            var inner = ex.InnerException;
            string details = $"{type}: {msg}";
            if (inner is IOException io && io.InnerException is SocketException se)
                details += $" | SocketError={se.SocketErrorCode}";
            else if (inner is SocketException se2)
                details += $" | SocketError={se2.SocketErrorCode}";
            return details;
        }

        private static bool IsTransientTransport(Exception ex)
        {
            return ex is HttpRequestException
                || ex is IOException
                || ex.InnerException is SocketException
                || (ex.InnerException is IOException io && io.InnerException is SocketException);
        }

        // Retry helper that rebuilds the HttpRequestMessage for each attempt
        private async Task<HttpResponseMessage> SendAsyncWithRetry(Func<HttpRequestMessage> make, CancellationToken ct, bool forceClose = false)
        {
            HttpResponseMessage SendOnce()
            {
                var req = make();
                if (forceClose)
                    req.Headers.ConnectionClose = true; // avoid stale pooled connections for critical one-off calls
                return _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct).GetAwaiter().GetResult();
            }

            try
            {
                return SendOnce();
            }
            catch (Exception ex) when (IsTransientTransport(ex))
            {
                Logger.Log("[DiscordService] transient transport error (attempt 1): " + FormatTransportException(ex), Logger.Level.Warn);
            }

            // Brief backoff then retry with a fresh request instance
            await Task.Delay(200, ct);

            try
            {
                return SendOnce();
            }
            catch (Exception ex)
            {
                // Let the caller decide; include rich details in message
                throw new HttpRequestException("[DiscordService] transport failed after retry: " + FormatTransportException(ex), ex);
            }
        }

        // ====== OAuth2 & API calls ======

        // All comments in English as requested
        public async Task<TokenResponse> ExchangeCodeAsync(string code, CancellationToken ct = default)
        {
            // Normalize incoming code
            code = (code ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(code))
                throw new Exception("Discord code is empty.");

            // Ensure redirect_uri is always sent and is exactly the one configured in the portal
            var redirect = string.IsNullOrWhiteSpace(_cfg.Discord.RedirectUri)
                ? "http://127.0.0.1" // fallback for desktop flow (must be whitelisted in the portal)
                : _cfg.Discord.RedirectUri.Trim();

            // Helpful debug (no secrets)
            var cidTail = _cfg.Discord.ClientId?.Length >= 4 ? _cfg.Discord.ClientId[^4..] : _cfg.Discord.ClientId;
            Logger.Log($"[DiscordOAuth] Exchange start: clientId=...{cidTail} | redirect={redirect} | codeLen={code.Length}", Logger.Level.Debug);

            var res = await SendAsyncWithRetry(() =>
            {
                return new HttpRequestMessage(HttpMethod.Post, "oauth2/token")
                {
                    Content = new FormUrlEncodedContent(new[]
                    {
                        new KeyValuePair<string,string>("client_id",     _cfg.Discord.ClientId),
                        new KeyValuePair<string,string>("client_secret", _cfg.Discord.ClientSecret),
                        new KeyValuePair<string,string>("grant_type",    "authorization_code"),
                        new KeyValuePair<string,string>("code",          code),
                        new KeyValuePair<string,string>("redirect_uri",  redirect),
                    })
                };
            }, ct, forceClose: true); // one-off critical call → prefer new TCP

            var raw = await res.Content.ReadAsStringAsync(ct);
            if (!res.IsSuccessStatusCode)
                throw new Exception($"Discord token exchange failed: {(int)res.StatusCode} {res.ReasonPhrase} - {raw}");

            return JsonSerializer.Deserialize<TokenResponse>(raw)!;
        }

        public async Task<MeResponse> GetMeAsync(string accessToken, CancellationToken ct = default)
        {
            var res = await SendAsyncWithRetry(() =>
            {
                var req = new HttpRequestMessage(HttpMethod.Get, "users/@me");
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
                return req;
            }, ct, forceClose: true); // also fine to force a fresh connection here

            var raw = await res.Content.ReadAsStringAsync(ct);
            if (!res.IsSuccessStatusCode)
                throw new Exception($"Discord /users/@me failed: {(int)res.StatusCode} {res.ReasonPhrase} - {raw}");

            return JsonSerializer.Deserialize<MeResponse>(raw)!;
        }

        public async Task<(bool isMember, string[] roles)> CheckGuildMemberAsync(string userId, CancellationToken ct = default)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(_cfg.Discord.GuildId))
                    throw new Exception("Discord GuildId missing in config");

                if (string.IsNullOrWhiteSpace(_cfg.Discord.BotToken))
                    throw new Exception("Discord BotToken missing in config");

                // Strip optional "Bot " prefix for Auth header convenience
                var rawToken = _cfg.Discord.BotToken.StartsWith("Bot ")
                    ? _cfg.Discord.BotToken.Substring(4)
                    : _cfg.Discord.BotToken;

                var res = await SendAsyncWithRetry(() =>
                {
                    var req = new HttpRequestMessage(HttpMethod.Get, $"guilds/{_cfg.Discord.GuildId}/members/{userId}");
                    req.Headers.Authorization = new AuthenticationHeaderValue("Bot", rawToken);
                    return req;
                }, ct /*, forceClose: false*/); // keep-alive is ok for repeated guild checks

                // --- Debug logs ---
                Logger.Log($"[DiscordService] Guild check request: GuildId={_cfg.Discord.GuildId}, UserId={userId}", Logger.Level.Debug);
                Logger.Log($"[DiscordService] BotToken length={_cfg.Discord.BotToken.Length}", Logger.Level.Debug);

                var body = await res.Content.ReadAsStringAsync(ct);

                if (!res.IsSuccessStatusCode)
                {
                    Logger.Log($"Discord guild member check failed: {(int)res.StatusCode} {res.ReasonPhrase} - {body}", Logger.Level.Warn);
                    return (false, Array.Empty<string>());
                }

                var member = JsonSerializer.Deserialize<JsonElement>(body);
                if (member.TryGetProperty("roles", out var rolesElem) && rolesElem.ValueKind == JsonValueKind.Array)
                {
                    var roles = rolesElem.EnumerateArray().Select(r => r.GetString() ?? "").ToArray();
                    return (true, roles);
                }

                return (true, Array.Empty<string>());
            }
            catch (Exception ex)
            {
                Logger.Log("CheckGuildMemberAsync error: " + ex.Message, Logger.Level.Error);
                return (false, Array.Empty<string>());
            }
        }
    }
}
