using SilentScan.Bench.Commands;

return await BenchRootCommand.Create().Parse(args).InvokeAsync();
