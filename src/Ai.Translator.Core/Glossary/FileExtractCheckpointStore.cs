using System.Text;
using System.Text.Json;
using Ai.Translator.Core.Abstractions;
using Ai.Translator.Core.Domain;

namespace Ai.Translator.Core.Glossary;

public sealed class FileExtractCheckpointStore : IExtractCheckpointStore
{
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public async Task<ExtractCheckpointState?> LoadAsync(string workDir, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(workDir);
        var path = StatePath(workDir);
        if (!File.Exists(path))
        {
            return null;
        }

        var json = await File.ReadAllTextAsync(path, Utf8NoBom, cancellationToken).ConfigureAwait(false);
        return JsonSerializer.Deserialize<ExtractCheckpointState>(json, JsonOptions);
    }

    public async Task SaveAsync(string workDir, ExtractCheckpointState state, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(workDir);
        ArgumentNullException.ThrowIfNull(state);
        Directory.CreateDirectory(workDir);
        var json = JsonSerializer.Serialize(state, JsonOptions);
        await File.WriteAllTextAsync(StatePath(workDir), json, Utf8NoBom, cancellationToken).ConfigureAwait(false);
    }

    private static string StatePath(string workDir) => Path.Combine(workDir, "state.json");
}
