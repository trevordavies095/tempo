using System.Diagnostics.CodeAnalysis;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Tempo.Api.Data;
using Tempo.Api.Models;
using Tempo.Api.Services;
using Tempo.Api.Utils;

namespace Tempo.Api.Commands;

public sealed class ResetPasswordArgs
{
    public string? Username { get; init; }
    public bool PasswordStdin { get; init; }
    public bool Help { get; init; }
}

public interface IResetPasswordInput
{
    bool IsTty { get; }
    string? ReadLine();
    string ReadMaskedLine();
}

public sealed class ConsoleResetPasswordInput : IResetPasswordInput
{
    public bool IsTty => !Console.IsInputRedirected;

    public string? ReadLine() => Console.In.ReadLine();

    public string ReadMaskedLine()
    {
        var buffer = new StringBuilder();
        while (true)
        {
            var key = Console.ReadKey(intercept: true);
            if (key.Key == ConsoleKey.Enter)
            {
                break;
            }

            if (key.Key == ConsoleKey.Backspace)
            {
                if (buffer.Length > 0)
                {
                    buffer.Length--;
                }

                continue;
            }

            if (!char.IsControl(key.KeyChar))
            {
                buffer.Append(key.KeyChar);
            }
        }

        return buffer.ToString();
    }
}

/// <summary>
/// Host-only break-glass password reset. Not an HTTP route.
/// </summary>
public static class ResetPasswordCommand
{
    public const string Verb = "reset-password";

    public const string Usage =
        """
        Usage: dotnet Tempo.Api.dll reset-password [options]

        Host-only password reset. Not an HTTP route.

        Options:
          --username <name>     User to reset (optional when exactly one account exists)
          --password-stdin      Read the new password from stdin (no prompt)
          --help, -h            Show this help

        Examples:
          docker compose exec -it api dotnet Tempo.Api.dll reset-password
          docker compose exec -T api dotnet Tempo.Api.dll reset-password --password-stdin
        """;

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
        var help = false;

        for (var i = 1; i < args.Count; i++)
        {
            var token = args[i];
            if (token is "--help" or "-h")
            {
                help = true;
                continue;
            }

            if (token == "--username")
            {
                if (i + 1 >= args.Count || args[i + 1].StartsWith('-'))
                {
                    error = "--username requires a value.";
                    return false;
                }

                username = args[++i].Trim();
                if (string.IsNullOrEmpty(username))
                {
                    error = "--username requires a value.";
                    return false;
                }

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

        parsed = new ResetPasswordArgs
        {
            Username = username,
            PasswordStdin = passwordStdin,
            Help = help
        };
        return true;
    }

    public static bool TryReadPassword(
        ResetPasswordArgs args,
        IResetPasswordInput input,
        TextWriter stderr,
        [NotNullWhen(true)] out string? password,
        [NotNullWhen(false)] out string? error)
    {
        password = null;
        error = null;

        if (args.PasswordStdin)
        {
            password = input.ReadLine() ?? string.Empty;
            return true;
        }

        if (!input.IsTty)
        {
            error = "No TTY. Use docker compose exec -it, or pass --password-stdin.";
            return false;
        }

        stderr.Write("New password: ");
        var first = input.ReadMaskedLine();
        stderr.WriteLine();
        stderr.Write("Confirm password: ");
        var second = input.ReadMaskedLine();
        stderr.WriteLine();

        if (first != second)
        {
            error = "Passwords did not match.";
            return false;
        }

        password = first;
        return true;
    }

    public static async Task<int> ExecuteAsync(
        TempoDbContext db,
        PasswordService passwordService,
        ILogger logger,
        string? username,
        string password,
        TextWriter stdout,
        TextWriter stderr,
        CancellationToken cancellationToken = default)
    {
        var user = await ResolveUserAsync(db, username, stderr, cancellationToken);
        if (user == null)
        {
            return 1;
        }

        if (!PasswordPolicy.TryValidate(password, user.Username, out var policyError))
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

    private static async Task<User?> ResolveUserAsync(
        TempoDbContext db,
        string? username,
        TextWriter stderr,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrEmpty(username))
        {
            var match = await db.Users.FirstOrDefaultAsync(u => u.Username == username, cancellationToken);
            if (match == null)
            {
                await stderr.WriteLineAsync("User not found.");
            }

            return match;
        }

        var users = await db.Users.OrderBy(u => u.Username).ToListAsync(cancellationToken);
        if (users.Count == 0)
        {
            await stderr.WriteLineAsync("No account exists. Register in the app.");
            return null;
        }

        if (users.Count > 1)
        {
            await stderr.WriteLineAsync("Multiple users found. Pass --username <name>:");
            foreach (var row in users)
            {
                await stderr.WriteLineAsync(row.Username);
            }

            return null;
        }

        return users[0];
    }
}
