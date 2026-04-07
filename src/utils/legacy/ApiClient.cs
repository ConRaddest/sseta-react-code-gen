using System.Net;
using System.Text;
using System.Text.Json;

namespace ReactCodegen.Legacy
{
    public class AuthenticatedApiClient : IDisposable
    {
        private readonly HttpClient _client;
        private readonly HttpClientHandler _handler;
        private readonly CookieContainer _cookieContainer;
        private string _cookieHeader;

        public HttpClient Client => _client;
        public CookieContainer CookieContainer => _cookieContainer;
        public string CookieHeader => _cookieHeader;

        private AuthenticatedApiClient(string apiBaseUrl)
        {
            _cookieContainer = new CookieContainer();
            _handler = new HttpClientHandler
            {
                UseCookies = true,
                CookieContainer = _cookieContainer,
                ServerCertificateCustomValidationCallback = (message, cert, chain, errors) => true
            };

            _client = new HttpClient(_handler)
            {
                BaseAddress = new Uri(apiBaseUrl),
                Timeout = TimeSpan.FromSeconds(60)
            };

            _cookieHeader = string.Empty;
        }

        public static async Task<AuthenticatedApiClient?> CreateAsync(
            string apiBaseUrl,
            string authEmail,
            string authPassword)
        {
            var client = new AuthenticatedApiClient(apiBaseUrl);

            var loginResult = await client.LoginAsync(authEmail, authPassword);
            if (!loginResult)
            {
                client.Dispose();
                return null;
            }

            return client;
        }

        private async Task<bool> LoginAsync(string email, string password)
        {
            var cookiePairs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var loginBody = new
            {
                email,
                password
            };

            var content = new StringContent(JsonSerializer.Serialize(loginBody), Encoding.UTF8, "application/json");
            var loginUri = new Uri(_client.BaseAddress!, "/api/auth/login");
            using var response = await _client.PostAsync(loginUri, content);
            var hadSetCookie = response.Headers.TryGetValues("Set-Cookie", out var setCookieValues);

            if (hadSetCookie && setCookieValues != null)
            {
                foreach (var setCookie in setCookieValues)
                {
                    _cookieContainer.SetCookies(loginUri, setCookie);
                    var firstPart = setCookie.Split(';', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
                    if (!string.IsNullOrWhiteSpace(firstPart))
                    {
                        var idx = firstPart.IndexOf('=');
                        if (idx > 0)
                        {
                            var name = firstPart.Substring(0, idx).Trim();
                            var value = firstPart.Substring(idx + 1).Trim();
                            if (!string.IsNullOrWhiteSpace(name))
                            {
                                cookiePairs[name] = value;
                            }
                        }
                    }
                }
            }

            var loginCookies = _cookieContainer.GetCookies(loginUri);
            if (loginCookies.Count == 0 && hadSetCookie && setCookieValues != null)
            {
                foreach (var setCookie in setCookieValues)
                {
                    var firstPart = setCookie.Split(';', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
                    if (string.IsNullOrWhiteSpace(firstPart)) continue;
                    var idx = firstPart.IndexOf('=');
                    if (idx <= 0) continue;
                    var name = firstPart.Substring(0, idx).Trim();
                    var value = firstPart.Substring(idx + 1).Trim();
                    if (string.IsNullOrWhiteSpace(name)) continue;
                    var manualCookie = new Cookie(name, value, "/", loginUri.Host);
                    _cookieContainer.Add(loginUri, manualCookie);
                }
                loginCookies = _cookieContainer.GetCookies(loginUri);
            }

            if (loginUri.Scheme == "http")
            {
                var secureCookies = _cookieContainer.GetCookies(loginUri).Cast<Cookie>().Where(c => c.Secure).ToList();
                foreach (var cookie in secureCookies)
                {
                    var downgraded = new Cookie(cookie.Name, cookie.Value, cookie.Path, cookie.Domain)
                    {
                        Secure = false,
                        HttpOnly = cookie.HttpOnly,
                        Expires = cookie.Expires
                    };
                    _cookieContainer.Add(loginUri, downgraded);
                }
                loginCookies = _cookieContainer.GetCookies(loginUri);
            }

            if (!response.IsSuccessStatusCode)
            {
                var responseBody = await response.Content.ReadAsStringAsync();
                Console.WriteLine($"Login failed: {(int)response.StatusCode} {response.StatusCode}");
                return false;
            }

            _cookieHeader = _cookieContainer.GetCookieHeader(loginUri);
            if (string.IsNullOrWhiteSpace(_cookieHeader) && cookiePairs.Count > 0)
            {
                _cookieHeader = string.Join("; ", cookiePairs.Select(kvp => $"{kvp.Key}={kvp.Value}"));
            }

            return true;
        }

        public string GetEffectiveCookieHeader(Uri uri)
        {
            var effectiveCookieHeader = _cookieContainer.GetCookieHeader(uri);
            if (string.IsNullOrWhiteSpace(effectiveCookieHeader))
            {
                effectiveCookieHeader = _cookieHeader;
            }
            return effectiveCookieHeader;
        }

        public void Dispose()
        {
            _client?.Dispose();
            _handler?.Dispose();
        }
    }
}
