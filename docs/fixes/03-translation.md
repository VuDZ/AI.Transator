# Правки после эпохи 03

Статус: **принята с замечаниями**. 59 тестов зелёные. `translate` больше не заглушка: префикс + чанки + ретраи + `--chapters` + resume + полный EPUB, мок `ILlmProvider`. Живой HTTP — 04 (`UnconfiguredLlmProvider` в DI).

## До эпохи 04

1. **Два источника правил.** Канон слоя 1 — [`prompts/translate-system.md`](../../prompts/translate-system.md). Код берёт `TranslatorOptions.StyleRules` (короткий дефолт в C#). Нужен один источник: Cli копирует `prompts/translate-system.md` в output (как Local.json), factory/Host читает файл; Options — только fallback если файла нет. Не дублировать текст в двух местах.

## Не дефект 03

- `glossary extract` всё ещё заглушка (05).
- Без мока `translate` упадёт на `UnconfiguredLlmProvider` — так задумано до 04.
- HAP-rewrite XHTML — наблюдение из 02, не 03.
