using Ai.Translator.Core;

namespace Ai.Translator.Tests;

public sealed class InputPathGuardTests
{
    [Theory]
    [InlineData("book.pdf")]
    [InlineData("book.PDF")]
    [InlineData(@"C:\library\chapter.Pdf")]
    public void IsPdf_RejectsPdfExtension(string path)
    {
        Assert.True(InputPathGuard.IsPdf(path));
    }

    [Theory]
    [InlineData("book.epub")]
    [InlineData("glossary.md")]
    public void IsPdf_AllowsNonPdfPaths(string path)
    {
        Assert.False(InputPathGuard.IsPdf(path));
    }

    [Theory]
    [InlineData("book.epub")]
    [InlineData("book.EPUB")]
    [InlineData(@"C:\library\chapter.Epub")]
    public void IsEpub_AcceptsEpubExtension(string path)
    {
        Assert.True(InputPathGuard.IsEpub(path));
    }

    [Theory]
    [InlineData("book.pdf")]
    [InlineData("glossary.md")]
    public void IsEpub_RejectsNonEpubPaths(string path)
    {
        Assert.False(InputPathGuard.IsEpub(path));
    }
}
