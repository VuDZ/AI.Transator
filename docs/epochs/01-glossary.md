# Эпоха 01 — Glossary

## Цель

Сделать полный корпус и рабочий словарь книги файлами Markdown: парсинг, запись, пересечение с текстом книги. Модель не вызывается. Человек правит MD руками.

## Вход / выход

**Вход:**

- корпус `*.md` по [glossary-format.md](../glossary-format.md)
- EPUB оригинала (plain text глав нужен для матчера; чтение EPUB в этой эпохе — минимально достаточное, полный пайплайн записи книги — эпоха 02)

**Выход:**

- рабочий MD: преамбула корпуса + только встретившиеся записи, порядок как в корпусе
- CLI `glossary compile` пишет файл `--out`

Если полноценный `IEpubBookService` ещё не готов, эпоха 01 может извлечь текст через временный узкий порт `IBookTextExtractor` на `VersOne.Epub`, который эпоха 02 поглотит. Не парсить EPUB вручную вторым способом.

## В скоупе

- `GlossaryDocument`, `GlossaryEntry`, `GlossaryEntryKind`
- `IGlossaryParser` / `IGlossaryWriter` (или один сервис чтения-записи)
- `IGlossaryCompiler`
- матчер границ из [glossary-format.md](../glossary-format.md): `Astartes`, `Astartes'`, `anti-Astartes`
- сохранение неизвестных ключей не обязательно; известные поля не терять
- unit-тесты: round-trip минимального MD; compile отфильтровывает невстретившееся; алиасы работают
- `glossary compile` перестаёт быть заглушкой

## Вне скоупа

- вызов LLM
- `glossary extract`
- кандидаты терминов во время перевода
- пересортировка записей по алфавиту
- разрешение омонимии `Chapter`
- парсинг вики
- запись EPUB

## Контракты

```
IGlossaryParser.Parse(string markdown) -> GlossaryDocument
IGlossaryWriter.Write(GlossaryDocument) -> string
IGlossaryCompiler.Compile(GlossaryDocument corpus, string bookPlainText) -> GlossaryDocument
```

`bookPlainText` — уже извлечённый текст, не путь. Извлечение — ответственность EPUB-порта, даже если тонкая.

CLI:

```
ai-translator glossary compile --corpus <md> --book <epub> --out <md>
```

Ошибки: нет файла, не EPUB, пустой корпус, невалидный MD без единого `##` — понятный exit code ≠ 0.

## Критерии приёмки

- эталонный MD из теста переживает parse → write → parse без потери `ru`, `type`, `notes`, `do-not-use`, `aliases`
- compile на тексте «The anti-Astartes relic» оставляет запись `Astartes` / alias и не тащит заведомо отсутствующий `Ciaphas Cain`
- повторный compile на тех же входах бит-в-бит или семантически идентичен (стабильный порядок)
- сеть не дергается

## Риски

- `\b` в .NET считает границу на `_` и цифрах иначе, чем нужно; следовать правилу «нет ASCII-буквы по бокам».
- Тянуть весь корпус в translate «пока нет compile» — запрещено даже во временном коде.
- Писать JSON-кэш словаря рядом с MD — запрещено.

## Зависимости

Эпоха 00 закрыта. Формат — [glossary-format.md](../glossary-format.md).
