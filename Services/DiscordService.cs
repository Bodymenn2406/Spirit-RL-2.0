// All comments in English as requested
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
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
            _http = new HttpClient
            {
                BaseAddress = new Uri("https://discord.com/api/")
            };
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

        public async Task<TokenResponse> ExchangeCodeAsync(string code, CancellationToken ct = default)
        {
            // Build body
            var kv = new List<KeyValuePair<string, string>>
            {
                new("client_id", _cfg.Discord.ClientId),
                new("client_secret", _cfg.Discord.ClientSecret),
                new("grant_type", "authorization_code"),
                new("code", code)
            };
            if (!string.IsNullOrWhiteSpace(_cfg.Discord.RedirectUri))
                kv.Add(new("redirect_uri", _cfg.Discord.RedirectUri));

            using var req = new HttpRequestMessage(HttpMethod.Post, "oauth2/token")
            {
                Content = new FormUrlEncodedContent(kv)
            };
            using var res = await _http.SendAsync(req, ct);
            var raw = await res.Content.ReadAsStringAsync(ct);
            if (!res.IsSuccessStatusCode)
                throw new Exception($"Discord token exchange failed: {(int)res.StatusCode} {res.ReasonPhrase} - {raw}");

            return JsonSerializer.Deserialize<TokenResponse>(raw)!;
        }

        public async Task<MeResponse> GetMeAsync(string accessToken, CancellationToken ct = default)
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, "users/@me");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            using var res = await _http.SendAsync(req, ct);
            var raw = await res.Content.ReadAsStringAsync(ct);
            if (!res.IsSuccessStatusCode)
                throw new Exception($"Discord /users/@me failed: {(int)res.StatusCode} {res.ReasonPhrase} - {raw}");

            return JsonSerializer.Deserialize<MeResponse>(raw)!;
        }

        public async Task<(bool isMember, string[] roles)> CheckGuildMemberAsync(string userId)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(_cfg.Discord.GuildId))
                    throw new Exception("Discord GuildId missing in config");

                if (string.IsNullOrWhiteSpace(_cfg.Discord.BotToken))
                    throw new Exception("Discord BotToken missing in config");

                var url = $"/guilds/{_cfg.Discord.GuildId}/members/{userId}";
                // All comments in English as requested
                var rawToken = _cfg.Discord.BotToken.StartsWith("Bot ")
                    ? _cfg.Discord.BotToken.Substring(4)
                    : _cfg.Discord.BotToken;

                var req = new HttpRequestMessage(HttpMethod.Get, $"guilds/{_cfg.Discord.GuildId}/members/{userId}");
                req.Headers.Authorization = new AuthenticationHeaderValue("Bot", rawToken);

                // --- Debug logs ---
                Logger.Log($"[DiscordService] Guild check request: GuildId={_cfg.Discord.GuildId}, UserId={userId}", Logger.Level.Debug);
                Logger.Log($"[DiscordService] BotToken length={_cfg.Discord.BotToken.Length}", Logger.Level.Debug);

                var res = await _http.SendAsync(req);
                var body = await res.Content.ReadAsStringAsync();

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
