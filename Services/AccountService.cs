using System;
using System.Security.Cryptography;
using System.Text;

namespace CoffeeMC.App.Services
{
    public class AccountService
    {
        // Считает тот же самый UUID, который использует официальный Minecraft
        // в оффлайн-режиме: детерминированно, из строки "OfflinePlayer:Ник"
        public static Guid GetOfflineUuid(string username)
        {
            var input = $"OfflinePlayer:{username}";
            var inputBytes = Encoding.UTF8.GetBytes(input);

            using var md5 = MD5.Create();
            var hash = md5.ComputeHash(inputBytes);

            // Подгоняем байты под формат UUID версии 3 (стандартные битовые метки версии/варианта)
            hash[6] = (byte)((hash[6] & 0x0F) | 0x30);
            hash[8] = (byte)((hash[8] & 0x3F) | 0x80);

            return new Guid(hash);
        }

        // PIN храним только в виде хеша — не для защиты от взлома (это локальный однопользовательский
        // файл настроек), а чтобы друг за тем же компьютером не мог просто выбрать твой профиль одним кликом
        // Новый формат: v2:<соль>:<PBKDF2-хеш>. Старые PIN (просто SHA-256) продолжают работать.
        public static string HashPin(string pin)
        {
            var salt = RandomNumberGenerator.GetBytes(16);
            var hash = Rfc2898DeriveBytes.Pbkdf2(pin, salt, 100_000, HashAlgorithmName.SHA256, 32);
            return $"v2:{Convert.ToHexString(salt)}:{Convert.ToHexString(hash)}";
        }

        public static bool VerifyPin(string pin, string? stored)
        {
            if (string.IsNullOrEmpty(stored))
                return false;

            if (stored.StartsWith("v2:", StringComparison.Ordinal))
            {
                var parts = stored.Split(':');
                if (parts.Length != 3)
                    return false;

                try
                {
                    var salt = Convert.FromHexString(parts[1]);
                    var expected = Convert.FromHexString(parts[2]);
                    var actual = Rfc2898DeriveBytes.Pbkdf2(pin, salt, 100_000, HashAlgorithmName.SHA256, 32);
                    return CryptographicOperations.FixedTimeEquals(actual, expected);
                }
                catch (FormatException)
                {
                    return false;
                }
            }

            // legacy
            var legacy = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(pin)));
            return string.Equals(legacy, stored, StringComparison.OrdinalIgnoreCase);
        }
    }
}