// HuaweiCrypto.cs — birleştirilmiş sürüm: OpenSource Decrypt + kapalı sürümün Encrypt'i
// (sUser şifre yazma için geri şifreleme gerekli).
// Format: [01 00 00 00][4B alan][16B tohum/IV][AES-256-CBC gövde][32B HMAC-SHA256]
// Anahtar: k = SHA256(k || magic) x 8192, k0 = tohum||sıfır16
using System;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;

namespace HekzOntRace.Core;

public static class HuaweiCrypto
{
    private static byte[] MagicBytes()
    {
        byte[] d = new byte[36]
        {
            95, 93, 65, 0, 10, 15, 14, 7, 10, 117,
            114, 117, 7, 118, 114, 117, 119, 125, 124, 126,
            10, 125, 122, 120, 120, 105, 104, 20, 101, 16,
            96, 96, 22, 106, 106, 99
        };
        byte[] outp = new byte[d.Length];
        for (int i = 0; i < d.Length; i++)
            outp[i] = (byte)(d[i] ^ (55 + i));
        return outp;
    }

    public static byte[] DeriveKey(byte[] seed)
    {
        byte[] key = new byte[32];
        Buffer.BlockCopy(seed, 0, key, 0, 16);
        byte[] magic = MagicBytes();
        byte[] buffer = new byte[32 + magic.Length];
        using SHA256 sha = SHA256.Create();
        for (int i = 0; i < 8192; i++)
        {
            Buffer.BlockCopy(key, 0, buffer, 0, 32);
            Buffer.BlockCopy(magic, 0, buffer, 32, magic.Length);
            key = sha.ComputeHash(buffer);
        }
        return key;
    }

    public sealed class Result
    {
        public byte[] Plain = Array.Empty<byte>();
        public byte[] Key = Array.Empty<byte>();
        public bool HmacValid;
        public string PlainText => Encoding.UTF8.GetString(Plain);
    }

    /// <summary>Düz metni (gzip akışı) şifreler — başlık ilk 8 bayt orijinalden.</summary>
    public static byte[] Encrypt(byte[] plain, byte[] header8)
    {
        int n = plain.Length % 16;
        byte[] seed = new byte[16];
        RandomNumberGenerator.Fill(seed);
        seed[15] = (byte)((seed[15] & 0xF0) | n);
        byte[] key = DeriveKey(seed);
        byte[] padded = new byte[plain.Length + ((16 - n) % 16)];
        Buffer.BlockCopy(plain, 0, padded, 0, plain.Length);
        using Aes aes = Aes.Create();
        aes.Key = key;
        aes.IV = seed;
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.None;
        byte[] ct = aes.CreateEncryptor().TransformFinalBlock(padded, 0, padded.Length);
        byte[] hmac;
        using (HMACSHA256 h = new HMACSHA256(key)) hmac = h.ComputeHash(ct);
        byte[] outp = new byte[8 + 16 + ct.Length + 32];
        Buffer.BlockCopy(header8, 0, outp, 0, 8);
        Buffer.BlockCopy(seed, 0, outp, 8, 16);
        Buffer.BlockCopy(ct, 0, outp, 24, ct.Length);
        Buffer.BlockCopy(hmac, 0, outp, 24 + ct.Length, 32);
        return outp;
    }

    public static Result Decrypt(byte[] data)
    {
        if (data == null || data.Length < 56 || data[0] != 1)
            throw new InvalidDataException("Format uyusmuyor (bashlik >= 56 bayt ve 01 00 00 00 ile baslamali).");
        byte[] iv = new byte[16];
        Buffer.BlockCopy(data, 8, iv, 0, 16);
        byte[] key = DeriveKey(iv);
        byte[] body = new byte[data.Length - 56];
        Buffer.BlockCopy(data, 24, body, 0, body.Length);
        bool hmacValid;
        using (HMACSHA256 hmac = new HMACSHA256(key))
        {
            hmacValid = hmac.ComputeHash(body).AsSpan().SequenceEqual(data.AsSpan(data.Length - 32, 32));
        }
        using Aes aes = Aes.Create();
        aes.Key = key;
        aes.IV = iv;
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.None;
        byte[] plain = aes.CreateDecryptor().TransformFinalBlock(body, 0, body.Length);
        int pad = iv[15] & 0xF;
        if (pad != 0)
            plain = plain[..(plain.Length - (16 - pad))];
        return new Result { Plain = plain, Key = key, HmacValid = hmacValid };
    }
}
