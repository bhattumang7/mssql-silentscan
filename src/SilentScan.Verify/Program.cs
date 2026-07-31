using SilentScan.Verify.Commands;

return await VerifyRootCommand.Create().Parse(args).InvokeAsync();
