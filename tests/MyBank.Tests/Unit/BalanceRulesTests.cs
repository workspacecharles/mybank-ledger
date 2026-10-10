using MyBank.Core;

namespace MyBank.Tests.Unit;

/// <summary>Regra de saldo (critérios 1, 2 e 4), repetição com a mesma chave (6 e 7) e titularidade da conta.</summary>
public sealed class BalanceRulesTests
{
    [Fact]
    public void Credito_soma_exatamente_o_valor_ao_saldo()
    {
        Assert.Equal(150.25m, LedgerRules.BalanceAfter(100.00m, EntryType.Credit, 50.25m));
    }

    [Fact]
    public void Debito_subtrai_exatamente_o_valor_do_saldo()
    {
        Assert.Equal(69.90m, LedgerRules.BalanceAfter(100.00m, EntryType.Debit, 30.10m));
    }

    [Fact]
    public void Debito_de_todo_o_saldo_e_aceito_e_zera_a_conta()
    {
        Assert.Equal(0.00m, LedgerRules.BalanceAfter(100.00m, EntryType.Debit, 100.00m));
    }

    [Fact]
    public void Debito_acima_do_saldo_e_rejeitado()
    {
        var ex = Assert.Throws<LedgerException>(() => LedgerRules.BalanceAfter(100.00m, EntryType.Debit, 100.01m));

        Assert.Equal("insufficient_funds", ex.Code);
        Assert.Equal(ErrorType.BusinessRule, ex.Type);
    }

    [Fact]
    public void Mesma_chave_com_mesmo_conteudo_e_aceita_como_repeticao()
    {
        var existing = Entry(EntryType.Debit, 10.00m, "aluguel");

        LedgerRules.EnsureSameRequest(existing, EntryType.Debit, 10.00m, "aluguel");
    }

    [Theory]
    [InlineData(EntryType.Credit, 10.00, "aluguel")]
    [InlineData(EntryType.Debit, 10.01, "aluguel")]
    [InlineData(EntryType.Debit, 10.00, "outra")]
    [InlineData(EntryType.Debit, 10.00, null)]
    public void Mesma_chave_com_outro_conteudo_e_conflito(EntryType type, double amount, string? description)
    {
        var existing = Entry(EntryType.Debit, 10.00m, "aluguel");

        var ex = Assert.Throws<LedgerException>(() => LedgerRules.EnsureSameRequest(existing, type, (decimal)amount, description));

        Assert.Equal("idempotency_key_reused", ex.Code);
        Assert.Equal(ErrorType.Conflict, ex.Type);
    }

    [Fact]
    public void Titular_da_conta_tem_acesso()
    {
        LedgerRules.EnsureOwner(new Account { OwnerId = "ana" }, "ana");
    }

    [Fact]
    public void Outro_usuario_nao_tem_acesso_a_conta()
    {
        var ex = Assert.Throws<LedgerException>(() => LedgerRules.EnsureOwner(new Account { OwnerId = "ana" }, "bruno"));

        Assert.Equal("forbidden", ex.Code);
        Assert.Equal(ErrorType.Forbidden, ex.Type);
    }

    private static LedgerEntry Entry(EntryType type, decimal amount, string? description) =>
        new() { Type = type, Amount = amount, Description = description };
}
