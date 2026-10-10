using Microsoft.EntityFrameworkCore;

namespace MyBank.Core;

public sealed record DailyBalancesResult(IReadOnlyList<DailyBalance> Days, int PendingEntries);

/// <summary>Regras de negócio da conta: abertura, lançamentos e consultas.</summary>
public sealed class LedgerService(MyBankDbContext db)
{
    public const string Currency = "BRL";
    public const decimal MaxAmount = 1_000_000_000_000m;
    public const int MaxDailyRange = 366;

    public async Task<Account> CreateAccountAsync(string? ownerId, decimal initialBalance, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(ownerId) || ownerId.Length > 128)
        {
            throw LedgerException.Validation("invalid_owner", "O titular é obrigatório e deve ter no máximo 128 caracteres.");
        }

        if (initialBalance < 0 || initialBalance > MaxAmount)
        {
            throw LedgerException.Validation("invalid_amount", $"O saldo inicial deve estar entre 0 e {MaxAmount:F2}.");
        }

        var account = new Account
        {
            Id = Guid.CreateVersion7(),
            OwnerId = ownerId,
            InitialBalance = ValidateScale(initialBalance),
            Balance = ValidateScale(initialBalance),
            CreatedAt = DateTimeOffset.UtcNow,
        };

        db.Accounts.Add(account);
        await db.SaveChangesAsync(ct);
        return account;
    }

    /// <summary>
    /// Registra um crédito ou débito. Em uma única transação: bloqueia a conta, trata a
    /// idempotência, aplica a regra de saldo e grava lançamento, novo saldo e evento da outbox.
    /// </summary>
    /// <returns>O lançamento e se ele é a repetição de uma requisição já confirmada.</returns>
    public async Task<(LedgerEntry Entry, bool Replayed)> RegisterEntryAsync(
        Guid accountId,
        string requesterId,
        string? idempotencyKey,
        string? type,
        decimal amount,
        string? currency,
        string? description,
        CancellationToken ct)
    {
        var key = ValidateIdempotencyKey(idempotencyKey);
        var entryType = ParseType(type);
        var value = ValidateAmount(amount, currency);
        description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        if (description?.Length > 500)
        {
            throw LedgerException.Validation("invalid_description", "A descrição deve ter no máximo 500 caracteres.");
        }

        await using var transaction = await db.Database.BeginTransactionAsync(ct);

        // Bloqueia a linha da conta até o commit: lançamentos da mesma conta, inclusive
        // repetições da mesma chave, são executados um de cada vez.
        var accounts = await db.Accounts
            .FromSql($"SELECT * FROM mybank.accounts WHERE id = {accountId} FOR UPDATE")
            .ToListAsync(ct);
        var account = accounts.SingleOrDefault() ?? throw AccountNotFound();
        EnsureOwner(account, requesterId);

        var existing = await db.LedgerEntries
            .AsNoTracking()
            .SingleOrDefaultAsync(e => e.AccountId == accountId && e.IdempotencyKey == key, ct);
        if (existing is not null)
        {
            var samePayload = existing.Type == entryType && existing.Amount == value && existing.Description == description;
            return samePayload
                ? (existing, true)
                : throw LedgerException.Conflict("idempotency_key_reused", "A chave de idempotência já foi usada com outro conteúdo.");
        }

        var balanceAfter = entryType == EntryType.Credit ? account.Balance + value : account.Balance - value;
        if (balanceAfter < 0)
        {
            throw LedgerException.BusinessRule("insufficient_funds", "Saldo insuficiente para o débito.");
        }

        var entry = new LedgerEntry
        {
            Id = Guid.CreateVersion7(),
            AccountId = accountId,
            Sequence = account.LastSequence + 1,
            Type = entryType,
            Amount = value,
            BalanceAfter = balanceAfter,
            RecordedAt = await db.Database.SqlQuery<DateTimeOffset>($"SELECT clock_timestamp() AS \"Value\"").SingleAsync(ct),
            Description = description,
            IdempotencyKey = key,
        };

        account.Balance = balanceAfter;
        account.LastSequence = entry.Sequence;
        db.LedgerEntries.Add(entry);
        db.OutboxMessages.Add(new OutboxMessage { EntryId = entry.Id, AccountId = accountId, CreatedAt = entry.RecordedAt });

        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return (entry, false);
    }

    public async Task<LedgerEntry> GetEntryAsync(Guid accountId, Guid entryId, string requesterId, CancellationToken ct)
    {
        await GetOwnedAccountAsync(accountId, requesterId, ct);
        return await db.LedgerEntries.AsNoTracking().SingleOrDefaultAsync(e => e.AccountId == accountId && e.Id == entryId, ct)
            ?? throw LedgerException.NotFound("entry_not_found", "Lançamento não encontrado.");
    }

    /// <summary>Saldo atual ou no instante <paramref name="at"/>, sempre a partir do razão.</summary>
    public async Task<decimal> GetBalanceAsync(Guid accountId, string requesterId, DateTimeOffset? at, CancellationToken ct)
    {
        var account = await GetOwnedAccountAsync(accountId, requesterId, ct);
        if (at is null)
        {
            return account.Balance;
        }

        // Saldo após o último lançamento até o instante; sem lançamento, vale o saldo inicial.
        var atUtc = at.Value.ToUniversalTime();
        var balance = await db.LedgerEntries
            .Where(e => e.AccountId == accountId && e.RecordedAt <= atUtc)
            .OrderByDescending(e => e.RecordedAt)
            .ThenByDescending(e => e.Sequence)
            .Select(e => (decimal?)e.BalanceAfter)
            .FirstOrDefaultAsync(ct);

        return balance ?? account.InitialBalance;
    }

    /// <summary>Consolidação diária do período e quantos lançamentos ainda não foram consolidados.</summary>
    public async Task<DailyBalancesResult> GetDailyBalancesAsync(Guid accountId, string requesterId, DateOnly from, DateOnly to, CancellationToken ct)
    {
        if (to < from || to.DayNumber - from.DayNumber + 1 > MaxDailyRange)
        {
            throw LedgerException.Validation("invalid_date_range", $"'from' deve ser menor ou igual a 'to', em um período de até {MaxDailyRange} dias.");
        }

        await GetOwnedAccountAsync(accountId, requesterId, ct);

        var days = await db.DailyBalances
            .AsNoTracking()
            .Where(d => d.AccountId == accountId && d.Date >= from && d.Date <= to)
            .OrderBy(d => d.Date)
            .ToListAsync(ct);
        var pending = await db.OutboxMessages.CountAsync(m => m.AccountId == accountId && m.ProcessedAt == null, ct);

        return new DailyBalancesResult(days, pending);
    }

    private async Task<Account> GetOwnedAccountAsync(Guid accountId, string requesterId, CancellationToken ct)
    {
        var account = await db.Accounts.AsNoTracking().SingleOrDefaultAsync(a => a.Id == accountId, ct) ?? throw AccountNotFound();
        EnsureOwner(account, requesterId);
        return account;
    }

    private static void EnsureOwner(Account account, string requesterId)
    {
        if (account.OwnerId != requesterId)
        {
            throw new LedgerException(ErrorType.Forbidden, "forbidden", "A conta não pertence ao usuário autenticado.");
        }
    }

    private static LedgerException AccountNotFound() => LedgerException.NotFound("account_not_found", "Conta não encontrada.");

    private static string ValidateIdempotencyKey(string? key)
    {
        var valid = !string.IsNullOrEmpty(key)
            && key.Length <= 128
            && key.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or ':' or '.');

        return valid
            ? key!
            : throw LedgerException.Validation(
                "invalid_idempotency_key",
                "O header Idempotency-Key é obrigatório: 1 a 128 caracteres entre letras, dígitos, '-', '_', ':' e '.'.");
    }

    private static EntryType ParseType(string? type) => type?.ToLowerInvariant() switch
    {
        "credit" => EntryType.Credit,
        "debit" => EntryType.Debit,
        _ => throw LedgerException.Validation("invalid_entry_type", "O tipo deve ser 'credit' ou 'debit'."),
    };

    private static decimal ValidateAmount(decimal amount, string? currency)
    {
        if (!string.Equals(currency, Currency, StringComparison.OrdinalIgnoreCase))
        {
            throw LedgerException.Validation("unsupported_currency", $"Somente {Currency} é suportado nesta versão.");
        }

        if (amount <= 0 || amount > MaxAmount)
        {
            throw LedgerException.Validation("invalid_amount", $"O valor deve ser positivo e até {MaxAmount:F2}.");
        }

        return ValidateScale(amount);
    }

    /// <summary>Exige no máximo duas casas e devolve o valor com exatamente duas (10 vira 10.00), como no banco.</summary>
    private static decimal ValidateScale(decimal amount) =>
        decimal.Round(amount, 2) == amount
            ? amount + 0.00m
            : throw LedgerException.Validation("invalid_scale", "O valor deve ter no máximo duas casas decimais.");
}
