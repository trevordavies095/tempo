namespace Tempo.Api.Tests.Infrastructure;

/// <summary>
/// Compliant passwords for integration tests (matches <see cref="Tempo.Api.Services.PasswordPolicy"/>).
/// </summary>
public static class TestPasswords
{
    public const string Default = "Tempo-Integration-Test-Pass!";
    public const string Alternate = "Tempo-Alternate-Test-Passphrase!";
}
