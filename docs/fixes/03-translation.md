# Правки после эпохи 03

Статус: **не сдана**. В этом рабочем дереве пайплайна перевода нет.

## Блокер

1. `translate` по-прежнему пишет `Not implemented.` и exit 1 (кроме PDF).
2. Нет типов и папок эпохи 03: `ILlmProvider`, `LlmRequest` (`StablePrefix` / `VariableContent`), `ITranslationPromptFactory`, `IChapterChunker`, `ITranslationValidator`, `IBookTranslationService`, `ICheckpointStore`. Тестов на чанкер / валидатор / `--chapters` нет.

Без этого нечего принимать по [03-translation.md](../epochs/03-translation.md).

## Что на диске есть (это 02, не 03)

- `IEpubBookService` + `WriteCopyAsync`
- `glossary compile` ходит в `IEpubBookService.OpenAsync`, `IBookTextExtractor` снят — пункт 2 из [01-glossary.md](01-glossary.md) выглядит закрытым
- `EpubBookServiceTests` есть

Эпоху 02 формально в этом чате не принимали. Если сдавали её — напиши, сверю отдельно. Если 03 лежит в другой ветке/копии — этого дерева она не касается.
