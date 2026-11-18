using System;
using System.IO;
using System.Security.Cryptography;

namespace Clipbook.Services;

/// <summary>
/// AES-GCM encryption with a random 256-bit key that is itself protected by Windows DPAPI
/// (tied to the current Windows user). Data format: [version][...]
///   version 0 = plain, version 1 = [nonce 12][tag 16][ciphertext]
/// </summary>
public static class Crypto
{
    private static byte[]? _key;

    private static byte[] Key => _key ??= LoadOrCreateKey();

    private static byte[] LoadOrCreateKey()
    {
        AppPaths.EnsureDirs();
        if (File.Exists(AppPaths.KeyPath))
        {
            try
            {
                return ProtectedData.Unprotect(File.ReadAllBytes(AppPaths.KeyPath), null, DataProtectionScope.CurrentUser);
            }
            catch (Exception ex)
            {
                AppPaths.Log("Could not read key, generating a new one: " + ex.Message);
            }
        }
        var key = RandomNumberGenerator.GetBytes(32);
        File.WriteAllBytes(AppPaths.KeyPath, ProtectedData.Protect(key, null, DataProtectionScope.CurrentUser));
        return key;
    }

    public static byte[] Protect(byte[] data, bool encrypt)
    {
        if (!encrypt)
        {
            var plain = new byte[data.Length + 1];
            plain[0] = 0;
            Buffer.BlockCopy(data, 0, plain, 1, data.Length);
            return plain;
        }

        var nonce = RandomNumberGenerator.GetBytes(12);
        var tag = new byte[16];
        var cipher = new byte[data.Length];
        using (var aes = new AesGcm(Key, 16))
            aes.Encrypt(nonce, data, cipher, tag);

        var result = new byte[1 + 12 + 16 + cipher.Length];
        result[0] = 1;
        Buffer.BlockCopy(nonce, 0, result, 1, 12);
        Buffer.BlockCopy(tag, 0, result, 13, 16);
        Buffer.BlockCopy(cipher, 0, result, 29, cipher.Length);
        return result;
    }

    public static byte[]? Unprotect(byte[] blob)
    {
        try
        {
            if (blob.Length == 0) return null;
            if (blob[0] == 0)
            {
                var plain = new byte[blob.Length - 1];
                Buffer.BlockCopy(blob, 1, plain, 0, plain.Length);
                return plain;
            }
            if (blob[0] == 1 && blob.Length >= 29)
            {
                var nonce = new byte[12];
                var tag = new byte[16];
                var cipher = new byte[blob.Length - 29];
                Buffer.BlockCopy(blob, 1, nonce, 0, 12);
                Buffer.BlockCopy(blob, 13, tag, 0, 16);
                Buffer.BlockCopy(blob, 29, cipher, 0, cipher.Length);
                var plain = new byte[cipher.Length];
                using var aes = new AesGcm(Key, 16);
                aes.Decrypt(nonce, cipher, tag, plain);
                return plain;
            }
        }
        catch (Exception ex)
        {
            AppPaths.Log("Decrypt failed: " + ex.Message);
        }
        return null;
    }

    /// <summary>Keyed hash used for duplicate detection (does not reveal content).</summary>
    public static string Hash(byte[] data) => Convert.ToHexString(HMACSHA256.HashData(Key, data));
}
