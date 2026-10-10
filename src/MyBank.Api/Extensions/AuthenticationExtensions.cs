using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;

namespace MyBank.Api.Extensions;

/// <summary>
/// Autenticação JWT. O titular da conta é a claim "sub"; abrir conta exige o papel
/// "accounts-admin". Em produção os tokens vêm de um provedor OIDC (Auth:Authority);
/// localmente, de POST /dev/token, assinado com Auth:SigningKey.
/// </summary>
internal static class AuthenticationExtensions
{
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
            .AddPolicy(AuthPolicies.AccountHolder, p => p.RequireAuthenticatedUser().RequireClaim("sub"))
            .AddPolicy(AuthPolicies.Admin, p => p.RequireRole(AuthPolicies.AdminRole));
    }

    /// <summary>Chave simétrica dos tokens locais; usada na validação e no emissor de /dev/token.</summary>
    internal static SymmetricSecurityKey SigningKey(IConfiguration configuration)
    {
        var key = configuration["Auth:SigningKey"];
        if (key is null || Encoding.UTF8.GetByteCount(key) < 32)
        {
            throw new InvalidOperationException("Configure Auth:Authority (OIDC) ou Auth:SigningKey com pelo menos 32 bytes.");
        }

        return new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key));
    }
}
