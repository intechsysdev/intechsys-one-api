using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using One.Application.Contracts;
using One.Infrastructure.Options;

namespace One.Infrastructure.Security;

/// <summary>
/// Cifra los valores secretos con AES-256-GCM. El formato en base es
/// "v1.{nonce}.{tag}.{ciphertext}", todo en base64url, para poder rotar el esquema más adelante.
/// </summary>
public sealed class SecretProtector : ISecretProtector
{
    private const string Version = "v1";
    private const int NonceSize = 12;
    private const int TagSize = 16;

    private readonly byte[] _key;

    public SecretProtector(IOptions<SecurityOptions> options)
    {
        var configured = options.Value.EncryptionKey;
        if (string.IsNullOrWhiteSpace(configured))
            throw new InvalidOperationException("Security:EncryptionKey no está configurada.");

        _key = DeriveKey(configured);
    }

    public string Protect(string plaintext)
    {
        ArgumentNullException.ThrowIfNull(plaintext);

        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var plainBytes = Encoding.UTF8.GetBytes(plaintext);
        var cipher = new byte[plainBytes.Length];
        var tag = new byte[TagSize];

        using var aes = new AesGcm(_key, TagSize);
        aes.Encrypt(nonce, plainBytes, cipher, tag);

        return string.Join('.', Version, Base64Url(nonce), Base64Url(tag), Base64Url(cipher));
    }

    public string Unprotect(string protectedValue)
    {
        if (!TryUnprotect(protectedValue, out var plaintext))
            throw new CryptographicException("No se pudo descifrar el valor: formato inválido o clave incorrecta.");

        return plaintext;
    }

    public bool TryUnprotect(string protectedValue, out string plaintext)
    {
        plaintext = string.Empty;
        if (string.IsNullOrWhiteSpace(protectedValue)) return false;

        var parts = protectedValue.Split('.');
        if (parts.Length != 4 || parts[0] != Version) return false;

        try
        {
            var nonce = FromBase64Url(parts[1]);
            var tag = FromBase64Url(parts[2]);
            var cipher = FromBase64Url(parts[3]);
            var plain = new byte[cipher.Length];

            using var aes = new AesGcm(_key, TagSize);
            aes.Decrypt(nonce, cipher, tag, plain);

            plaintext = Encoding.UTF8.GetString(plain);
            return true;
        }
        catch (Exception ex) when (ex is CryptographicException or FormatException or ArgumentException)
        {
            return false;
        }
    }

    /// <summary>Admite la clave en base64 de 32 bytes o cualquier frase, normalizada con SHA-256.</summary>
    private static byte[] DeriveKey(string configured)
    {
        if (configured.Length is 44 or 43)
        {
            try
            {
                var decoded = Convert.FromBase64String(configured.PadRight(44, '='));
                if (decoded.Length == 32) return decoded;
            }
            catch (FormatException)
            {
                // No era base64: se deriva por hash más abajo.
            }
        }

        return SHA256.HashData(Encoding.UTF8.GetBytes(configured));
    }

    private static string Base64Url(byte[] value) => Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[] FromBase64Url(string value)
    {
        var padded = value.Replace('-', '+').Replace('_', '/');
        return Convert.FromBase64String(padded.PadRight(padded.Length + (4 - padded.Length % 4) % 4, '='));
    }
}
