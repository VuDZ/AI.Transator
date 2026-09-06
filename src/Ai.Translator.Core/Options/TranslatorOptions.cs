namespace Ai.Translator.Core.Options;

public sealed class TranslatorOptions
{
    public const string SectionName = "Translator";

    public string TargetLanguage { get; set; } = "ru";

    public int MaxRetries { get; set; } = 3;

    public double Temperature { get; set; } = 0.3;

    public string StyleRules { get; set; } =
        """
        You are a literary translator. Translate the user XHTML fragment from English into Russian.
        Keep every HTML tag, attribute, and entity. Do not wrap the answer in markdown fences.
        Obey the working glossary and preamble below. Do not invent competing canonical names.
        Reply with the translated XHTML fragment only.
        """.Trim();
}
