using System.Text;
using HtmlAgilityPack;

namespace Ai.Translator.Core.Epub;

internal static class XhtmlToPlainText
{
    public static string Convert(string html)
    {
        ArgumentNullException.ThrowIfNull(html);
        if (html.Length == 0)
        {
            return string.Empty;
        }

        var document = new HtmlDocument();
        document.LoadHtml(html);
        var root = document.DocumentNode.SelectSingleNode("//body") ?? document.DocumentNode;
        var builder = new StringBuilder();
        Append(root, builder);
        return builder.ToString();
    }

    private static void Append(HtmlNode node, StringBuilder builder)
    {
        if (node.NodeType == HtmlNodeType.Comment)
        {
            return;
        }

        if (node.NodeType == HtmlNodeType.Text)
        {
            var text = HtmlEntity.DeEntitize(node.InnerText);
            if (text.Length > 0)
            {
                builder.Append(text);
            }

            return;
        }

        if (node.Name is "script" or "style" or "head")
        {
            return;
        }

        if (node.Name is "br")
        {
            AppendNewline(builder);
            return;
        }

        foreach (var child in node.ChildNodes)
        {
            Append(child, builder);
        }

        if (IsBlock(node.Name))
        {
            AppendNewline(builder);
        }
    }

    private static void AppendNewline(StringBuilder builder)
    {
        if (builder.Length == 0 || builder[^1] == '\n')
        {
            return;
        }

        builder.Append('\n');
    }

    private static bool IsBlock(string name) => name is "p" or "div" or "h1" or "h2" or "h3" or "h4" or "h5" or "h6"
        or "li" or "blockquote" or "section" or "article" or "tr" or "table" or "ul" or "ol";
}
