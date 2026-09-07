# Эпоха 07 — Extract --pairs

## Цель

`glossary extract` берёт пары глав из mapping-файла, а не из индекса kept spine. На BL vs фанперевод iкi сдвигает книгу; человек после `--list-pairs` пишет две точечные пары и один блок. Непопавшее в файл в модель не уходит.

Формат — [extract-pairs.md](../extract-pairs.md). Не менять синтаксис «заодно».

## Вход / выход

**Вход:** два EPUB, `--out`, `--pairs <file>`. Опционально `--merge-into`, `--model`.

**Выход:** тот же MD, что в 05, но LLM зовётся только по строкам mapping (порядок строк файла; блок разворачивается в kept-порядке).

## В скоупе

- `--pairs` у `glossary extract`
- без `--pairs` — поведение 05 (i к i), тесты 05 зелёные
- `--list-pairs` вместе с `--pairs` — ошибка до открытия книг
- `--pairs` без `--out` — ошибка
- парсер mapping: комментарии `#`, пустые строки, `left = right`, `A .. B = C .. D`
- пути как `FilePath` из `--list-pairs`; `\`/`/` и ordinal ignore case
- блок — срез kept reading order, не glob; A и B должны найтись, A не позже B; число глав слева = числу справа, иначе ошибка **до LLM**
- путь не найден в книге — ошибка до LLM с этим путём
- один путь дважды в файле — ошибка до LLM
- главы вне mapping не отправлять
- prefix extract тот же, что в 05 (правила + известные English), не резать под пару
- unit-тесты на фикстурных EPUB (не живые книги): точечная пара и блок; VariableContent совпадает с mapping, не со spine-индексом; глава вне файла не уходит; несовпадение длин блока — `CompleteAsync` ни разу

## Вне скоупа

- GUI / TUI
- `--align`, `--shift`, `--chapters` на extract
- менять `--list-pairs` и классификатор ролей
- sentence-align
- глобы, YAML, JSON рядом с mapping

## Контракты

```
ai-translator glossary extract --original <epub> --translation <epub> --out <md> --pairs <file>
    [--merge-into <corpus.md>] [--model <id>]
```

```
IGlossaryPairMapParser.Parse(text) -> IReadOnlyList<PairMapEntry>
IGlossaryExtractor.ExtractAsync(..., pairMapOrNull, ct)
```

`pairMapOrNull == null` — i к i как в 05. Не null — только разрешённые пары. Разрешение путей к `EpubChapter` — в extractor/сервисе после `OpenAsync`.

Пример: [examples/dawn-of-fire-1.pairs.txt](../examples/dawn-of-fire-1.pairs.txt).

## Критерии приёмки

- фикстура: EN три главы A,B,C; RU три главы X,Y,Z; mapping `A=Y` и не B/C — один вызов LLM, VariableContent содержит текст A и Y, не B и не X
- блок `A..B = X..Y` на четырёх главах по две: два вызова, порядок A-X затем B-Y
- путь из mapping отсутствует в EPUB — ошибка, LLM не вызван
- длины блока 2 vs 3 — ошибка, LLM не вызван
- без `--pairs` extract 05 не ломается
- PDF — тот же отказ, что в 00

## Риски

- Смешать `--pairs` и iкi в одном прогоне — запрещено.
- Считать role-таблицу 06 mapping-ом — нет, только файл.
- Живые EPUB с диска агента в тесты не класть.

## Зависимости

Эпохи 05–06. Формат — [extract-pairs.md](../extract-pairs.md).
