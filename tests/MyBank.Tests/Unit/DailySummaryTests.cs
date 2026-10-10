using MyBank.Core;

namespace MyBank.Tests.Unit;

/// <summary>Cálculo da consolidação de um dia a partir dos lançamentos (usado pelo worker).</summary>
public sealed class DailySummaryTests
{
    private static readonly Guid AccountId = Guid.NewGuid();
    private static readonly DateOnly Day = new(2026, 10, 9);

    [Fact]
    public void Dia_soma_creditos_e_debitos_e_usa_o_primeiro_e_o_ultimo_lancamento_para_abertura_e_fechamento()
    {
        // Saldo 100,00 antes do dia: +50,00 → 150,00; -30,00 → 120,00; +5,50 → 125,50.
        var entries = new[]
        {
            Entry(1, EntryType.Credit, 50.00m, 150.00m),
            Entry(2, EntryType.Debit, 30.00m, 120.00m),
            Entry(3, EntryType.Credit, 5.50m, 125.50m),
        };

        var day = LedgerRules.SummarizeDay(AccountId, Day, entries);

        Assert.Equal(AccountId, day.AccountId);
        Assert.Equal(Day, day.Date);
        Assert.Equal(100.00m, day.OpeningBalance);
        Assert.Equal(55.50m, day.TotalCredits);
        Assert.Equal(30.00m, day.TotalDebits);
        Assert.Equal(125.50m, day.ClosingBalance);
        Assert.Equal(3, day.EntryCount);
        Assert.Equal(1, day.FirstSequence);
        Assert.Equal(3, day.LastSequence);
    }

    [Fact]
    public void Ordem_dos_lancamentos_na_lista_nao_muda_o_resultado()
    {
        var entries = new[]
        {
            Entry(7, EntryType.Credit, 50.00m, 150.00m),
            Entry(8, EntryType.Debit, 30.00m, 120.00m),
            Entry(9, EntryType.Credit, 5.50m, 125.50m),
        };

        var inOrder = LedgerRules.SummarizeDay(AccountId, Day, entries);
        var reversed = LedgerRules.SummarizeDay(AccountId, Day, [.. entries.OrderByDescending(e => e.Sequence)]);

        Assert.Equivalent(inOrder, reversed);
    }

    [Fact]
    public void Abertura_desfaz_o_primeiro_lancamento_quando_ele_e_debito()
    {
        var day = LedgerRules.SummarizeDay(AccountId, Day, [Entry(1, EntryType.Debit, 40.00m, 60.00m)]);

        Assert.Equal(100.00m, day.OpeningBalance);
        Assert.Equal(60.00m, day.ClosingBalance);
    }

    [Fact]
    public void Dia_sem_lancamentos_nao_pode_ser_consolidado()
    {
        Assert.Throws<ArgumentException>(() => LedgerRules.SummarizeDay(AccountId, Day, []));
    }

    private static LedgerEntry Entry(long sequence, EntryType type, decimal amount, decimal balanceAfter) =>
        new() { AccountId = AccountId, Sequence = sequence, Type = type, Amount = amount, BalanceAfter = balanceAfter };
}
