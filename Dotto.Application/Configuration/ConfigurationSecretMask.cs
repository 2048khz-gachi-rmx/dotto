namespace Dotto.Application.Configuration;

public static class ConfigurationSecretMask
{
    private static readonly string[] SecretSuffixes =
    [
        "ApiKey",
        "AccessKey",
        "SecretKey",
        "Password",
        "Token",
        "Secret",
        "Credential"
    ];

    public const string Mask = "***";

    public static bool IsSecret(string key)
    {
        return SecretSuffixes.Any(suffix => key.EndsWith(suffix, StringComparison.OrdinalIgnoreCase));
    }

    public static string MaskIfSecret(string key, string value)
    {
        return IsSecret(key) ? Mask : value;
    }
}
