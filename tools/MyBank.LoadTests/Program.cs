using MyBank.LoadTests;

LoadTestOptions options;
try
{
    options = LoadTestOptions.Parse(args);
}
catch (Exception ex) when (ex is ArgumentException or FormatException)
{
    await Console.Error.WriteLineAsync(ex.Message);
    await Console.Error.WriteLineAsync(LoadTestOptions.Usage);
    return 64;
}

using var runner = new LoadTestRunner(options);
return await runner.RunAsync();
