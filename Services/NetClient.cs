using System;
using System.Net.Http;

namespace CoffeeMC.App.Services
{
    // Один общий HttpClient на всё приложение (раньше каждый сервис создавал свой — лишние сокеты и соединения)
    public static class NetClient
    {
        public static readonly HttpClient Shared = Create();

        private static HttpClient Create()
        {
            var client = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
            client.DefaultRequestHeaders.UserAgent.ParseAdd("CoffeeMC-Launcher/0.2");
            return client;
        }

        // Скачивать разрешаем только по https (версия мода может принести свои ссылки)
        public static bool IsSecureUrl(string? url) =>
            Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps;

        public static bool IsHostOrSubdomain(string? url, params string[] hosts)
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
                return false;

            foreach (var host in hosts)
            {
                if (uri.Host.Equals(host, StringComparison.OrdinalIgnoreCase) ||
                    uri.Host.EndsWith("." + host, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }
    }
}
