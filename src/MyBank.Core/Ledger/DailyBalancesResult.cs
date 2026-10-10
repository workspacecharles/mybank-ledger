using MyBank.Domain.Entities;


namespace MyBank.Core.Ledger;

/// <summary>Consolidação diária de um período e quantos lançamentos da conta ainda não foram consolidados.</summary>
public sealed record DailyBalancesResult(IReadOnlyList<DailyBalance> Days, int PendingEntries);
