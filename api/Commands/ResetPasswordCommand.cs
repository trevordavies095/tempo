using System.Diagnostics.CodeAnalysis;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Tempo.Api.Data;
using Tempo.Api.Services;
using Tempo.Api.Utils;

namespace Tempo.Api.Commands;

public sealed class ResetPasswordArgs
{
    public required string Username { get; init; }
}

/// <summary>
/// Host-only break-glass password reset. Not an HTTP route.
/// </summary>
public static class ResetPasswordCommand
{
    public const string Verb = "reset-password";

    public static bool IsVerb(IReadOnlyList<string> args) =>
        args.Count > 0 && string.Equals(args[0], Verb, StringComparison.Ordinal);

    public static bool TryParse(
        IReadOnlyList<string> args,
        [NotNullWhen(true)] out ResetPasswordArgs? parsed,
        [NotNullWhen(false)] out string? error)
    {
        parsed = null;
        error = null;

        if (!IsVerb(args))
        {
            error = $"Expected {Verb} command.";
            return false;
        }

        string? username = null;
        var passwordStdin = false;

        for (var i = 1; i < args.Count; i++)
        {
            var token = args[i];
            if (token == "--username")
            {
                if (i + 1 >= args.Count || args[i + 1].StartsWith('-'))
                {
                    error = "--username requires a value.";
                    return false;
                }

                username = args[++i].Trim();
                continue;
            }

            if (token == "--password-stdin")
            {
                passwordStdin = true;
                continue;
            }

            if (token == "--password" || token.StartsWith("--password=", StringComparison.Ordinal))
            {
                error = "Do not pass --password; use --password-stdin.";
                return false;
            }

            error = $"Unknown argument: {token}";
            return false;
        }

        if (string.IsNullOrEmpty(username))
        {
            error = "--username is required.";
            return false;
        }

        if (!passwordStdin)
        {
            error = "--password-stdin is required.";
            return false;
        }

        parsed = new ResetPasswordArgs { Username = username };
        return true;
    }

    public static async Task<int> ExecuteAsync(
        TempoDbContext db,
        PasswordService passwordService,
        ILogger logger,
        string username,
        string password,
        TextWriter stdout,
        TextWriter stderr,
        CancellationToken cancellationToken = default)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Username == username, cancellationToken);
        if (user == null)
        {
            await stderr.WriteLineAsync("User not found.");
            return 1;
        }

        if (!PasswordPolicy.TryValidate(password, username, out var policyError))
        {
            await stderr.WriteLineAsync(policyError);
            return 1;
        }

        UserPassword.ApplyNewPassword(user, password, passwordService);
        await db.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Password reset via break-glass: {Username}",
            LogSanitizer.Sanitize(user.Username));

        await stdout.WriteLineAsync(
            $"Password reset for {user.Username}. Existing sessions are invalid. Log in again.");
        return 0;
    }
}
