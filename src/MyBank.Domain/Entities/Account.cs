namespace MyBank.Domain.Entities;

/// <summary>Conta em BRL. O saldo é atualizado na mesma transação de cada lançamento.</summary>
public sealed class Account
{
    public Guid Id { get; set; }

    /// <summary>Titular (claim "sub" do token); usado na autorização por conta.</summary>
    public string OwnerId { get; set; } = string.Empty;

    public decimal InitialBalance { get; set; }

    public decimal Balance { get; set; }

    /// <summary>Sequência do último lançamento da conta; 0 quando não há lançamentos.</summary>
    public long LastSequence { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}
