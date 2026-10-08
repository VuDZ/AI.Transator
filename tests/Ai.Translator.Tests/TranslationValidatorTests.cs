using Ai.Translator.Core.Domain;
using Ai.Translator.Core.Translation;

namespace Ai.Translator.Tests;

public sealed class TranslationValidatorTests
{
    private readonly TranslationValidator _validator = new();

    [Theory]
    [InlineData("<p>Он увидел The на табличке.</p>")]
    [InlineData("<p>Группа The Who выступила вечером.</p>")]
    [InlineData("<p>The</p>")]
    [InlineData("<p>Он прочитал <em>The</em> на двери.</p>")]
    public void Validate_IsolatedEnglishWord_IsWarning(string content)
    {
        var result = _validator.Validate("<p>source</p>", new LlmResponse { Content = content, FinishReason = "stop" });
        Assert.True(result.IsValid, result.Reason);
        Assert.Contains("The", Assert.Single(result.Warnings));
    }

    [Theory]
    [InlineData("<p>The blade moved.</p>")]
    [InlineData("<p>Он ответил: The blade moved. Затем ушёл.</p>")]
    [InlineData("<p>The <em>blade</em> <span>moved</span>.</p>")]
    [InlineData("<p>and the</p>")]
    [InlineData("<p>&#84;he&nbsp;blade moved.</p>")]
    [InlineData("<p>The blade's edge was sharp.</p>")]
    public void Validate_ConnectedEnglishProse_FailsWithContext(string content)
    {
        var result = _validator.Validate("<p>source</p>", new LlmResponse { Content = content, FinishReason = "stop" });
        Assert.False(result.IsValid);
        Assert.StartsWith(TranslationValidator.EnglishFunctionWordReasonPrefix, result.Reason);
        Assert.Contains("context:", result.Reason);
    }

    [Theory]
    [InlineData("<p title='The blade moved'>Перевод готов.</p>")]
    [InlineData("<!-- The blade moved --><p>Перевод готов.</p>")]
    [InlineData("<head><title>The blade moved</title></head><p>Перевод готов.</p>")]
    [InlineData("<style>the and was</style><p>Перевод готов.</p>")]
    [InlineData("<script>the and was</script><p>Перевод готов.</p>")]
    [InlineData("<p>Перевод&nbsp;готов&thinsp;и&mdash;завершён.</p>")]
    [InlineData("<p>Сигнал a_start получен, котatрый и theория.</p>")]
    public void Validate_NonVisibleOrNonEnglishTokens_DoNotWarn(string content)
    {
        var result = _validator.Validate("<p>source</p>", new LlmResponse { Content = content, FinishReason = "stop" });
        Assert.True(result.IsValid, result.Reason);
        Assert.Empty(result.Warnings);
    }

    [Fact]
    public void Validate_IsolatedWordsInDifferentSentencesAndBlocks_AreNotOnePhrase()
    {
        var result = _validator.Validate("<p>source</p>", new LlmResponse
        {
            Content = "<p>The. And.</p><p>From</p><p>In</p><p>With</p>",
            FinishReason = "stop"
        });
        Assert.True(result.IsValid, result.Reason);
        Assert.Equal(3, result.Warnings.Count);
    }

    [Fact]
    public void Validate_EnglishDiagnostic_HasBoundedContext()
    {
        var result = _validator.Validate("<p>source</p>", new LlmResponse
        {
            Content = "<p>The blade moved " + new string('x', 200000) + "</p>",
            FinishReason = "stop"
        });
        Assert.False(result.IsValid);
        Assert.True(result.Reason!.Length < 200);
    }

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
