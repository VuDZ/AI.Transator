using System.CommandLine;
using Ai.Translator.Core;
using Ai.Translator.Core.Abstractions;
using Ai.Translator.Core.Domain;
using Ai.Translator.Core.Glossary;
using Ai.Translator.Core.Llm;
using Microsoft.Extensions.DependencyInjection;

namespace Ai.Translator.Cli;

internal static class CommandTree
{
    public static RootCommand Create(IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(services);

        var root = new RootCommand("Literary EPUB translation with a universe glossary.");
        root.Subcommands.Add(CreateGlossaryCommand(services));
        root.Subcommands.Add(CreateTranslateCommand(services));
        return root;
    }

    private static Command CreateGlossaryCommand(IServiceProvider services)
    {
        var glossary = new Command("glossary", "Glossary compile and extract commands.");
        glossary.Subcommands.Add(CreateCompileCommand(services));
        glossary.Subcommands.Add(CreateExtractCommand(services));
        return glossary;
    }

    private static Command CreateCompileCommand(IServiceProvider services)
    {
        var corpusOption = new Option<string>("--corpus")
        {
            Description = "Full universe glossary Markdown path.",
            Required = true
        };
        var bookOption = new Option<string>("--book")
        {
            Description = "Original EPUB path.",
            Required = true
        };
        var outOption = new Option<string>("--out")
        {
            Description = "Working glossary Markdown output path.",
            Required = true
        };

        var compile = new Command("compile", "Compile a working glossary for one book.")
        {
            corpusOption,
            bookOption,
            outOption
        };

        compile.SetAction(async (parseResult, cancellationToken) =>
        {
            ArgumentNullException.ThrowIfNull(services);

            var book = parseResult.GetValue(bookOption);
            if (book is not null && InputPathGuard.IsPdf(book))
            {
                parseResult.InvocationConfiguration.Error.WriteLine(InputPathGuard.PdfRejectedMessage);
                return 1;
            }

            var corpus = parseResult.GetValue(corpusOption);
            var output = parseResult.GetValue(outOption);
            if (corpus is null || book is null || output is null)
            {
                parseResult.InvocationConfiguration.Error.WriteLine("Missing required argument.");
                return 1;
            }

            try
            {
                var compileService = services.GetRequiredService<IGlossaryCompileService>();
                await compileService.CompileAsync(corpus, book, output, cancellationToken);
                return 0;
            }
            catch (Exception ex) when (ex is FileNotFoundException or GlossaryFormatException or InvalidOperationException)
            {
                parseResult.InvocationConfiguration.Error.WriteLine(ex.Message);
                return 1;
            }
        });

        return compile;
    }

    private static Command CreateExtractCommand(IServiceProvider services)
    {
        var originalOption = new Option<string>("--original")
        {
            Description = "Original EPUB path.",
            Required = true
        };
        var translationOption = new Option<string>("--translation")
        {
            Description = "Existing translation EPUB path.",
            Required = true
        };
        var outOption = new Option<string>("--out")
        {
            Description = "Extracted glossary Markdown output path.",
            Required = true
        };

        var extract = new Command("extract", "Extract glossary candidates from an original and its translation.")
        {
            originalOption,
            translationOption,
            outOption
        };

        extract.SetAction(parseResult =>
        {
            ArgumentNullException.ThrowIfNull(services);
            parseResult.InvocationConfiguration.Error.WriteLine("Not implemented.");
            return 1;
        });

        return extract;
    }

    private static Command CreateTranslateCommand(IServiceProvider services)
    {
        var inputOption = new Option<string>("--input")
        {
            Description = "Input EPUB path.",
            Required = true
        };
        var glossaryOption = new Option<string>("--glossary")
        {
            Description = "Working glossary Markdown path.",
            Required = true
        };
        var outOption = new Option<string>("--out")
        {
            Description = "Output EPUB path.",
            Required = true
        };
        var modelOption = new Option<string?>("--model")
        {
            Description = "Override Llm:Model for this run."
        };
        var workDirOption = new Option<string?>("--work-dir")
        {
            Description = "Checkpoint working directory."
        };
        var resumeOption = new Option<bool>("--resume")
        {
            Description = "Resume unfinished chunks from the working directory."
        };
        var chaptersOption = new Option<string?>("--chapters")
        {
            Description = "1-based spine index or inclusive range, e.g. 3 or 2-4."
        };

        var translate = new Command("translate", "Translate an EPUB using a working glossary.")
        {
            inputOption,
            glossaryOption,
            outOption,
            modelOption,
            workDirOption,
            resumeOption,
            chaptersOption
        };

        translate.SetAction(async (parseResult, cancellationToken) =>
        {
            ArgumentNullException.ThrowIfNull(services);

            var input = parseResult.GetValue(inputOption);
            if (input is not null && InputPathGuard.IsPdf(input))
            {
                parseResult.InvocationConfiguration.Error.WriteLine(InputPathGuard.PdfRejectedMessage);
                return 1;
            }

            var glossary = parseResult.GetValue(glossaryOption);
            var output = parseResult.GetValue(outOption);
            if (input is null || glossary is null || output is null)
            {
                parseResult.InvocationConfiguration.Error.WriteLine("Missing required argument.");
                return 1;
            }

            try
            {
                var translation = services.GetRequiredService<IBookTranslationService>();
                var result = await translation.RunAsync(
                    new TranslationJob
                    {
                        InputPath = input,
                        GlossaryPath = glossary,
                        OutputPath = output,
                        Model = parseResult.GetValue(modelOption),
                        WorkDir = parseResult.GetValue(workDirOption),
                        Resume = parseResult.GetValue(resumeOption),
                        Chapters = parseResult.GetValue(chaptersOption)
                    },
                    cancellationToken);
                return result.HasFailures ? 1 : 0;
            }
            catch (Exception ex) when (ex is FileNotFoundException or GlossaryFormatException or InvalidOperationException or LlmException)
            {
                parseResult.InvocationConfiguration.Error.WriteLine(ex.Message);
                return 1;
            }
        });

        return translate;
    }
}
