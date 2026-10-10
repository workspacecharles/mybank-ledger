using System.ComponentModel;
using System.Diagnostics;

namespace MyBank.LoadTests;

/// <summary>Versão do código no momento da execução, para que o relatório possa ser reproduzido.</summary>
internal sealed record CodeVersion(string? Commit, bool HasLocalChanges)
{
    public static CodeVersion Detect()
    {
        var commit = Git("rev-parse --short HEAD");
        var status = commit is null ? null : Git("status --porcelain --untracked-files=no");
        return new CodeVersion(commit, !string.IsNullOrEmpty(status));
    }

    public string Describe() => Commit switch
    {
        null => "desconhecida (git indisponível)",
        _ when HasLocalChanges => $"commit `{Commit}` com alterações locais não commitadas",
        _ => $"commit `{Commit}`",
    };

    private static string? Git(string arguments)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo("git", arguments)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            });
            if (process is null)
            {
                return null;
            }

            var output = process.StandardOutput.ReadToEnd().Trim();
            process.WaitForExit();
            return process.ExitCode == 0 ? output : null;
        }
        catch (Win32Exception)
        {
            return null; // git não instalado
        }
    }
}
