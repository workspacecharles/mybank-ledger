using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using MyBank.Api.Contracts;
using MyBank.Api.Extensions;
using MyBank.Domain;

namespace MyBank.Api.Endpoints;

internal static class DevTokenEndpoints
{
    /// <summary>Emissor de tokens local; só existe em Development com Auth:EnableDevTokenEndpoint=true.</summary>
    public static void MapDevTokenEndpoint(this WebApplication app)
    {
        var config = app.Configuration.GetSection("Auth");
        if (!app.Environment.IsDevelopment() || !config.GetValue<bool>("EnableDevTokenEndpoint"))
        {
            return;
        }

        app.MapPost("/dev/token", (DevTokenRequest request) =>
        {
            if (string.IsNullOrWhiteSpace(request.Subject) || request.Subject.Length > 128)
            {
                throw LedgerException.Validation("invalid_subject", "Informe 'subject' com até 128 caracteres.");
            }

            var claims = new Dictionary<string, object> { ["sub"] = request.Subject };
            if (request.Roles is { Count: > 0 })
            {
                claims["role"] = request.Roles.ToArray();
            }

            var expiresAt = DateTimeOffset.UtcNow.AddHours(2);
            var token = new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
            {
                Issuer = config["Issuer"],
                Audience = config["Audience"],
                Expires = expiresAt.UtcDateTime,
                Claims = claims,
                SigningCredentials = new SigningCredentials(AuthenticationExtensions.SigningKey(app.Configuration), SecurityAlgorithms.HmacSha256),
            });

            return Results.Ok(new DevTokenResponse(token, expiresAt));
        }).AllowAnonymous();
    }
}
