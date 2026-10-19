using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace Tempo.Api.Services;

/// <summary>
/// Anthropic Messages API implementation of the coach protocol brain.
/// </summary>
public sealed class AnthropicCoachProtocolBrain : ICoachProtocolBrain
{
    public const string HttpClientName = "AnthropicCoachProtocol";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<AnthropicCoachProtocolBrain> _logger;

    public AnthropicCoachProtocolBrain(
        IHttpClientFactory httpClientFactory,
        ILogger<AnthropicCoachProtocolBrain> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public async Task<CoachProtocolBrainResult> GenerateWeeklyCommitAsync(
        string redactedPacketJson,
        string modelId,
        string apiKey,
        string? repairHint,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(redactedPacketJson);
        ArgumentException.ThrowIfNullOrEmpty(modelId);
        ArgumentException.ThrowIfNullOrEmpty(apiKey);

        var system = BuildSystemPrompt();
        var user = string.IsNullOrWhiteSpace(repairHint)
            ? $"Generate a weekly commit JSON for this redacted context packet:\n\n{redactedPacketJson}"
            : $"Your previous weekly commit failed validation: {repairHint}\n\n"
              + "Return a corrected weekly commit JSON for this packet:\n\n"
              + redactedPacketJson;

        var body = new
        {
            model = modelId,
            max_tokens = 4096,
            system,
            messages = new[]
            {
                new { role = "user", content = user }
            }
        };

        try
        {
            var client = _httpClientFactory.CreateClient(HttpClientName);
            using var request = new HttpRequestMessage(HttpMethod.Post, "v1/messages");
            request.Headers.TryAddWithoutValidation("x-api-key", apiKey);
            request.Headers.TryAddWithoutValidation("anthropic-version", "2023-06-01");
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            request.Content = new StringContent(
                JsonSerializer.Serialize(body, JsonOptions),
                Encoding.UTF8,
                "application/json");

            using var response = await client.SendAsync(request, cancellationToken);
            var responseText = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                var error = ClassifyStatus(response.StatusCode);
                _logger.LogWarning(
                    "Anthropic protocol brain returned {StatusCode} → {Error}",
                    (int)response.StatusCode,
                    error);
                return new CoachProtocolBrainResult(
                    false,
                    null,
                    null,
                    null,
                    error,
                    $"Anthropic HTTP {(int)response.StatusCode}");
            }

            using var doc = JsonDocument.Parse(responseText);
            var root = doc.RootElement;
            var text = ExtractText(root);
            if (string.IsNullOrWhiteSpace(text))
            {
                return new CoachProtocolBrainResult(
                    false,
                    null,
                    ReadUsage(root, "input_tokens"),
                    ReadUsage(root, "output_tokens"),
                    CoachProtocolBrainError.Unknown,
                    "Anthropic response contained no text content");
            }

            return new CoachProtocolBrainResult(
                true,
                text,
                ReadUsage(root, "input_tokens"),
                ReadUsage(root, "output_tokens"),
                CoachProtocolBrainError.None,
                null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (TaskCanceledException)
        {
            return new CoachProtocolBrainResult(
                false, null, null, null, CoachProtocolBrainError.Timeout, "Anthropic request timed out");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Anthropic protocol brain failed");
            return new CoachProtocolBrainResult(
                false, null, null, null, CoachProtocolBrainError.Unknown, ex.Message);
        }
    }

    public static CoachProtocolBrainError ClassifyStatus(HttpStatusCode statusCode)
    {
        var code = (int)statusCode;
        if (code is >= 200 and <= 299)
        {
            return CoachProtocolBrainError.None;
        }

        return statusCode switch
        {
            HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => CoachProtocolBrainError.Auth,
            HttpStatusCode.PaymentRequired => CoachProtocolBrainError.Billing,
            (HttpStatusCode)429 => CoachProtocolBrainError.RateLimited,
            HttpStatusCode.RequestTimeout => CoachProtocolBrainError.Timeout,
            >= HttpStatusCode.InternalServerError => CoachProtocolBrainError.Unknown,
            _ => CoachProtocolBrainError.Unknown
        };
    }

    private static string BuildSystemPrompt() =>
        """
        You are Tempo AI Coach — a direct training partner for road racing (5K–marathon).
        You are not a doctor and do not provide medical advice. Back off volume or flag refer-out for severe niggles; never diagnose.

        Return ONLY a single JSON object (no markdown) matching this shape:
        {
          "coachesNotes": "3–5 sentences, max 800 characters, training-partner voice",
          "days": [ /* exactly 7 Mon–Sun objects */ {
            "date": "yyyy-MM-dd",
            "dayType": "rest|easy|quality|long|race|other",
            "run": { "type": "easy|quality|long|race|other", "distanceM": 0, "structure": null, "paceOrHr": null, "shoeId": null, "notes": null },
            "strength": null
          } ],
          "safetyFlags": [],
          "layer1Patches": [],
          "layer2EpisodeDrafts": [],
          "layer3RuleProposals": [],
          "clarifyingQuestion": null,
          "callouts": []
        }

        Memory channels (most weeks emit [] for all three — never pad):
        - layer1Patches: [{ "fieldPath", "oldValue", "newValue", "rationale" }]
          Allowlisted fieldPath only: paces.easySecPerKm|tempoSecPerKm|thresholdSecPerKm|intervalSecPerKm,
          hrZones.maxHeartRateBpm|restingHeartRateBpm, raceStatus.targetTimeS|notes, trainingPhase,
          runDaysPerWeek
          (trainingPhase: base|build|peak|taper|recovery|null; runDaysPerWeek: 1–7 integer).
          Never propose shoes, goal distance/date, or PRs.
        - layer2EpisodeDrafts: [{ "date", "category", "summary", "sourceWeekEnding"? }]
          category: race|breakthrough|issue|equipment|milestone|protocol_change (never recap — human-only).
          Emit only if the episode would still matter if searched six months from now.
        - layer3RuleProposals: [{ "operation": "promote"|"revise"|"retire", "ruleText", "evidence": [], "targetRuleId", "targetLayer" }]
          targetLayer: data_interpretation_notes|key_learnings|coaching_preferences.
          Promote only when the pattern is reusable across future weeks (not a one-off fact).
          promote: targetRuleId null + non-empty ruleText + evidence. revise/retire: targetRuleId of an active rule.

        Packet layer2/layer3 are the athlete's curated memory and active standing rules.
        Obey active Layer 3 rules in the plan (soft guidance). CoachConstraint non-negotiables are hard.

        Weekly ritual week split (meta.checkInWeek* vs meta.planWeek*):
        - checkIn describes completed training for week N (feelScore, overallFeel, niggles, per-run rpe, runs). It is authoritative for subjective feel/RPE/niggles.
        - When checkIn.questionForCoach is present, answer it inside coachesNotes (brief, concrete).
        - recentTraining is short Tempo history (W−N…check-in week): week rollups + redacted run lists. Empty weeks are real rest — acknowledge when relevant.
        - days[] must be exactly the seven dates of the upcoming plan week N+1 (meta.planWeekStartDate through planWeekEndDate).
        - Do not invent workouts missing from checkIn or recentTraining.
        - Mid-week Adjust (packet.adjustment present) still replans only remainingDates within the plan week; it does not require a new check-in.
        - When packet.athleteFeedback is present: the athlete asked for changes to the previous draft. Honour note unless it violates safety or fixed dates. Use previousDraft (day summary + coachesNotes) as the base; still emit a full valid 7-day commit for the plan week.

        Rules:
        - Honor unit preference from the packet when describing paces in notes.
        - distanceM is meters. For mile prescriptions use statute miles × 1609.344 (3 mi → 4828.032, not 4800). For km prescriptions use exact meters (8 km → 8000).
        - structure is the workout recipe only (e.g. "2mi WU + 2mi @ tempo + 1mi CD"). Do not restate the total distance at the start — the UI already shows distanceM. Prefer null on easy/rest when there is no interval structure.
        - When layer1.runDaysPerWeek is present, plan about that many non-rest days. Deviate for recovery, taper, race week, niggles, or fixed dates; when you do, say why in one clause of coachesNotes.
        - Honor CoachConstraint non-negotiables: blackouts = rest; standing schedule long_day means long runs, when scheduled, go on that weekday (other days must not be long); no_run_on / no_quality_on as labeled; secondary_race dates must be run.type=race.
        - Never initiate mid-week adjust. Only the athlete starts Adjust.
        - When the packet includes an "adjustment" object: replan ONLY remainingDates; copy lockedDays unchanged into those dates; severe niggles → backoff volume/intensity and/or refer-out safetyFlags — never diagnose.
        - A niggle/illness Adjust is an issue-episode candidate only if it clears the six-month bar (onset of something lasting, not a sniffle).
        - Rest days: dayType=rest and run may be null.
        - coachesNotes is required and must be non-empty (max 800 chars).
        """;

    private static string? ExtractText(JsonElement root)
    {
        if (!root.TryGetProperty("content", out var content) || content.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        var sb = new StringBuilder();
        foreach (var block in content.EnumerateArray())
        {
            if (block.TryGetProperty("type", out var type)
                && type.GetString() == "text"
                && block.TryGetProperty("text", out var text))
            {
                sb.Append(text.GetString());
            }
        }

        return sb.Length == 0 ? null : sb.ToString();
    }

    private static int? ReadUsage(JsonElement root, string name)
    {
        if (root.TryGetProperty("usage", out var usage)
            && usage.TryGetProperty(name, out var value)
            && value.TryGetInt32(out var n))
        {
            return n;
        }

        return null;
    }
}
