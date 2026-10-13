namespace Tempo.Api.Services;

/// <summary>
/// Host kill switch for AI Coach. Env: Coach__Enabled. Default false.
/// </summary>
public class CoachConfig
{
    public bool Enabled { get; set; }
}
