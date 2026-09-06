namespace Ai.Translator.Core.Llm;

public sealed class LlmException : Exception
{
    public LlmException(string message, bool isRetryable, int? httpStatusCode = null, Exception? innerException = null)
        : base(message, innerException)
    {
        IsRetryable = isRetryable;
        HttpStatusCode = httpStatusCode;
    }

    public bool IsRetryable { get; }

    public int? HttpStatusCode { get; }
}
