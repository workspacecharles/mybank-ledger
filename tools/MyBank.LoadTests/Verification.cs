namespace MyBank.LoadTests;

internal sealed class Verification
{
    public int AcceptedEntries { get; init; }

    public List<string> BalanceMismatches { get; } = [];

    public bool LedgerChecked { get; set; }

    public int LedgerEntries { get; set; }

    public int Lost { get; set; }

    public int DuplicatedAcceptances { get; set; }

    /// <summary>Lançamentos confirmados cuja resposta o cliente não recebeu (timeout sem retry bem-sucedido).</summary>
    public int ConfirmedWithoutResponse { get; set; }

    public TimeSpan? ProjectionConvergence { get; set; }

    /// <summary>Com <c>--worker stopped</c>: lançamentos ainda não consolidados ao fim da carga (prova de que o worker estava parado).</summary>
    public int? PendingWithWorkerStopped { get; set; }
}
