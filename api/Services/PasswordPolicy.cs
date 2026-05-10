using System.Diagnostics.CodeAnalysis;
using System.Text;

namespace Tempo.Api.Services;

/// <summary>
/// NIST/CISA-aligned password rules: length-focused, no mandatory complexity, block common values.
/// </summary>
public static class PasswordPolicy
{
    public const int MinLength = 16;
    public const int MaxLength = 64;

    /// <summary>BCrypt uses at most 72 UTF-8 bytes; reject beyond this for predictable behavior.</summary>
    public const int MaxUtf8Bytes = 72;

    private static readonly HashSet<string> CommonPasswordsLower = BuildBlocklist();

    /// <summary>
    /// Validates a new password (register or change). Does not trim <paramref name="password"/>; whitespace may be intentional in passphrases.
    /// </summary>
    public static bool TryValidate(string password, string username, [NotNullWhen(false)] out string? error)
    {
        error = null;
        if (string.IsNullOrWhiteSpace(password))
        {
            error = "Password is required";
            return false;
        }

        if (password.Length < MinLength)
        {
            error = $"Password must be at least {MinLength} characters";
            return false;
        }

        if (password.Length > MaxLength)
        {
            error = $"Password must be at most {MaxLength} characters";
            return false;
        }

        var utf8Bytes = Encoding.UTF8.GetByteCount(password);
        if (utf8Bytes > MaxUtf8Bytes)
        {
            error = "Password is too long when encoded (maximum 72 bytes in UTF-8); use a shorter passphrase";
            return false;
        }

        if (HasFiveOrMoreRepeatedRun(password))
        {
            error = "Password must not contain the same character repeated five or more times in a row";
            return false;
        }

        var trimmedUsername = username.Trim();
        if (trimmedUsername.Length >= 3 &&
            password.Contains(trimmedUsername, StringComparison.OrdinalIgnoreCase))
        {
            error = "Password must not contain your username";
            return false;
        }

        if (CommonPasswordsLower.Contains(password.ToLowerInvariant()))
        {
            error = "This password is too common; choose a different passphrase";
            return false;
        }

        return true;
    }

    private static bool HasFiveOrMoreRepeatedRun(string password)
    {
        if (password.Length < 5)
        {
            return false;
        }

        for (var i = 0; i <= password.Length - 5; i++)
        {
            var c = password[i];
            if (password[i + 1] == c && password[i + 2] == c && password[i + 3] == c && password[i + 4] == c)
            {
                return true;
            }
        }

        return false;
    }

    private static HashSet<string> BuildBlocklist()
    {
        string[] items =
        [
            "passwordpassword",
            "passwordpassword1",
            "1234567890123456",
            "qwertyuiopasdfgh",
            "qwertyqwertyqwer",
            "adminadminadmin1",
            "welcome welcome!",
            "iloveyouiloveyou",
            "sunshinesunshine",
            "princessprincess",
            "footballfootball",
            "baseballbaseball",
            "abcabcabcabcabcd",
            "letmeinletmein!!",
            "monkeymonkey1234",
            "dragon dragon 12",
            "master master 123",
            "login login login!",
            "passw0rdpassw0rd",
            "p@ssw0rdp@ssw0rd!",
            "trustno1trustno1!",
            "accessaccess12345",
            "secretsecret1234",
            "testtesttesttest",
            "guestguestguest1",
            "changemechangeme",
            "defaultdefault12",
            "temporaltemporal1",
            "temposelfhosted1",
            "runnerrunnerrunner",
            "marathonmarathon1",
            "strava strava 123",
            "garmin garmin 123",
            "applewatchapple1",
            "correcthorsebatt1",
            "hunter22hunter2222",
            "password12345678",
            "qwerty1234567890",
            "1q2w3e4r5t6y7u8i",
            "zaq1xsw2cde3vfr4",
            "!qaz2wsx3edc4rfv",
            "asdfasdfasdfasdf",
            "qwerqwerqwerqwer",
            "zxcvzxcvzxcvzxcv",
            "emailaddress1234",
            "usernameusername",
            "administrator123",
            "rootrootrootroot",
            "supersecretsuper",
            "mustchangemust123",
            "winter2024winter2",
            "summer2024summer",
            "spring2024spring",
            "fall2024fall2024",
            "januaryjanuary01",
            "decemberdecember",
            "saturdaysaturday",
            "sundaysundaysun1",
            "whateverwhatever1",
            "nothingnothing12",
            "blahblahblahblah",
            "aaaaaaaaaaaaaaaa",
            "bbbbbbbbbbbbbbbb",
            "cccccccccccccccc",
            "dddddddddddddddd",
            "eeeeeeeeeeeeeeee",
            "0000000000000000",
            "1111111111111111",
            "0123456789012345",
            "9876543210987654",
        ];

        return new HashSet<string>(items, StringComparer.Ordinal);
    }
}
