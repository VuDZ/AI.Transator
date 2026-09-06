using Ai.Translator.Cli;

using var host = TranslatorHost.CreateBuilder().Build();
return await CommandTree.Create(host.Services).Parse(args).InvokeAsync();
