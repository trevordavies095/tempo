namespace Tempo.Api.Utils;

/// <summary>
/// Utility class for sanitizing user input before logging to prevent log injection attacks.
/// Removes newline characters and other control characters that could be used to forge log entries.
/// </summary>
public static class LogSanitizer
{
    /// <summary>
    /// Sanitizes a string for safe logging by removing newline characters and other control characters.
    /// This prevents log injection attacks where malicious input could forge new log entries.
    /// </summary>
    /// <param name="input">The user input string to sanitize</param>
    /// <returns>Sanitized string with newlines and control characters removed</returns>
    public static string Sanitize(string? input)
    {
        if (string.IsNullOrEmpty(input))
        {
            return string.Empty;
        }

        // Remove newline characters (\r, \n) and other control characters
        // Replace with space to preserve readability while preventing injection
        var sanitized = input
            .Replace("\r", " ")
            .Replace("\n", " ")
            .Replace("\t", " ");

        // Remove any remaining control characters (characters < 32 except space)
        var chars = sanitized.ToCharArray();
        for (int i = 0; i < chars.Length; i++)
        {
            if (char.IsControl(chars[i]) && chars[i] != ' ')
            {
                chars[i] = ' ';
            }
        }

        // Collapse multiple consecutive spaces into a single space
        var result = new string(chars);
        while (result.Contains("  "))
        {
            result = result.Replace("  ", " ");
        }

        return result.Trim();
    }
}
