# Эпоха 09 — CLI progress и тихие HTTP-логи

## Цель

Долгий `translate` / `glossary extract` читается человеком: не лента `HttpClient` Information, а бар, ETA и usage (последний запрос + сумма). Пайплайн, префикс и провайдер не менять.

## Вход / выход

**Вход:** те же команды. TTY не обязателен для корректности (CI/тесты без бара).

**Выход:** stderr — прогресс (бар Spectre, текущий запрос, ETA). После каждого завершённого LLM-вызова и в конце прогона — блок статистики. stdout команд, которые и так пишут текст (`--list-pairs`), не смешивать с баром.

## В скоупе

- В [appsettings.json](../../src/Ai.Translator.Cli/appsettings.json) явно `Warning` для `System.Net.Http`, `System.Net.Http.HttpClient`, `System.Net.Http.HttpClient.llm` — даже если Default поднимут до Information в Local.
- Не логировать URL/старт POST на Information. Существующие `LogInformation` чанка/пары можно оставить или заменить прогрессом; HTTP-шум фреймворка — убрать.
- `Spectre.Console` **только в Cli**. В Core — порт прогресса (например `IRunProgress`), без ссылки на Spectre.
- Бар: done/total LLM-шагов (чанки translate с учётом уже Done при `--resume`; пары/осколки extract). Пока запрос висит — подпись текущего шага (`0007-0000`, путь пары extract).
- ETA: `TimeProvider`; среднее wall-time завершённых шагов × оставшиеся. До первого завершения — `—` / unknown, не врать.
- Статистика после шага и в конце, наглядно (не одна простыня Information):
  - последний: elapsed, `prompt_tokens`, `cached_tokens`, `completion_tokens`
  - сумма: те же поля + число шагов + failed
- `LlmResponse.CompletionTokens` (`int?`) из `usage.completion_tokens`, если шлюз отдал.
- `compile` и `--list-pairs` — без бара.
- Тесты: фейковый `IRunProgress` в Core (счётчики, ETA-формула на фиксированном `TimeProvider`). Бар Spectre в unit не обязателен. Сеть не звать.

Пакет — [stack.md](../stack.md).

## Вне скоупа

- файл стоимости / прайс в рублях (backlog `CachedTokens`)
- Spectre в Core или Tests
- менять ретраи, чекпоинты, `--pairs`, префикс
- SSE / streaming ради ETA
- TUI/GUI

## Контракты

```
IRunProgress
  Begin(totalSteps)
  StepBegin(label)
  StepEnd(LlmUsageSnapshot last, LlmUsageSnapshot totals, TimeSpan etaOrZero)
  Complete(LlmUsageSnapshot totals)

LlmResponse
  ... существующее ...
  CompletionTokens   // int?, из usage.completion_tokens
```

Cli регистрирует Spectre-реализацию. Host/тесты могут подставить `NullRunProgress` / коллектор.

Команды и флаги не переименовывать.

## Критерии приёмки

- при Default=Information в тестовом Host категория `HttpClient.llm` не пишет Start/Sending processing на успешный POST (мок handler)
- translate двух чанков: прогресс 1/2 затем 2/2; после второго в totals prompt = сумма двух usage
- `--resume` с одним Done: total = оставшиеся, бар не считает готовое заново
- extract двух пар: те же шаги, label содержит путь или индекс пары
- до первого `StepEnd` ETA не положительный
- `completion_tokens` в JSON ответа парсится; поля нет — `CompletionTokens` null
- нет `new HttpClient`; нет Spectre в csproj Core

## Риски

- Бар в stdout сломает `--list-pairs` и пайпы — только stderr.
- Spectre в Core потянет UI в тесты — запрещено.
- ETA по одному длинному первому чанку врёт на хвосте — в epoch достаточно среднего, не сглаживать «заодно».

## Зависимости

Эпохи 03–08. Новый пакет только `Spectre.Console` в Cli.
