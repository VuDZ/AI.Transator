# Эпоха 10 — Extract resume и тело ошибки LLM

## Цель

Долгий `glossary extract` не теряет уже оплаченные пары из-за одного 503. В stderr видно, что вернул шлюз, а не только `LLM gateway error (HTTP 503).` После обрыва тот же `--out` продолжается через `--resume`.

## Вход / выход

**Вход:** те же флаги extract, плюс `--work-dir` и `--resume`. `Translator:MaxRetries` и `TimeProvider` уже есть.

**Выход:** `--out` обновляется после каждого успешного фрагмента. В work-dir — `state.json` с отметками Done. При HTTP-ошибке шлюза `LlmException.Message` содержит статус и сжатое тело ответа. CLI по-прежнему печатает `ex.Message` в stderr.

## В скоупе

- `ChatCompletionsLlmProvider`: при неуспешном HTTP в сообщение эксцепции — тело ответа, схлопнутые пробелы, обрезать ~500 символов. 401/403: текущая фраза про ключ остаётся, тело — хвостом. `IsRetryable` не менять (401/403 — нет; 429 и ≥500 — да). Translate получает ту же диагностику.
- Extract: ретраи `LlmException.IsRetryable` до `Translator:MaxRetries`, пауза `100ms << attempt` через `TimeProvider` — как у чанка translate. Ретраи не новый шаг бара.
- Неретраибельная ошибка — сразу наружу. После исчерпания ретраев: `StepEnd(failed: true)`, проброс `LlmException` (пару не помечать skipped, книгу дальше не вести).
- Отдельный `IExtractCheckpointStore` + `state.json`. **Не** расширять `ICheckpointStore` перевода.
- `--work-dir` / `--resume` у extract. Дефолт work-dir: рядом с `--out`, `{stem}.extract.work`.
- `--resume` без `state.json` — ошибка. `--list-pairs` не сочетать с `--work-dir` / `--resume`.
- После каждого успешного фрагмента: полный текущий MD в `--out` и шаг Done в state.
- Совместимость resume: пути original/translation, хеш `--pairs` или режим i-к-i, `--max-pairs`, `--merge-into`, модель, `ContextWindowTokens` / `ReservedOutputTokens`. Расхождение — ошибка, не тихий микс.
- `--resume` не зовёт LLM для Done; `IRunProgress.Begin` — только оставшиеся шаги. Prefix по-прежнему один раз из `--merge-into` / пустого корпуса.
- На resume текущий документ — последний `--out` (не заново только merge-into).

## Вне скоупа

- молча пропускать упавшую пару и вести extract дальше (у translate чанк `failed` остаётся как есть)
- файл стоимости / прайс в рублях
- пересобирать prefix mid-run
- расширять `ICheckpointStore` под extract
- менять формат словаря
- SSE / streaming

## Контракты

```
ai-translator glossary extract --original <epub> --translation <epub> --out <md>
            [--merge-into <corpus.md>] [--model <id>] [--pairs <file>] [--max-pairs N]
            [--work-dir <path>] [--resume]
```

```
IExtractCheckpointStore
  LoadAsync(workDir) -> ExtractCheckpointState?
  SaveAsync(workDir, state)

ExtractCheckpointState
  OriginalPath, TranslationPath
  PairsHash            // SHA-256 hex файла --pairs; null = i-к-i
  MaxPairs             // int?
  MergeIntoPath        // string?
  Model
  ContextWindowTokens, ReservedOutputTokens
  CompletedSteps       // label шагов прогресса, тот же формат что у бара
```

`--work-dir` по умолчанию: `{dir(--out)}/{stem(--out)}.extract.work`.

`--resume` читает `--out` как текущий корпус, если есть хоть один Done. Нет `--out` при непустом CompletedSteps — ошибка.

Пустой/битый MD ответа модели — по-прежнему warning и skip merge, шаг считается успешным для чекпоинта (это не сбой шлюза).

## Критерии приёмки

- 503 с телом `No provider is currently available` → `LlmException.Message` содержит HTTP 503 и этот текст; `IsRetryable == true`
- extract: два retryable сбоя, затем успех — один шаг бара, `CompleteAsync` вызван трижды
- исчерпание ретраев на второй паре: исключение наружу, `--out` содержит первую пару
- `--resume` не повторяет Done; в модель идут только оставшиеся
- несовместимый fingerprint (другой `--pairs` / окно) — ошибка до LLM
- `--list-pairs` + `--resume` или `--work-dir` — отказ CLI без LLM
- `--resume` без state.json — ошибка
- нет `new HttpClient`; три проекта; тесты не ходят в сеть

## Риски

- Проглотить тело 503 — снова непонятно, почему шлюз отверг.
- Писать `--out` только в конце — обрыв снова сжигает токены.
- Skip упавшей главы «заодно» — дыры в словаре без глаз оператора.
- Переиспользовать `ICheckpointStore` перевода — смешать чанки EPUB с парами extract.
- `--resume` с другим `--pairs` без проверки — пропустить не те фрагменты.

## Зависимости

Эпохи 03–09 (`MaxRetries`, чекпоинты translate как образец, extract `--pairs`, прогресс).
