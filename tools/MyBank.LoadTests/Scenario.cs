namespace MyBank.LoadTests;

internal enum Scenario
{
    /// <summary>50% lançamentos em contas distintas, 30% saldo, 20% consolidação diária.</summary>
    Mixed = 1,

    /// <summary>100% lançamentos concorrentes na mesma conta (pior caso de serialização).</summary>
    HotAccount = 2,

    /// <summary>100% consultas à consolidação diária.</summary>
    DailyBalance = 3,
}
