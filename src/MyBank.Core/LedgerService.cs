using Microsoft.EntityFrameworkCore;
using MyBank.Domain;
using MyBank.Domain.Entities;

namespace MyBank.Core;

/// <summary>Casos de uso da conta: abertura, lançamentos e consultas. As regras ficam em <see cref="LedgerRules"/>.</summary>
public sealed class LedgerService(MyBankDbContext db)
{
    public async Task<Account> CreateAccountAsync(string? ownerId, decimal initialBalance, CancellationToken ct)
    {
        var owner = LedgerRules.ValidateOwner(ownerId);
        var balance = LedgerRules.ValidateInitialBalance(initialBalance);

        var account = new Account
        {
            Id = Guid.CreateVersion7(),
            OwnerId = owner,
            InitialBalance = balance,
            Balance = balance,
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
        var key = LedgerRules.ValidateIdempotencyKey(idempotencyKey);
        var entryType = LedgerRules.ParseType(type);
        var value = LedgerRules.ValidateAmount(amount, currency);
        description = LedgerRules.NormalizeDescription(description);

        await using var transaction = await db.Database.BeginTransactionAsync(ct);

        // Bloqueia a linha da conta até o commit: lançamentos da mesma conta, inclusive
        // repetições da mesma chave, são executados um de cada vez.
        var accounts = await db.Accounts
            .FromSql($"SELECT * FROM mybank.accounts WHERE id = {accountId} FOR UPDATE")
            .ToListAsync(ct);
        var account = accounts.SingleOrDefault() ?? throw AccountNotFound();
        LedgerRules.EnsureOwner(account, requesterId);

        var existing = await db.LedgerEntries
            .AsNoTracking()
            .SingleOrDefaultAsync(e => e.AccountId == accountId && e.IdempotencyKey == key, ct);
        if (existing is not null)
        {
            LedgerRules.EnsureSameRequest(existing, entryType, value, description);
            return (existing, true);
        }

        var balanceAfter = LedgerRules.BalanceAfter(account.Balance, entryType, value);

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
        LedgerRules.ValidateDateRange(from, to);
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
        LedgerRules.EnsureOwner(account, requesterId);
        return account;
    }

    private static LedgerException AccountNotFound() => LedgerException.NotFound("account_not_found", "Conta não encontrada.");
}
