using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace Tempo.Api.Models.Responses;

/// <summary>
/// Body of <c>GET /auth/me</c>.
/// </summary>
public class CurrentUserDocument
{
    [Required]
    [JsonPropertyName("userId")]
    public Guid UserId { get; set; }

    [Required]
    [JsonPropertyName("username")]
    public string Username { get; set; } = string.Empty;

    [Required]
    [JsonPropertyName("createdAt")]
    public DateTime CreatedAt { get; set; }

    [JsonPropertyName("lastLoginAt")]
    public DateTime? LastLoginAt { get; set; }

    [Required]
    [JsonPropertyName("onboardingCompleted")]
    public bool OnboardingCompleted { get; set; }
}
