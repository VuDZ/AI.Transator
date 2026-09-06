using System.Text;
using System.CommandLine;
using Ai.Translator.Cli;
using Ai.Translator.Core;
using Ai.Translator.Core.Abstractions;
using Ai.Translator.Core.Domain;
using Ai.Translator.Core.Epub;
using Ai.Translator.Core.Glossary;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace Ai.Translator.Tests;

public sealed class GlossaryPairPreviewTests
{
    [Fact]
    public async Task Preview_DifferentFrontmatter_SpineShifted_RolePairsChapter1()
    {
        var root = NewRoot();
        try
        {
            var originalPath = MinimalEpubFactory.Create(
                Path.Combine(root, "original"),
                "<h1>Preface</h1><p>Original front matter unique to this edition.</p>",
                "<h1>Chapter One</h1><p>The Emperor's light fills the first chapter.</p>");
            var translationPath = MinimalEpubFactory.Create(
                Path.Combine(root, "translation"),
                "<h1>Глава первая</h1><p>Свет Императора наполняет первую главу.</p>");

            var epub = new EpubBookService();
            var original = await epub.OpenAsync(originalPath, CancellationToken.None);
            var translation = await epub.OpenAsync(translationPath, CancellationToken.None);
            var preview = new GlossaryPairPreview().Preview(original, translation);
            var stdout = PairPreviewFormatter.Format(preview);

            var spine = preview.SpinePairs;
            Assert.Equal(2, spine.Count);
            var firstOriginal = spine[0].Original;
            var firstTranslation = spine[0].Translation;
            Assert.NotNull(firstOriginal);
            Assert.NotNull(firstTranslation);
            Assert.Equal("other", firstOriginal.Role);
            Assert.Equal("chapter 1", firstTranslation.Role);
            Assert.Contains("Preface", firstOriginal.Preview, StringComparison.Ordinal);
            Assert.Contains("Глава первая", firstTranslation.Preview, StringComparison.Ordinal);
            Assert.False(spine[0].IsTail);
            Assert.True(spine[1].IsTail);
            Assert.Equal("chapter 1", spine[1].Original!.Role);
            Assert.Null(spine[1].Translation);

            var role = Assert.Single(preview.RolePairs);
            Assert.Equal("chapter 1", role.RoleKey);
            Assert.Equal("OEBPS/chapter2.xhtml", role.OriginalPath);
            Assert.Equal("OEBPS/chapter1.xhtml", role.TranslationPath);

            Assert.Contains("Spine pairs", stdout, StringComparison.Ordinal);
            Assert.Contains("Suggested role pairs", stdout, StringComparison.Ordinal);
            Assert.Contains("chapter 1  OEBPS/chapter2.xhtml  OEBPS/chapter1.xhtml", stdout, StringComparison.Ordinal);
            Assert.Contains("TAIL", stdout, StringComparison.Ordinal);
        }
        finally
        {
            DeleteRoot(root);
        }
    }

    [Fact]
    public void Preview_DoesNotDependOnLlmProvider()
    {
        var constructor = typeof(GlossaryPairPreview).GetConstructors().Single();
        Assert.Empty(constructor.GetParameters());
        Assert.DoesNotContain(
            typeof(GlossaryPairPreview).GetConstructors().SelectMany(c => c.GetParameters()),
            p => p.ParameterType == typeof(ILlmProvider)
                || p.ParameterType == typeof(IGlossaryExtractor));
    }

    [Fact]
    public void Preview_DuplicateChapter1_BothUnpaired()
    {
        var original = Book(
            Chapter("OEBPS/a.xhtml", "Chapter One", "First copy."),
            Chapter("OEBPS/b.xhtml", "Chapter One", "Second copy."));
        var translation = Book(Chapter("OEBPS/t.xhtml", "Глава первая", "Один."));

        var preview = new GlossaryPairPreview().Preview(original, translation);

        Assert.Empty(preview.RolePairs);
        Assert.Equal(2, preview.OriginalUnpaired.Count);
        Assert.All(preview.OriginalUnpaired, row =>
        {
            Assert.Equal("chapter 1", row.RoleKey);
            Assert.Contains("duplicate role key 'chapter 1' on original", row.Reason, StringComparison.Ordinal);
        });
        var unpairedTranslation = Assert.Single(preview.TranslationUnpaired);
        Assert.Equal("chapter 1", unpairedTranslation.RoleKey);
        Assert.Equal("OEBPS/t.xhtml", unpairedTranslation.FilePath);
    }

