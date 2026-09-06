using System.Security.Cryptography;
using System.Text;

namespace Ai.Translator.Core.Translation;

public static class PrefixHasher
{
    public static string ComputeSha256Hex(string prefix)
    {
        ArgumentNullException.ThrowIfNull(prefix);
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(prefix));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
