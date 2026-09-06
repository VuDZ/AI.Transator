# Эпоха 06 — Extract list-pairs

## Цель

Перед живым `glossary extract` человек видит, **как тулза спарует главы**, не тратя токены. На реальных EPUB (официальный BL vs фанперевод) индекс kept reading order съезжает: предисловия разные, у одной стороны лишний Map/TOC/Extract. Полный прогон без этой проверки сжигает окно на сдвинутых парах.

Extract по-прежнему парует i к i. Эта эпоха только **печатает** две таблицы. Align в пайплайн не подключать.

## Вход / выход

**Вход:** два EPUB (оригинал и перевод). Те же `OpenAsync` и пропуск пустых spine, что в 02/05.

**Выход:** текст в stdout. Не MD-словарь, не вызов LLM.

## В скоупе

- флаг `--list-pairs` у `glossary extract`
- без `--list-pairs` поведение 05 не менять
- `--out` / `--merge-into` / `--model` вместе с `--list-pairs` — ошибка до открытия книги (достаточно одной фразы)
- PDF — тот же отказ, что в 00
- две секции в выводе:
  1. **Spine pairs** — 1-based kept-индекс, путь entry, заголовок или первые ~80 символов, число символов, отношение длин ru/en; неспаренный хвост помечен `TAIL`
  2. **Suggested role pairs** — превью, extract их не читает. Ключ роли + пути обеих сторон. Непопавшие — `UNPAIRED`
- классификатор роли по заголовку / началу plain text (без TOC-файла, без LLM):
  - `chapter N` — `Chapter Twenty-One` / `Глава двадцать первая` (и One…Forty / первая…сороковая)
  - `dramatis` — dramatis / действующ
  - `vessels` — vessels / корабл / prominent vessels
  - `legend` — 41st millennium / десять тысяч лет / it is the 41st
  - `epilogue` — epilogue / эпилог
  - `appendix` — appendix / приложен
  - `author` — about the author / об авторе
  - иначе `other` (не паровать с чужим `other`)
- пары ролей: одинаковый ключ (`dramatis`↔`dramatis`, `chapter 1`↔`chapter 1`). Два `chapter 1` на одной стороне — оба в `UNPAIRED` с причиной
- unit-тесты на фикстурных EPUB (не живые книги с диска агента): фронтматтер разный → spine-таблица сдвинута, role-таблица склеивает `chapter 1`; мок `ILlmProvider` не вызывается

## Вне скоупа

- визуальный / TUI редактор, четвёртый проект, веб
- менять парование `ExtractAsync` (остаётся индекс kept spine)
- `--align`, `--shift`, mapping-файл (два href + блок) — следующая эпоха, если понадобится
- `--chapters` на extract
- sentence-align, hunalign
- запись EPUB, вызов модели

Реальное выравнивание типичной пары BL/фан — две точечные пары (Dramatis, Vessels) и один непрерывный блок глав. Это модель будущего mapping-файла, не GUI и не скоуп 06.

## Контракты

```
ai-translator glossary extract --original <epub> --translation <epub> --list-pairs
```

Обычный extract без изменений:

```
ai-translator glossary extract --original <epub> --translation <epub> --out <md>
    [--merge-into <corpus.md>] [--model <id>]
```

```
IGlossaryPairPreview.Preview(originalBook, translationBook) -> PairPreview
```

`PairPreview` содержит spine-пары, role-пары и два списка unpaired. CLI только печатает. `GlossaryExtractor.ExtractAsync` не вызывать.

Индекс в таблицах — тот же kept reading order, что увидит extract (пустые spine уже отброшены).

## Критерии приёмки

- `--list-pairs` на двух фикстурах с разным числом предисловий: stdout содержит секцию spine (пара 1 — разные роли/превью) и секцию role (`chapter 1` на одной строке с обоими путями)
- `ILlmProvider.CompleteAsync` не вызывается
- `--list-pairs --out …` (или merge-into / model) → exit ≠ 0, без HTTP
- PDF оригинала или перевода — тот же текст отказа, что в 00
- без `--list-pairs` extract по-прежнему пишет MD (существующие тесты 05 зелёные)

## Риски

- Считать role-таблицу истиной и молча кормить её в extract — запрещено в этой эпохе.
- Классификатор врёт на не-40k книгах (`other` / `UNPAIRED`); для превью это приемлемо.
- Печатать полный plain text глав — шумно; только путь, роль, счётчик, короткий preview.
- Живые EPUB с машины агента не класть в тесты и не читать как фикстуру.

## Зависимости

Эпоха 05 закрыта. Нужны `IEpubBookService` и CLI `glossary extract`.
