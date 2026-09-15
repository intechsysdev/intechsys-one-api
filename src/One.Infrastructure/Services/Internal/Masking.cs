namespace One.Infrastructure.Services.Internal;

/// <summary>Enmascarado uniforme de valores sensibles antes de devolverlos al portal.</summary>
public static class Masking
{
    public const string Placeholder = "••••••••";

    public static string ApiKey(string prefix) => $"{prefix}{Placeholder}";

    public static string Secret(string last4) => $"sk_{Placeholder}{last4}";

    public static string? SettingValue(string? value, bool isSecret, bool reveal)
    {
        if (!isSecret || reveal) return value;
        return string.IsNullOrEmpty(value) ? null : Placeholder;
    }
}