    [Fact]
    public void Preview_OtherRoles_AreNotPairedWithEachOther()
    {
        var original = Book(Chapter("OEBPS/preface.xhtml", "Preface", "Front matter."));
        var translation = Book(Chapter("OEBPS/intro.xhtml", "Вступление", "Другой фронтматтер."));

        var preview = new GlossaryPairPreview().Preview(original, translation);

        Assert.Empty(preview.RolePairs);
        Assert.Equal("other", Assert.Single(preview.OriginalUnpaired).RoleKey);
        Assert.Equal("other", Assert.Single(preview.TranslationUnpaired).RoleKey);
        Assert.False(Assert.Single(preview.SpinePairs).IsTail);
    }

    [Theory]
    [InlineData("Chapter One", "the story begins", "chapter 1")]
    [InlineData("Chapter Twenty-One", "the story begins", "chapter 21")]
    [InlineData("Chapter 40", "the story begins", "chapter 40")]
    [InlineData("Глава двадцать первая", "история начинается", "chapter 21")]
    [InlineData("Глава сороковая", "история начинается", "chapter 40")]
    [InlineData("Dramatis Personae", "names of the host", "dramatis")]
    [InlineData("Действующие лица", "имена", "dramatis")]
    [InlineData("Prominent Vessels", "a list of ships", "vessels")]
    [InlineData("Корабли", "список", "vessels")]
    [InlineData("It is the 41st millennium", "for more than", "legend")]
    [InlineData("Десять тысяч лет", "войны", "legend")]
    [InlineData("Epilogue", "after the war", "epilogue")]
    [InlineData("Эпилог", "после войны", "epilogue")]
    [InlineData("Appendix", "extra notes", "appendix")]
    [InlineData("Приложение", "заметки", "appendix")]
    [InlineData("About the Author", "a short bio", "author")]
    [InlineData("Об авторе", "кратко", "author")]
    [InlineData("Random title", "unrelated prose", "other")]
    public void Preview_ClassifiesRoleFromHeadingAndStart(string heading, string body, string expectedRole)
    {
        var book = Book(Chapter("OEBPS/ch.xhtml", heading, body));
        var preview = new GlossaryPairPreview().Preview(book, book);
        Assert.Equal(expectedRole, preview.SpinePairs[0].Original!.Role);
    }

    [Fact]
    public void HasListPairsConflict_WhenOutMergeOrModelPresent()
    {
        Assert.True(GlossaryExtractArguments.HasListPairsConflict(true, "out.md", null, null));
        Assert.True(GlossaryExtractArguments.HasListPairsConflict(true, null, "corpus.md", null));
        Assert.True(GlossaryExtractArguments.HasListPairsConflict(true, null, null, "model-id"));
        Assert.False(GlossaryExtractArguments.HasListPairsConflict(true, null, null, null));
        Assert.False(GlossaryExtractArguments.HasListPairsConflict(false, "out.md", "corpus.md", "model-id"));
    }

    [Fact]
    public async Task ListPairs_OnFixtureEpubs_PrintsSpineAndRoleSectionsWithoutCallingLlm()
    {
        var root = NewRoot();
        try
        {
            var originalPath = MinimalEpubFactory.Create(
                Path.Combine(root, "original"),
                "<h1>Preface</h1><p>Original front matter unique to this edition.</p>",
                "<h1>Chapter One</h1><p>The Emperor's light fills the first chapter.</p>");
            var translationPath = MinimalEpubFactory.Create(
                Path.Combine(root, "translation"),
                "<h1>Глава первая</h1><p>Свет Императора наполняет первую главу.</p>");

            var llm = new Mock<ILlmProvider>(MockBehavior.Strict);
            var (exit, stdout, _) = await InvokeExtractAsync(
                llm.Object,
                "--original", originalPath,
                "--translation", translationPath,
                "--list-pairs");

            Assert.Equal(0, exit);
            Assert.Contains("Spine pairs", stdout, StringComparison.Ordinal);
            Assert.Contains("Suggested role pairs", stdout, StringComparison.Ordinal);
            Assert.Contains("chapter 1  OEBPS/chapter2.xhtml  OEBPS/chapter1.xhtml", stdout, StringComparison.Ordinal);
            Assert.Contains("TAIL", stdout, StringComparison.Ordinal);
            llm.Verify(
                x => x.CompleteAsync(It.IsAny<LlmRequest>(), It.IsAny<CancellationToken>()),
                Times.Never);
        }
        finally
        {
            DeleteRoot(root);
        }
    }

