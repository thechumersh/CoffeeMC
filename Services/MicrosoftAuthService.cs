using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace CoffeeMC.App.Services
{
    public class DeviceCodeInfo
    {
        public string DeviceCode { get; set; } = string.Empty;
        public string UserCode { get; set; } = string.Empty;
        public string VerificationUri { get; set; } = string.Empty;
        public int ExpiresIn { get; set; }
        public int Interval { get; set; }
    }

    public class MicrosoftAuthResult
    {
        public bool Success { get; set; }
        public string? Error { get; set; }
        public string Username { get; set; } = string.Empty;
        public string Uuid { get; set; } = string.Empty;
        public string McAccessToken { get; set; } = string.Empty;
        public DateTime McAccessTokenExpiresUtc { get; set; }
        public string MsRefreshToken { get; set; } = string.Empty;
    }

    // Авторизация через настоящий аккаунт Microsoft — по тому же принципу, что у Prism/MultiMC:
    // Device Code Flow (ввод короткого кода на microsoft.com/link) -> Xbox Live -> XSTS -> Minecraft Services.
    // Подробности схемы: https://wiki.vg/Microsoft_Authentication_Scheme
    public class MicrosoftAuthService
    {
        // Публичный client_id официального Minecraft-лаунчера — его используют многие open-source
        // лаунчеры (HMCL, PollyMC и др.) именно для этого device-code флоу. Если Microsoft когда-нибудь
        // ужесточит квоты для него, нужно будет зарегистрировать своё Azure-приложение и подставить
        // свой client_id здесь.
        private const string ClientId = "1d1fa340-c2fa-45a9-bf3d-7d16d5599c2d";
        private const string Scope = "XboxLive.signin offline_access";

        private static readonly HttpClient _http = NetClient.Shared;

        public async Task<DeviceCodeInfo> RequestDeviceCodeAsync()
        {
            var response = await _http.PostAsync(
                "https://login.microsoftonline.com/consumers/oauth2/v2.0/devicecode",
                new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["client_id"] = ClientId,
                    ["scope"] = Scope
                }));

            if (!response.IsSuccessStatusCode)
                throw new HttpRequestException(await DescribeAsync("devicecode", response));

            var json = await response.Content.ReadFromJsonAsync<JsonElement>();

            return new DeviceCodeInfo
            {
                DeviceCode = json.GetProperty("device_code").GetString() ?? string.Empty,
                UserCode = json.GetProperty("user_code").GetString() ?? string.Empty,
                VerificationUri = json.TryGetProperty("verification_uri", out var vu)
                    ? vu.GetString() ?? "https://microsoft.com/link"
                    : "https://microsoft.com/link",
                ExpiresIn = json.TryGetProperty("expires_in", out var ei) ? ei.GetInt32() : 900,
                Interval = json.TryGetProperty("interval", out var iv) ? iv.GetInt32() : 5
            };
        }

        // Опрашивает токен-эндпоинт, пока пользователь не введёт код в браузере, не откажется или не истечёт время
        public async Task<MicrosoftAuthResult> PollForTokenAsync(DeviceCodeInfo device, CancellationToken cancellationToken)
        {
            var deadline = DateTime.UtcNow.AddSeconds(device.ExpiresIn);
            var interval = TimeSpan.FromSeconds(Math.Max(device.Interval, 5));

            while (DateTime.UtcNow < deadline)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await Task.Delay(interval, cancellationToken);

                var response = await _http.PostAsync(
                    "https://login.microsoftonline.com/consumers/oauth2/v2.0/token",
                    new FormUrlEncodedContent(new Dictionary<string, string>
                    {
                        ["client_id"] = ClientId,
                        ["grant_type"] = "urn:ietf:params:oauth:grant-type:device_code",
                        ["device_code"] = device.DeviceCode
                    }));

                var pollBody = await response.Content.ReadAsStringAsync();
                JsonElement json;
                try { json = JsonSerializer.Deserialize<JsonElement>(pollBody); }
                catch { return new MicrosoftAuthResult { Success = false, Error = $"token: HTTP {(int)response.StatusCode}: {Trim(pollBody)}" }; }

                if (response.IsSuccessStatusCode)
                {
                    var msAccessToken = json.GetProperty("access_token").GetString()!;
                    var msRefreshToken = json.TryGetProperty("refresh_token", out var rt) ? rt.GetString() ?? string.Empty : string.Empty;
                    return await FinishLoginAsync(msAccessToken, msRefreshToken);
                }

                var error = json.TryGetProperty("error", out var e) ? e.GetString() : null;

                if (error is "authorization_declined" or "expired_token" or "bad_verification_code")
                    return new MicrosoftAuthResult { Success = false, Error = error };

                // authorization_pending / slow_down — пользователь ещё не ввёл код, ждём дальше.
                // Любая другая ошибка (invalid_client, unauthorized_client...) — это неверная настройка Azure-приложения, показываем её сразу.
                if (error is not (null or "authorization_pending" or "slow_down"))
                {
                    var desc = json.TryGetProperty("error_description", out var d) ? d.GetString() : null;
                    return new MicrosoftAuthResult { Success = false, Error = $"token: {error}: {Trim(desc)}" };
                }

                // По протоколу при slow_down интервал опроса нужно увеличить на 5 секунд
                if (error == "slow_down")
                    interval += TimeSpan.FromSeconds(5);

                if (error == null)
                    return new MicrosoftAuthResult { Success = false, Error = $"token: HTTP {(int)response.StatusCode}: {Trim(pollBody)}" };
            }

            return new MicrosoftAuthResult { Success = false, Error = "timeout" };
        }

        // Тихий повторный вход без браузера — по refresh_token, сохранённому с прошлого раза
        public async Task<MicrosoftAuthResult> RefreshAsync(string refreshToken)
        {
            try
            {
                var response = await _http.PostAsync(
                    "https://login.microsoftonline.com/consumers/oauth2/v2.0/token",
                    new FormUrlEncodedContent(new Dictionary<string, string>
                    {
                        ["client_id"] = ClientId,
                        ["grant_type"] = "refresh_token",
                        ["refresh_token"] = refreshToken,
                        ["scope"] = Scope
                    }));

                if (!response.IsSuccessStatusCode)
                    return new MicrosoftAuthResult { Success = false, Error = "refresh_failed" };

                var json = await response.Content.ReadFromJsonAsync<JsonElement>();
                var msAccessToken = json.GetProperty("access_token").GetString()!;
                var msRefreshToken = json.TryGetProperty("refresh_token", out var rt) ? rt.GetString() ?? refreshToken : refreshToken;

                return await FinishLoginAsync(msAccessToken, msRefreshToken);
            }
            catch (Exception ex)
            {
                return new MicrosoftAuthResult { Success = false, Error = ex.Message };
            }
        }

        // Xbox Live отвечает 400 без тела, если нет заголовков Accept/Content-Type: application/json
        // (PostAsJsonAsync Accept не ставит и добавляет charset к Content-Type), поэтому запрос собираем вручную
        private async Task<HttpResponseMessage> PostJsonAsync(string url, object payload)
        {
            var request = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = new StringContent(JsonSerializer.Serialize(payload), System.Text.Encoding.UTF8)
            };
            request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            request.Headers.TryAddWithoutValidation("x-xbl-contract-version", "1");

            return await _http.SendAsync(request);
        }

        private static string Trim(string? text)
        {
            text = (text ?? string.Empty).Replace("\r", " ").Replace("\n", " ").Trim();
            return text.Length > 400 ? text[..400] + "..." : text;
        }

        // Короткое описание отказа сервера: на каком шаге, какой код и что ответил
        private static async Task<string> DescribeAsync(string step, HttpResponseMessage response)
        {
            var body = await response.Content.ReadAsStringAsync();
            return $"{step}: HTTP {(int)response.StatusCode}: {Trim(body)}";
        }

        private async Task<MicrosoftAuthResult> FinishLoginAsync(string msAccessToken, string msRefreshToken)
        {
            try
            {
                // 1. Xbox Live: токен Azure-приложения идёт с префиксом "d=", на случай отказа пробуем "t="
                HttpResponseMessage xblResponse = null!;
                foreach (var prefix in new[] { "d=", "t=" })
                {
                    var xblPayload = new
                    {
                        Properties = new
                        {
                            AuthMethod = "RPS",
                            SiteName = "user.auth.xboxlive.com",
                            RpsTicket = $"{prefix}{msAccessToken}"
                        },
                        RelyingParty = "http://auth.xboxlive.com",
                        TokenType = "JWT"
                    };

                    xblResponse = await PostJsonAsync("https://user.auth.xboxlive.com/user/authenticate", xblPayload);
                    if (xblResponse.IsSuccessStatusCode)
                        break;
                }

                if (!xblResponse.IsSuccessStatusCode)
                    return new MicrosoftAuthResult { Success = false, Error = await DescribeAsync("xbox live", xblResponse) };

                var xblJson = await xblResponse.Content.ReadFromJsonAsync<JsonElement>();
                var xblToken = xblJson.GetProperty("Token").GetString()!;
                var uhs = xblJson.GetProperty("DisplayClaims").GetProperty("xui")[0].GetProperty("uhs").GetString()!;

                // 2. XSTS
                var xstsPayload = new
                {
                    Properties = new
                    {
                        SandboxId = "RETAIL",
                        UserTokens = new[] { xblToken }
                    },
                    RelyingParty = "rp://api.minecraftservices.com/",
                    TokenType = "JWT"
                };

                var xstsResponse = await PostJsonAsync("https://xsts.auth.xboxlive.com/xsts/authorize", xstsPayload);

                if (!xstsResponse.IsSuccessStatusCode)
                {
                    var xstsBody = await xstsResponse.Content.ReadAsStringAsync();
                    long xErr = 0;
                    try
                    {
                        var errJson = JsonSerializer.Deserialize<JsonElement>(xstsBody);
                        xErr = errJson.TryGetProperty("XErr", out var xe) ? xe.GetInt64() : 0L;
                    }
                    catch { }

                    var reason = xErr switch
                    {
                        2148916233 => "no_xbox_account",
                        2148916235 => "region_blocked",
                        2148916236 or 2148916237 => "adult_verification_needed",
                        2148916238 => "underage",
                        _ => $"xsts: HTTP {(int)xstsResponse.StatusCode}: {Trim(xstsBody)}"
                    };

                    return new MicrosoftAuthResult { Success = false, Error = reason };
                }

                var xstsJson = await xstsResponse.Content.ReadFromJsonAsync<JsonElement>();
                var xstsToken = xstsJson.GetProperty("Token").GetString()!;

                // 3. Minecraft Services
                var mcPayload = new { identityToken = $"XBL3.0 x={uhs};{xstsToken}" };
                var mcResponse = await PostJsonAsync("https://api.minecraftservices.com/authentication/login_with_xbox", mcPayload);
                if (!mcResponse.IsSuccessStatusCode)
                {
                    return new MicrosoftAuthResult
                    {
                        Success = false,
                        Error = (int)mcResponse.StatusCode == 403 ? "app_not_approved" : await DescribeAsync("minecraft", mcResponse)
                    };
                }
                var mcJson = await mcResponse.Content.ReadFromJsonAsync<JsonElement>();
                var mcAccessToken = mcJson.GetProperty("access_token").GetString()!;
                var expiresIn = mcJson.TryGetProperty("expires_in", out var expEl) ? expEl.GetInt32() : 86000;

                // 4. Проверяем владение игрой — отсекает аккаунты Microsoft без купленного Minecraft
                var entReq = new HttpRequestMessage(HttpMethod.Get, "https://api.minecraftservices.com/entitlements/mcstore");
                entReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", mcAccessToken);
                var entResponse = await _http.SendAsync(entReq);

                if (entResponse.IsSuccessStatusCode)
                {
                    var entJson = await entResponse.Content.ReadFromJsonAsync<JsonElement>();
                    var ownsGame = entJson.TryGetProperty("items", out var itemsEl) && itemsEl.GetArrayLength() > 0;

                    if (!ownsGame)
                        return new MicrosoftAuthResult { Success = false, Error = "no_game_ownership" };
                }

                // 5. Профиль — настоящие ник и UUID
                var profReq = new HttpRequestMessage(HttpMethod.Get, "https://api.minecraftservices.com/minecraft/profile");
                profReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", mcAccessToken);
                var profResponse = await _http.SendAsync(profReq);

                if (!profResponse.IsSuccessStatusCode)
                    return new MicrosoftAuthResult { Success = false, Error = "no_profile" };

                var profJson = await profResponse.Content.ReadFromJsonAsync<JsonElement>();

                return new MicrosoftAuthResult
                {
                    Success = true,
                    Username = profJson.GetProperty("name").GetString() ?? string.Empty,
                    Uuid = profJson.GetProperty("id").GetString() ?? string.Empty,
                    McAccessToken = mcAccessToken,
                    McAccessTokenExpiresUtc = DateTime.UtcNow.AddSeconds(Math.Max(expiresIn - 60, 60)),
                    MsRefreshToken = msRefreshToken
                };
            }
            catch (Exception ex)
            {
                return new MicrosoftAuthResult { Success = false, Error = ex.Message };
            }
        }
    }
}
