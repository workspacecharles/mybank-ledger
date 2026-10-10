using Microsoft.EntityFrameworkCore;

namespace MyBank.Core;

/// <summary>
/// Consome a outbox e atualiza a consolidação diária. Cada lote é uma transação:
/// aplica os lançamentos e marca os eventos como processados juntos. Se algo falhar,
/// nada é confirmado e o lote volta para a fila, sem perda nem duplicidade.
/// </summary>
public sealed class DailyBalanceProjector(MyBankDbContext db)
{
    /// <returns>Quantidade de eventos processados (0 quando a fila está vazia).</returns>
    public async Task<int> ProcessBatchAsync(int batchSize, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);

        // SKIP LOCKED: vários workers dividem a fila sem pegar o mesmo evento.
        var messages = await db.OutboxMessages
            .FromSql($"""
                SELECT * FROM mybank.outbox_messages
                WHERE processed_at IS NULL
                ORDER BY id
                LIMIT {batchSize}
                FOR UPDATE SKIP LOCKED
                """)
            .ToListAsync(ct);
        if (messages.Count == 0)
        {
            return 0;
        }

        var entryIds = messages.Select(m => m.EntryId).ToList();
        var entries = await db.LedgerEntries.AsNoTracking().Where(e => entryIds.Contains(e.Id)).ToListAsync(ct);

        // Ordem fixa (conta, dia) para que workers concorrentes travem as linhas na mesma ordem.
        var days = entries
            .GroupBy(e => (e.AccountId, Date: DateOnly.FromDateTime(e.RecordedAt.UtcDateTime)))
            .OrderBy(g => g.Key.AccountId)
            .ThenBy(g => g.Key.Date);

        foreach (var day in days)
        {
            await UpsertDayAsync(day.Key.AccountId, day.Key.Date, [.. day], ct);
        }

        var now = DateTimeOffset.UtcNow;
        messages.ForEach(m => m.ProcessedAt = now);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);

        return messages.Count;
    }

    /// <summary>
    /// Soma os lançamentos ao dia. Abertura e fechamento vêm do lançamento de menor e
    /// maior sequência, então o resultado não depende da ordem em que os eventos chegam.
    /// </summary>
    private async Task UpsertDayAsync(Guid accountId, DateOnly date, List<LedgerEntry> entries, CancellationToken ct)
    {
        var first = entries.MinBy(e => e.Sequence)!;
        var last = entries.MaxBy(e => e.Sequence)!;
        var opening = first.Type == EntryType.Credit ? first.BalanceAfter - first.Amount : first.BalanceAfter + first.Amount;
        var credits = entries.Where(e => e.Type == EntryType.Credit).Sum(e => e.Amount);
        var debits = entries.Where(e => e.Type == EntryType.Debit).Sum(e => e.Amount);

        await db.Database.ExecuteSqlAsync($"""
            INSERT INTO mybank.daily_balances AS d
                (account_id, date, opening_balance, total_credits, total_debits, closing_balance,
                 entry_count, first_sequence, last_sequence, updated_at)
            VALUES ({accountId}, {date}, {opening}, {credits}, {debits}, {last.BalanceAfter},
                    {entries.Count}, {first.Sequence}, {last.Sequence}, now())
            ON CONFLICT (account_id, date) DO UPDATE SET
                total_credits = d.total_credits + EXCLUDED.total_credits,
                total_debits = d.total_debits + EXCLUDED.total_debits,
                entry_count = d.entry_count + EXCLUDED.entry_count,
                opening_balance = CASE WHEN EXCLUDED.first_sequence < d.first_sequence
                                       THEN EXCLUDED.opening_balance ELSE d.opening_balance END,
                closing_balance = CASE WHEN EXCLUDED.last_sequence > d.last_sequence
                                       THEN EXCLUDED.closing_balance ELSE d.closing_balance END,
                first_sequence = LEAST(d.first_sequence, EXCLUDED.first_sequence),
                last_sequence = GREATEST(d.last_sequence, EXCLUDED.last_sequence),
                updated_at = now()
            """, ct);
    }
}
