namespace MyBank.Api;

/// <summary>Nomes das políticas de autorização e do papel de administrador.</summary>
internal static class AuthPolicies
{
    /// <summary>Usuário autenticado com claim "sub" (titular da conta).</summary>
    public const string AccountHolder = "account-holder";

    /// <summary>Exige o papel <see cref="AdminRole"/>; usado na abertura de conta.</summary>
    public const string Admin = "accounts-admin";

    public const string AdminRole = "accounts-admin";
}
