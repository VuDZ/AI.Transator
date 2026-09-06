using HtmlAgilityPack;

namespace Ai.Translator.Core.Epub;

internal static class XhtmlChapterParser
{
    private static readonly HashSet<string> BlockNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "p", "h1", "h2", "h3", "h4", "h5", "h6", "blockquote", "li"
    };

    public static string GetBodyInnerHtml(string xhtml)
    {
        ArgumentNullException.ThrowIfNull(xhtml);
        var document = Load(xhtml);
        var body = FindBody(document);
        return body?.InnerHtml ?? string.Empty;
    }

    public static IReadOnlyList<string> GetBlockFragments(string xhtml)
    {
        ArgumentNullException.ThrowIfNull(xhtml);
        var document = Load(xhtml);
        var body = FindBody(document);
        if (body is null)
        {
            return [];
        }

        var fragments = new List<string>();
        CollectBlocks(body, fragments);
        if (fragments.Count == 0)
        {
            var inner = body.InnerHtml;
            if (!string.IsNullOrWhiteSpace(inner))
            {
                fragments.Add(inner);
            }
        }

        return fragments;
    }

    public static string ReplaceBodyInnerHtml(string xhtml, string bodyInnerHtml)
    {
        ArgumentNullException.ThrowIfNull(xhtml);
        ArgumentNullException.ThrowIfNull(bodyInnerHtml);

        var document = Load(xhtml);
        var body = FindBody(document)
            ?? throw new InvalidOperationException("XHTML has no body element.");
        body.InnerHtml = bodyInnerHtml;

        var html = document.DocumentNode.OuterHtml;
        if (HasXmlDeclaration(xhtml) && !HasXmlDeclaration(html))
        {
            return "<?xml version=\"1.0\" encoding=\"UTF-8\"?>" + "\n" + html;
        }

        return html;
    }

    private static HtmlDocument Load(string xhtml)
    {
        var document = new HtmlDocument
        {
            OptionOutputAsXml = true,
            OptionWriteEmptyNodes = true
        };
        document.LoadHtml(xhtml);
        return document;
    }

    private static HtmlNode? FindBody(HtmlDocument document)
    {
        foreach (var node in document.DocumentNode.Descendants())
        {
            if (node.NodeType == HtmlNodeType.Element
                && string.Equals(node.Name, "body", StringComparison.OrdinalIgnoreCase))
            {
                return node;
            }
        }

        return null;
    }

    private static void CollectBlocks(HtmlNode node, List<string> fragments)
    {
        if (node.NodeType == HtmlNodeType.Element && BlockNames.Contains(node.Name))
        {
            fragments.Add(node.OuterHtml);
            return;
        }

        foreach (var child in node.ChildNodes)
        {
            CollectBlocks(child, fragments);
        }
    }

    private static bool HasXmlDeclaration(string xml)
    {
        var span = xml.AsSpan().TrimStart();
        return span.StartsWith("<?xml", StringComparison.OrdinalIgnoreCase);
    }
}
