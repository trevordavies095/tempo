using Tempo.Api.Models;

namespace Tempo.Api.Services;

/// <summary>
/// Shared password-hash write for change-password and the host-only reset-password command.
/// Does not validate policy, verify the current password, set cookies, log, or save.
/// </summary>
public static class UserPassword
{
    public static void ApplyNewPassword(User user, string plaintext, PasswordService passwordService)
    {
        user.PasswordHash = passwordService.HashPassword(plaintext);
        user.SessionVersion++;
    }
}
