namespace MyBank.LoadTests;

internal sealed record AcceptedEntry(Guid AccountId, string Key, Guid EntryId, decimal SignedAmount, object Body);
