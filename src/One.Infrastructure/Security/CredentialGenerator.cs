using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using One.Application.Contracts;
using One.Domain.Enums;
using One.Infrastructure.Options;

namespace One.Infrastructure.Security;

/// <summary>
/// Genera pares api key / secreto con entropía criptográfica y los guarda solo como
/// HMAC-SHA256 con pepper de servidor: un volcado de la base no permite reconstruirlos.
/// </summary>
public sealed class CredentialGenerator : ICredentialGenerator
{
    private const string Alphabet = "abcdefghijkmnopqrstuvwxyzABCDEFGHJKLMNPQRSTUVWXYZ23456789";
    private const int KeyLength = 32;
    private const int SecretLength = 48;
    private const int ClientIdLength = 20;

    private readonly byte[] _pepper;

    public CredentialGenerator(IOptions<SecurityOptions> options)
    {
        var pepper = options.Value.ApiKeyPepper;
        if (string.IsNullOrWhiteSpace(pepper))
            throw new InvalidOperationException("Security:ApiKeyPepper no está configurada.");

        _pepper = Encoding.UTF8.GetBytes(pepper);
    }

    public GeneratedCredential Generate(AppEnvironment environment)
    {
        var tag = EnvironmentTag(environment);

        var clientId = $"one_{tag}_{RandomString(ClientIdLength)}";
        var apiKey = $"ak_{tag}_{RandomString(KeyLength)}";
        var apiSecret = $"sk_{tag}_{RandomString(SecretLength)}";

        return new GeneratedCredential(
            ClientId: clientId,
            ApiKey: apiKey,
            KeyPrefix: apiKey[..Math.Min(14, apiKey.Length)],
            ApiKeyHash: Hash(apiKey),
            ApiSecret: apiSecret,
            SecretHash: Hash(apiSecret),
            SecretLast4: apiSecret[^4..]);
    }

    public string Hash(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        return Convert.ToHexStringLower(HMACSHA256.HashData(_pepper, Encoding.UTF8.GetBytes(value)));
    }

    /// <summary>Comparación en tiempo constante para no filtrar información por temporización.</summary>
    public bool Verify(string value, string hash)
    {
        if (string.IsNullOrWhiteSpace(value) || string.IsNullOrWhiteSpace(hash)) return false;

        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(Hash(value)),
            Encoding.UTF8.GetBytes(hash));
    }

    private static string EnvironmentTag(AppEnvironment environment) => environment switch
    {
        AppEnvironment.Production => "live",
        AppEnvironment.Staging => "stg",
        _ => "dev"
    };

    private static string RandomString(int length)
    {
        var buffer = new char[length];
        for (var i = 0; i < length; i++)
            buffer[i] = Alphabet[RandomNumberGenerator.GetInt32(Alphabet.Length)];

        return new string(buffer);
    }
}
