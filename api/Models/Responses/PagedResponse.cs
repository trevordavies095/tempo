using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace Tempo.Api.Models.Responses;

/// <summary>
/// Paginated list wrapper shared by workout list and time-series.
/// </summary>
public class PagedResponse<T>
{
    [Required]
    [JsonPropertyName("items")]
    public List<T> Items { get; set; } = [];

    [Required]
    [JsonPropertyName("totalCount")]
    public int TotalCount { get; set; }

    [Required]
    [JsonPropertyName("page")]
    public int Page { get; set; }

    [Required]
    [JsonPropertyName("pageSize")]
    public int PageSize { get; set; }

    [Required]
    [JsonPropertyName("totalPages")]
    public int TotalPages { get; set; }
}
