using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace MyBank.Api;

public sealed record DevTokenRequest(string? Subject, IReadOnlyList<string>? Roles);

public sealed record DevTokenResponse(string AccessToken, DateTimeOffset ExpiresAt);

/// <summary>
/// Autenticação JWT. O titular da conta é a claim "sub"; abrir conta exige o papel
/// "accounts-admin". Em produção os tokens vêm de um provedor OIDC (Auth:Authority);
/// localmente, de POST /dev/token, assinado com Auth:SigningKey.
/// </summary>
internal static class Auth
{
    public const string AccountHolderPolicy = "account-holder";
    public const string AdminPolicy = "accounts-admin";
    public const string AdminRole = "accounts-admin";

    public static string SubjectId(this ClaimsPrincipal user) =>
        user.FindFirstValue("sub") ?? throw new InvalidOperationException("Token sem claim 'sub'.");

    public static void AddMyBankAuth(this WebApplicationBuilder builder)
    {
        var config = builder.Configuration.GetSection("Auth");
        var authority = config["Authority"];

        builder.Services
            .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(jwt =>
            {
                jwt.MapInboundClaims = false; // mantém "sub" e "role" com os nomes originais
                jwt.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidIssuer = config["Issuer"],
                    ValidAudience = config["Audience"],
                    RoleClaimType = "role",
                    ClockSkew = TimeSpan.FromSeconds(30),
                };

                if (!string.IsNullOrWhiteSpace(authority))
                {
                    jwt.Authority = authority;
                }
                else
                {
                    jwt.TokenValidationParameters.IssuerSigningKey = SigningKey(builder.Configuration);
                    jwt.TokenValidationParameters.ValidAlgorithms = [SecurityAlgorithms.HmacSha256];
                }
            });

        builder.Services.AddAuthorizationBuilder()
            .AddPolicy(AccountHolderPolicy, p => p.RequireAuthenticatedUser().RequireClaim("sub"))
            .AddPolicy(AdminPolicy, p => p.RequireRole(AdminRole));
    }

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
                throw Core.LedgerException.Validation("invalid_subject", "Informe 'subject' com até 128 caracteres.");
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
                SigningCredentials = new SigningCredentials(SigningKey(app.Configuration), SecurityAlgorithms.HmacSha256),
            });

            return Results.Ok(new DevTokenResponse(token, expiresAt));
        }).AllowAnonymous();
    }

    private static SymmetricSecurityKey SigningKey(IConfiguration configuration)
    {
        var key = configuration["Auth:SigningKey"];
        if (key is null || Encoding.UTF8.GetByteCount(key) < 32)
        {
            throw new InvalidOperationException("Configure Auth:Authority (OIDC) ou Auth:SigningKey com pelo menos 32 bytes.");
        }

        return new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key));
    }
}
