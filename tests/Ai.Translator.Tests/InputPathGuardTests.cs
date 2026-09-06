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
}
