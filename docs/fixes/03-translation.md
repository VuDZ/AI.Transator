# Правки после эпохи 03

Статус: **пункт 1 закрыт** (проверено по коду). 59 тестов зелёные на сдаче эпохи. `translate` больше не заглушка: префикс + чанки + ретраи + `--chapters` + resume + полный EPUB, мок `ILlmProvider`. Живой HTTP — 04 (`UnconfiguredLlmProvider` в DI).

## До эпохи 04

1. **Два источника правил.** Канон слоя 1 — [`prompts/translate-system.md`](../../prompts/translate-system.md). Cli копирует файл в output (как Local.json). `StyleRulesLoader` читает его; `TranslatorOptions.StyleRules` — fallback, если файла нет. Текст промпта в C# не дублируется.

## Не дефект 03

- `glossary extract` всё ещё заглушка (05).
- Без мока `translate` упадёт на `UnconfiguredLlmProvider` — так задумано до 04.
- HAP-rewrite XHTML — наблюдение из 02, не 03.
