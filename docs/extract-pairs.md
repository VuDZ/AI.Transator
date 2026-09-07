# Mapping-файл для glossary extract

Канон `--pairs` (эпоха 07). Без флага extract парует kept-индекс к индексу. Человек пишет mapping после `--list-pairs`.

Это не словарь и не JSON. Один файл на пару книг.

## Зачем

Официальный EPUB и фанперевод почти никогда не совпадают по spine. На Dawn of Fire 1 после пропуска пустых: EN 52 / RU 48, extract iкi сдвигает всю книгу. Выравнивание типичной пары — **две точечные пары + один непрерывный блок** глав, не 50 ручных строк и не GUI.

Непопавшие в mapping главы **не** отправляются в модель.

## Синтаксис

```
# комментарий до конца строки

# точечная пара: путь entry в ZIP оригинала = путь перевода
OEBPS/07-40k-Intro.xhtml = OEBPS/Text/Section0001.xhtml

# блок: последовательные kept-главы от A до B включительно на каждой стороне
# длины длин должны совпасть, иначе ошибка до LLM
OEBPS/08-40k-Content.xhtml .. OEBPS/08-40k-Content-39.xhtml = OEBPS/Text/Section0003.xhtml .. OEBPS/Text/Section0042.xhtml
```

Правила:

- пустые строки допустимы
- путь — тот же `FilePath`, что печатает `--list-pairs` (`OEBPS/…`). `\` и `/` эквивалентны, сравнение ordinal ignore case
- блок — срез в **kept reading order** (пустые spine уже отброшены), не glob по имени файла
- `A .. B` на стороне: A и B должны найтись, A не позже B
- один и тот же путь нельзя указать дважды
- нет глобов, regex, индексов `--chapters` и YAML

## CLI

```
ai-translator glossary extract --original <epub> --translation <epub> --out <md> --pairs <file>
    [--max-pairs N]
```

Без `--pairs` — i к i (эпоха 05). С `--pairs` — только строки файла. `--max-pairs N` — первые N развёрнутых пар (короткий прогон без правки файла). Не смешивать с `--list-pairs`. Не `--chapters`.

Пример для Dawn of Fire 1: [examples/dawn-of-fire-1.pairs.txt](examples/dawn-of-fire-1.pairs.txt).