    [Fact]
    public async Task ListPairs_WithOut_ExitsNonZeroWithoutCallingLlm()
    {
        var llm = new Mock<ILlmProvider>(MockBehavior.Strict);
        var (exit, _, stderr) = await InvokeExtractAsync(
            llm.Object,
            "--original", "original.epub",
            "--translation", "translation.epub",
            "--list-pairs",
            "--out", "out.md");

        Assert.NotEqual(0, exit);
        Assert.Contains(GlossaryExtractArguments.ListPairsConflictMessage, stderr, StringComparison.Ordinal);
        llm.Verify(
            x => x.CompleteAsync(It.IsAny<LlmRequest>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ListPairs_WithMergeInto_ExitsNonZeroWithoutCallingLlm()
    {
        var llm = new Mock<ILlmProvider>(MockBehavior.Strict);
        var (exit, _, stderr) = await InvokeExtractAsync(
            llm.Object,
            "--original", "original.epub",
            "--translation", "translation.epub",
            "--list-pairs",
            "--merge-into", "corpus.md");

        Assert.NotEqual(0, exit);
        Assert.Contains(GlossaryExtractArguments.ListPairsConflictMessage, stderr, StringComparison.Ordinal);
        llm.Verify(
            x => x.CompleteAsync(It.IsAny<LlmRequest>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ListPairs_WithModel_ExitsNonZeroWithoutCallingLlm()
    {
        var llm = new Mock<ILlmProvider>(MockBehavior.Strict);
        var (exit, _, stderr) = await InvokeExtractAsync(
            llm.Object,
            "--original", "original.epub",
            "--translation", "translation.epub",
            "--list-pairs",
            "--model", "other-model");

        Assert.NotEqual(0, exit);
        Assert.Contains(GlossaryExtractArguments.ListPairsConflictMessage, stderr, StringComparison.Ordinal);
        llm.Verify(
            x => x.CompleteAsync(It.IsAny<LlmRequest>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ListPairs_PdfOriginal_SameRejectionAsFoundations()
    {
        var llm = new Mock<ILlmProvider>(MockBehavior.Strict);
        var (exit, _, stderr) = await InvokeExtractAsync(
            llm.Object,
            "--original", "book.pdf",
            "--translation", "book.epub",
            "--list-pairs");

        Assert.NotEqual(0, exit);
        Assert.Contains(InputPathGuard.PdfRejectedMessage, stderr, StringComparison.Ordinal);
        llm.Verify(
            x => x.CompleteAsync(It.IsAny<LlmRequest>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ListPairs_PdfTranslation_SameRejectionAsFoundations()
    {
        var llm = new Mock<ILlmProvider>(MockBehavior.Strict);
        var (exit, _, stderr) = await InvokeExtractAsync(
            llm.Object,
            "--original", "book.epub",
            "--translation", "book.pdf",
            "--list-pairs");

        Assert.NotEqual(0, exit);
        Assert.Contains(InputPathGuard.PdfRejectedMessage, stderr, StringComparison.Ordinal);
        llm.Verify(
            x => x.CompleteAsync(It.IsAny<LlmRequest>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    private static async Task<(int Exit, string Stdout, string Stderr)> InvokeExtractAsync(
        ILlmProvider llm,
        params string[] extractArgs)
    {
        const string json =
            """
            {
              "Llm": {
                "BaseUrl": "http://127.0.0.1:9/v1",
                "Model": "test-model",
                "ApiKey": "test-key",
                "ContextWindowTokens": 8000,
                "ReservedOutputTokens": 500
              }
            }
            """;

        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
        var configuration = new ConfigurationBuilder()
            .AddJsonStream(stream)
            .Build();

        var services = new ServiceCollection();
        services.AddTranslator(configuration);
        services.AddSingleton(llm);
        await using var provider = services.BuildServiceProvider();

        var stdout = new StringWriter();
        var stderr = new StringWriter();
        var args = new string[extractArgs.Length + 2];
        args[0] = "glossary";
        args[1] = "extract";
        extractArgs.CopyTo(args, 2);

        var parseResult = CommandTree.Create(provider).Parse(args);
        parseResult.InvocationConfiguration.Output = stdout;
        parseResult.InvocationConfiguration.Error = stderr;
        var exit = await parseResult.InvokeAsync();
        return (exit, stdout.ToString(), stderr.ToString());
    }

    private static EpubBookModel Book(params EpubChapter[] chapters) =>
        new() { Chapters = chapters };

    private static EpubChapter Chapter(string path, string heading, string body) =>
        new()
        {
            FilePath = path,
            Xhtml = "<html><body><h1>" + heading + "</h1><p>" + body + "</p></body></html>",
            PlainText = heading + "\n" + body,
            BodyInnerHtml = "<h1>" + heading + "</h1><p>" + body + "</p>"
        };

    private static string NewRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "ai-translator-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private static void DeleteRoot(string root)
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
