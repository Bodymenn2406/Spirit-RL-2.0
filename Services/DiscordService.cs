// All comments in English as requested
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Spirit.Core.Config;

namespace Spirit.Core.Services
{
    public sealed class DiscordService
    {
        private readonly DiscordConfig _cfg;
        private readonly HttpClient _http;

        public DiscordService(DiscordConfig cfg)
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
                new("client_id", _cfg.ClientId),
                new("client_secret", _cfg.ClientSecret),
                new("grant_type", "authorization_code"),
                new("code", code)
            };
            if (!string.IsNullOrWhiteSpace(_cfg.RedirectUri))
                kv.Add(new("redirect_uri", _cfg.RedirectUri));

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

        public async Task<(bool isMember, string[] roles)> CheckGuildMemberAsync(string userId, CancellationToken ct = default)
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, $"guilds/{_cfg.GuildId}/members/{userId}");
            req.Headers.Authorization = AuthenticationHeaderValue.Parse(_cfg.BotToken); // "Bot xxx"
            using var res = await _http.SendAsync(req, ct);
            var raw = await res.Content.ReadAsStringAsync(ct);

            if (res.StatusCode == System.Net.HttpStatusCode.NotFound)
                return (false, Array.Empty<string>());

            if (!res.IsSuccessStatusCode)
                throw new Exception($"Discord guild member check failed: {(int)res.StatusCode} {res.ReasonPhrase} - {raw}");

            using var doc = JsonDocument.Parse(raw);
            var roles = doc.RootElement.TryGetProperty("roles", out var r) && r.ValueKind == JsonValueKind.Array
                ? r.EnumerateArray().Select(x => x.GetString() ?? "").Where(s => s.Length > 0).ToArray()
                : Array.Empty<string>();

            return (true, roles);
        }
    }
}
