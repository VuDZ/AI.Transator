using Ai.Translator.Cli;

using (TranslatorHost.CreateBuilder().Build())
{
    return await CommandTree.Create().Parse(args).InvokeAsync();
}
