using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Tempo.Api.Data;

namespace Tempo.Api.Authentication;

/// <summary>
/// JWT Bearer events: cookie-first token, JSON 401 body on challenge (Theme C).
/// </summary>
public static class TempoJwtBearerEvents
{
    public static JwtBearerEvents Create()
    {
        return new JwtBearerEvents
        {
            OnMessageReceived = context =>
            {
                context.Token = context.Request.Cookies["authToken"];
                return Task.CompletedTask;
            },
            OnTokenValidated = async context =>
            {
                if (context.Principal?.Identity?.IsAuthenticated != true)
                {
                    return;
                }

                var userIdClaim = context.Principal.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                if (string.IsNullOrEmpty(userIdClaim) || !Guid.TryParse(userIdClaim, out var userId))
                {
                    return;
                }

                var db = context.HttpContext.RequestServices.GetRequiredService<TempoDbContext>();
                var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId);
                if (user == null)
                {
                    context.Fail("User not found.");
                    return;
                }

                var sessClaim = context.Principal.FindFirst(TempoJwtClaimTypes.SessionVersion)?.Value;
                if (string.IsNullOrEmpty(sessClaim))
                {
                    if (user.SessionVersion != 0)
                    {
                        context.Fail("Session no longer valid.");
                    }

                    return;
                }

                if (sessClaim != user.SessionVersion.ToString())
                {
                    context.Fail("Session no longer valid.");
                    return;
                }
            },
            OnChallenge = async context =>
            {
                context.HandleResponse();
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                context.Response.ContentType = "application/json";
                await context.Response.WriteAsJsonAsync(new { error = AuthErrorMessages.Unauthorized });
            }
        };
    }
}
