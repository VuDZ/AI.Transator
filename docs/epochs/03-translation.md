# Эпоха 03 — Translation

## Цель

Перевести книгу по главам или кускам главы: стабильный кешируемый префикс, структурные ретраи, чекпоинты, сборка EPUB. Для проверки MVP — одна глава или диапазон, книга собирается целиком.

## Вход / выход

**Вход:**

- EPUB оригинала
- рабочий словарь книги (MD), уже скомпилированный
- модель из Local / `--model` (окно — `ContextWindowTokens` из Local)
- опционально `--chapters` (номер или диапазон в reading order)

**Выход:**

- EPUB перевода
- work-dir с чекпоинтами
- `candidates.md` (или аналог) с предложенными новыми терминами — **не** влитыми в префикс
- ненулевой exit code, если остались дырки, даже если частичный EPUB записан

## В скоупе

- `ITranslationPromptFactory`: стиль + преамбула + записи рабочего словаря = `StablePrefix`; чанк = `VariableContent`
- `ITokenEstimator`: эвристика length/4
- `IChapterChunker`: пакует блочные HTML-фрагменты в бюджет `ContextWindowTokens - prefix - reservedOutput`
- если глава целиком влезает — один чанк, не резать зря
- `ITranslationValidator`: пусто; `FinishReason` length/max_tokens; сильно просевший счётчик блоков; английские служебные слова (`the`, `and`, `was`, `with`, `that` и короткий стоп-лист); ответ-рассуждение вместо HTML (нет ни одного исходного тега, если на входе были блоки)
- ретраи: HTTP 429/5xx и провал валидатора, до `MaxRetries`, пауза через `TimeProvider`
- после исчерпания попыток — чанк `failed`, лог причины, перевод книги продолжается
- `ICheckpointStore` + `--resume`
- если хеш префикса в state ≠ текущему словарю — не мешать старые чанки с новым каноном: ошибка или явный `--force-retranslate` (достаточно ошибки; force можно отложить)
- кандидаты терминов: эвристика или отдельный мягкий проход **после** чанка не меняет префикс; в v1 достаточно выписать в файл имена/слова из исходника, которых нет в рабочем словаре и которые выглядят как реалии (простая заглавная фраза). Не вызывать вторую модель обязательно
- `translate` перестаёт быть заглушкой
- `--chapters n` и `--chapters from-to`: 1-based reading order, включительно; без флага — все главы
- выходной EPUB полный: вне диапазона — исходный XHTML, внутри — перевод
- диапазон вне книги или `from > to` — ошибка до LLM, в тексте число глав
- `state.json` помнит диапазон; `--resume` с другим `--chapters` — отказ
- префикс тот же, что на полную книгу (рабочий словарь не режется под диапазон)
- unit-тесты чанкера, валидатора, сборки префикса (словарь в prefix, текст главы не в prefix)
- unit-тест: три главы, `--chapters 2` — в выходе переведена только вторая

В этой эпохе достаточно мока `ILlmProvider`. Живой HTTP — эпоха 04. Контракт `LlmRequest` уже с `StablePrefix` / `VariableContent`, иначе кеш в 04 не взвести.

## Вне скоупа

- мини-словарь на чанк
- подмешивание кандидатов в рабочий MD
- второй редакторский прогон
- кусок эталонного перевода в префиксе
- PDF
- смена target language
- красивый progress UI (достаточно логов)
- список глав через запятую (`1,4,9`) и выбор по названию из TOC

## Контракты

```
ai-translator translate --input <epub> --glossary <md> --out <epub>
    [--model <id>] [--work-dir <path>] [--resume] [--chapters <n>|<from>-<to>]
```

```
ITranslationPromptFactory.Create(GlossaryDocument working, string styleRules) -> prefix
IChapterChunker.Chunk(EpubChapter, prefixTokenCount, modelProfile) -> IReadOnlyList<TranslationChunk>
ITranslationValidator.Validate(sourceChunk, LlmResponse) -> ValidationResult
IBookTranslationService.RunAsync(TranslationJob, ct)
ICheckpointStore.Load/Save
```

`styleRules` — текст [`prompts/translate-system.md`](../../prompts/translate-system.md), не вшивать WH40k. Cli копирует файл в output; loader читает его с диска. `TranslatorOptions.StyleRules` — только fallback, если файла нет. Factory: слой 1 + `IGlossaryWriter.Write(working)`. Не класть правила в чанкер.

Work-dir:

```
state.json
chunks/{id}.source.html
chunks/{id}.translated.html
chunks/{id}.error.txt
candidates.md
```

Хеш префикса — стабильный хеш от UTF-8 текста prefix (SHA-256 hex).

## Критерии приёмки

- на фикстуре из двух глав с моком LLM: выходной EPUB содержит оба перевода, CSS/картинки исходника на месте
- `--chapters 2` на трёх главах: вторая переведена, первая и третья — оригинал
- мок, который один раз отвечает обрывком, вызывается повторно
- после N провалов одного чанка остальные чанки всё равно переведены; exit code ≠ 0
- `--resume` не дергает LLM для чанков со статусом `done`
- смена glossary без смены work-dir → отказ resume
- префикс не содержит текст текущей главы
- candidates не читаются пайплайном обратно в prefix

## Риски

- Резать словарь «чтобы влезло» на лету — запрещено. Если префикс + минимальный блок не лезут в окно — явная ошибка «словарь слишком большой / модель слишком маленькая».
- Считать латинские имена «просочившимся английским» — валить будет весь 40k. Только стоп-лист служебных слов.
- Логировать полный prefix — дорого и шумит секретами стиля; логировать id чанка, токены, cached tokens.
- `.Result` на HttpClient — запрещено.

## Зависимости

Эпохи 00–02. Живой клиент и Local.json — 04, порт LLM уже используется.
