namespace Ai.Translator.Core.Abstractions;

public interface IGlossaryCompileService
{
    Task CompileAsync(
        string corpusPath,
        string bookPath,
        string outputPath,
        CancellationToken cancellationToken);
}
