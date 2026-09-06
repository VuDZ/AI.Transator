using Ai.Translator.Core.Domain;
using Ai.Translator.Core.Translation;

namespace Ai.Translator.Tests;

public sealed class TranslationValidatorTests
{
    private readonly TranslationValidator _validator = new();

    [Fact]
    public void Validate_EmptyContent_Fails()
    {
        var result = _validator.Validate("<p>Hello</p>", new LlmResponse { Content = "  ", FinishReason = "stop" });

        Assert.False(result.IsValid);
        Assert.Contains("Empty", result.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("length")]
    [InlineData("max_tokens")]
    public void Validate_TruncatedFinishReason_Fails(string finishReason)
    {
        var result = _validator.Validate(
            "<p>Hello</p>",
            new LlmResponse { Content = "<p>При</p>", FinishReason = finishReason });

        Assert.False(result.IsValid);
        Assert.Contains("truncated", result.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Validate_DroppedBlocks_Fails()
    {
        var source = "<p>One</p><p>Two</p><p>Three</p><p>Four</p>";
        var result = _validator.Validate(
            source,
            new LlmResponse { Content = "<p>Один</p>", FinishReason = "stop" });

        Assert.False(result.IsValid);
        Assert.Contains("block", result.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Validate_EnglishFunctionWords_Fails()
    {
        var result = _validator.Validate(
            "<p>Hello</p>",
            new LlmResponse { Content = "<p>the blade and the war</p>", FinishReason = "stop" });

        Assert.False(result.IsValid);
        Assert.Contains("English", result.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Validate_LatinNameIsNotTreatedAsEnglishLeak()
    {
        var result = _validator.Validate(
            "<p>Eisenhorn arrived</p>",
            new LlmResponse { Content = "<p>Eisenhorn прибыл</p>", FinishReason = "stop" });

        Assert.True(result.IsValid, result.Reason);
    }

    [Fact]
    public void Validate_ReasoningWithoutHtml_Fails()
    {
        var result = _validator.Validate(
            "<p>Hello</p>",
            new LlmResponse { Content = "Конечно, вот перевод этой главы.", FinishReason = "stop" });

        Assert.False(result.IsValid);
        Assert.Contains("reasoning", result.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Validate_CleanRussianHtml_Passes()
    {
        var result = _validator.Validate(
            "<p>Hello <em>there</em></p>",
            new LlmResponse { Content = "<p>Привет <em>друг</em></p>", FinishReason = "stop" });

        Assert.True(result.IsValid, result.Reason);
    }
}
