using System.Text;
using System.Text.Json;
using Ai.Translator.Core.Abstractions;
using Ai.Translator.Core.Domain;

namespace Ai.Translator.Core.Translation;

public sealed class FileCheckpointStore : ICheckpointStore
{
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public async Task<TranslationCheckpointState?> LoadAsync(string workDir, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(workDir);
        var path = StatePath(workDir);
        if (!File.Exists(path))
        {
            return null;
        }

        var json = await File.ReadAllTextAsync(path, Utf8NoBom, cancellationToken).ConfigureAwait(false);
        return JsonSerializer.Deserialize<TranslationCheckpointState>(json, JsonOptions);
    }

    public async Task SaveAsync(string workDir, TranslationCheckpointState state, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(workDir);
        ArgumentNullException.ThrowIfNull(state);
        Directory.CreateDirectory(workDir);
        var json = JsonSerializer.Serialize(state, JsonOptions);
        await File.WriteAllTextAsync(StatePath(workDir), json, Utf8NoBom, cancellationToken).ConfigureAwait(false);
    }

    public Task WriteChunkSourceAsync(string workDir, string chunkId, string html, CancellationToken cancellationToken) =>
        WriteChunkFileAsync(workDir, chunkId, ".source.html", html, cancellationToken);

    public Task WriteChunkTranslatedAsync(string workDir, string chunkId, string html, CancellationToken cancellationToken) =>
        WriteChunkFileAsync(workDir, chunkId, ".translated.html", html, cancellationToken);

    public Task WriteChunkErrorAsync(string workDir, string chunkId, string error, CancellationToken cancellationToken) =>
        WriteChunkFileAsync(workDir, chunkId, ".error.txt", error, cancellationToken);

    public async Task<string?> ReadChunkTranslatedAsync(string workDir, string chunkId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(workDir);
        ArgumentNullException.ThrowIfNull(chunkId);
        var path = ChunkPath(workDir, chunkId, ".translated.html");
        if (!File.Exists(path))
        {
            return null;
        }

        return await File.ReadAllTextAsync(path, Utf8NoBom, cancellationToken).ConfigureAwait(false);
    }

    public async Task WriteCandidatesAsync(string workDir, string markdown, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(workDir);
        ArgumentNullException.ThrowIfNull(markdown);
        Directory.CreateDirectory(workDir);
        await File.WriteAllTextAsync(CandidatesPath(workDir), markdown, Utf8NoBom, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<string?> ReadCandidatesAsync(string workDir, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(workDir);
        var path = CandidatesPath(workDir);
        if (!File.Exists(path))
        {
            return null;
        }

        return await File.ReadAllTextAsync(path, Utf8NoBom, cancellationToken).ConfigureAwait(false);
    }

    private static async Task WriteChunkFileAsync(
        string workDir,
        string chunkId,
        string suffix,
        string content,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(workDir);
        ArgumentNullException.ThrowIfNull(chunkId);
        ArgumentNullException.ThrowIfNull(content);
        var path = ChunkPath(workDir, chunkId, suffix);
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await File.WriteAllTextAsync(path, content, Utf8NoBom, cancellationToken).ConfigureAwait(false);
    }

    private static string StatePath(string workDir) => Path.Combine(workDir, "state.json");

    private static string CandidatesPath(string workDir) => Path.Combine(workDir, "candidates.md");

    private static string ChunkPath(string workDir, string chunkId, string suffix)
    {
        var safe = Sanitize(chunkId);
        return Path.Combine(workDir, "chunks", safe + suffix);
    }

    private static string Sanitize(string chunkId)
    {
        var chars = chunkId.ToCharArray();
        for (var i = 0; i < chars.Length; i++)
        {
            if (!char.IsAsciiLetterOrDigit(chars[i]) && chars[i] != '-')
            {
                chars[i] = '_';
            }
        }

        return new string(chars);
    }
}
