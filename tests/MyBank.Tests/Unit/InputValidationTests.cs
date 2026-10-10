using System.Globalization;
using MyBank.Core;

namespace MyBank.Tests.Unit;

/// <summary>Validações de entrada aplicadas antes de qualquer gravação (critério 3).</summary>
public sealed class InputValidationTests
{
    public static TheoryData<decimal> ValidAmounts => [0.01m, 10m, 50.25m, LedgerRules.MaxAmount];

    public static TheoryData<decimal, string, string> InvalidAmounts => new()
    {
        { 0m, "BRL", "invalid_amount" },
        { -5m, "BRL", "invalid_amount" },
        { LedgerRules.MaxAmount + 0.01m, "BRL", "invalid_amount" },
        { 10.001m, "BRL", "invalid_scale" },
        { 10m, "USD", "unsupported_currency" },
        { 10m, "", "unsupported_currency" },
    };

    [Theory]
    [MemberData(nameof(ValidAmounts))]
    public void Valor_positivo_com_ate_duas_casas_em_BRL_e_aceito(decimal amount)
    {
        Assert.Equal(amount, LedgerRules.ValidateAmount(amount, "BRL"));
    }

    [Fact]
    public void Valor_aceito_sai_sempre_com_duas_casas_como_no_banco()
    {
        var value = LedgerRules.ValidateAmount(10m, "BRL");

        Assert.Equal("10.00", value.ToString(CultureInfo.InvariantCulture));
    }

    [Fact]
    public void Moeda_e_comparada_sem_diferenciar_maiusculas()
    {
        Assert.Equal(10m, LedgerRules.ValidateAmount(10m, "brl"));
    }

    [Theory]
    [MemberData(nameof(InvalidAmounts))]
    public void Valor_ou_moeda_invalidos_sao_rejeitados(decimal amount, string currency, string expectedCode)
    {
        var ex = Assert.Throws<LedgerException>(() => LedgerRules.ValidateAmount(amount, currency));

        Assert.Equal(expectedCode, ex.Code);
        Assert.Equal(ErrorType.Validation, ex.Type);
    }

    [Fact]
    public void Moeda_ausente_e_rejeitada()
    {
        var ex = Assert.Throws<LedgerException>(() => LedgerRules.ValidateAmount(10m, null));

        Assert.Equal("unsupported_currency", ex.Code);
    }

    [Theory]
    [InlineData("credit", EntryType.Credit)]
    [InlineData("CREDIT", EntryType.Credit)]
    [InlineData("debit", EntryType.Debit)]
    public void Tipo_credit_ou_debit_e_aceito(string type, EntryType expected)
    {
        Assert.Equal(expected, LedgerRules.ParseType(type));
    }

    [Theory]
    [InlineData("transfer")]
    [InlineData("")]
    [InlineData(null)]
    public void Tipo_desconhecido_e_rejeitado(string? type)
    {
        var ex = Assert.Throws<LedgerException>(() => LedgerRules.ParseType(type));

        Assert.Equal("invalid_entry_type", ex.Code);
    }

    [Theory]
    [InlineData("k1")]
    [InlineData("pedido-123_A:b.c")]
    public void Chave_de_idempotencia_valida_e_aceita(string key)
    {
        Assert.Equal(key, LedgerRules.ValidateIdempotencyKey(key));
    }

    [Fact]
    public void Chave_de_idempotencia_com_128_caracteres_e_aceita()
    {
        var key = new string('a', 128);

        Assert.Equal(key, LedgerRules.ValidateIdempotencyKey(key));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("com espaço")]
    [InlineData("acentuação")]
    [InlineData("a/b")]
    public void Chave_de_idempotencia_ausente_ou_com_caractere_proibido_e_rejeitada(string? key)
    {
        var ex = Assert.Throws<LedgerException>(() => LedgerRules.ValidateIdempotencyKey(key));

        Assert.Equal("invalid_idempotency_key", ex.Code);
    }

    [Fact]
    public void Chave_de_idempotencia_acima_de_128_caracteres_e_rejeitada()
    {
        var ex = Assert.Throws<LedgerException>(() => LedgerRules.ValidateIdempotencyKey(new string('a', 129)));

        Assert.Equal("invalid_idempotency_key", ex.Code);
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("   ", null)]
    [InlineData("  aluguel  ", "aluguel")]
    public void Descricao_e_aparada_e_vazia_vira_nula(string? description, string? expected)
    {
        Assert.Equal(expected, LedgerRules.NormalizeDescription(description));
    }

    [Fact]
    public void Descricao_acima_do_limite_e_rejeitada()
    {
        var ex = Assert.Throws<LedgerException>(() => LedgerRules.NormalizeDescription(new string('a', LedgerRules.MaxDescriptionLength + 1)));

        Assert.Equal("invalid_description", ex.Code);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("   ")]
    public void Titular_e_obrigatorio_na_abertura_de_conta(string? owner)
    {
        var ex = Assert.Throws<LedgerException>(() => LedgerRules.ValidateOwner(owner));

        Assert.Equal("invalid_owner", ex.Code);
    }

    [Fact]
    public void Titular_acima_do_limite_e_rejeitado()
    {
        var ex = Assert.Throws<LedgerException>(() => LedgerRules.ValidateOwner(new string('a', LedgerRules.MaxOwnerLength + 1)));

        Assert.Equal("invalid_owner", ex.Code);
    }

    [Fact]
    public void Saldo_inicial_zero_e_aceito()
    {
        Assert.Equal(0m, LedgerRules.ValidateInitialBalance(0m));
    }

    [Theory]
    [InlineData(-1, "invalid_amount")]
    [InlineData(1.001, "invalid_scale")]
    public void Saldo_inicial_negativo_ou_com_mais_de_duas_casas_e_rejeitado(double initialBalance, string expectedCode)
    {
        var ex = Assert.Throws<LedgerException>(() => LedgerRules.ValidateInitialBalance((decimal)initialBalance));

        Assert.Equal(expectedCode, ex.Code);
    }

    [Theory]
    [InlineData("2026-10-09", "2026-10-09")]
    [InlineData("2026-01-01", "2026-12-31")]
    [InlineData("2028-01-01", "2028-12-31")]
    public void Periodo_de_um_ate_366_dias_e_aceito(string from, string to)
    {
        LedgerRules.ValidateDateRange(DateOnly.Parse(from, CultureInfo.InvariantCulture), DateOnly.Parse(to, CultureInfo.InvariantCulture));
    }

    [Theory]
    [InlineData("2026-10-09", "2026-10-08")]
    [InlineData("2026-01-01", "2027-01-02")]
    public void Periodo_invertido_ou_acima_de_366_dias_e_rejeitado(string from, string to)
    {
        var ex = Assert.Throws<LedgerException>(() =>
            LedgerRules.ValidateDateRange(DateOnly.Parse(from, CultureInfo.InvariantCulture), DateOnly.Parse(to, CultureInfo.InvariantCulture)));

        Assert.Equal("invalid_date_range", ex.Code);
    }
}
