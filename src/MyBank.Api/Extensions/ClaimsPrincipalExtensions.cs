using System.Security.Claims;

namespace MyBank.Api.Extensions;

internal static class ClaimsPrincipalExtensions
{
    /// <summary>Titular autenticado: a claim "sub" do token.</summary>
    public static string SubjectId(this ClaimsPrincipal user) =>
        user.FindFirstValue("sub") ?? throw new InvalidOperationException("Token sem claim 'sub'.");
}
