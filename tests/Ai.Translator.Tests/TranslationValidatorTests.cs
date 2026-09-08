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
        Assert.StartsWith(TranslationValidator.EnglishFunctionWordReasonPrefix, result.Reason);
        Assert.Contains("the", result.Reason, StringComparison.OrdinalIgnoreCase);
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

    [Fact]
    public void Validate_IdentifierAStart_IsNotTreatedAsArticleA()
    {
        var result = _validator.Validate(
            "<p>a_start</p>",
            new LlmResponse { Content = "<p>Сигнал a_start принят</p>", FinishReason = "stop" });

        Assert.True(result.IsValid, result.Reason);
    }

    [Fact]
    public void Validate_MachineProtocolGibberish_Passes()
    {
        const string protocol =
            """
            <p>00011101011 HOSTILE by%? abb. 01100 orbit trajectory 66.88.345/99.34.236 then a_start=678ren</p>
            <p>011011011 HOSTILE >40000 9r1Nt if orkoid 101 begin gggg!// 1101101110000000110100 redo from start?</p>
            """;
        var result = _validator.Validate(
            protocol,
            new LlmResponse { Content = protocol, FinishReason = "stop" });

        Assert.True(result.IsValid, result.Reason);
    }

    [Fact]
    public void Validate_ProtocolLinePlusEnglishProse_Fails()
    {
        var content =
            """
            <p>00011101011 HOSTILE by%? 011011011 redo from start?</p>
            <p>the blade and the war</p>
            """;
        var result = _validator.Validate(
            "<p>One</p><p>Two</p>",
            new LlmResponse { Content = content, FinishReason = "stop" });

        Assert.False(result.IsValid);
        Assert.StartsWith(TranslationValidator.EnglishFunctionWordReasonPrefix, result.Reason);
        Assert.Contains("the", result.Reason, StringComparison.OrdinalIgnoreCase);
    }
}
