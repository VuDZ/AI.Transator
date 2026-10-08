# Локальный доступ к модели

Один OpenAI-compatible шлюз (Provod.ai, OpenRouter, свой прокси). BaseUrl, Model и ApiKey лежат в `appsettings.Local.json` на этой машине. В git файла нет.

## Файлы

| Файл | Git | Назначение |
| --- | --- | --- |
| `src/Ai.Translator.Cli/appsettings.json` | да | язык, ретраи, температура. Без URL, модели и ключа |
| `src/Ai.Translator.Cli/appsettings.Local.json.example` | да | образец с пустым `ApiKey` |
| `src/Ai.Translator.Cli/appsettings.Local.json` | нет | BaseUrl, Model, ApiKey |

`.gitignore`: `appsettings.Local.json`, `**/appsettings.Local.json`.

```json
{
  "Llm": {
    "BaseUrl": "https://api.provod.ai/v1",
    "Model": "openai/gpt-5.4",
    "ApiKey": "sk-...",
    "ContextWindowTokens": 128000,
    "ReservedOutputTokens": 8000,
    "TimeoutSeconds": 300,
    "CacheMode": "none",
    "SendTemperature": false
  }
}
```

`CacheMode`: `none` или `openrouter` (явный `cache_control` на префиксе).

`SendTemperature`: `false` или нет поля — в Chat Completions нет `temperature`. `true` — слать `Translator:Temperature`. На Provod/GPT-5 значение `0.3` даёт 503 (эпоха 08).

`TimeoutSeconds` — лимит всего запроса, включая чтение SSE (дефолт 300). Для больших глав можно задать 3600. 504 и обрыв потока — ретраимые.

`Translator:MaxConcurrency` (эпоха 13): сколько чанков `translate` держит в модели одновременно после прогрева первым чанком. Дефолт **2**, диапазон 1–8, в `appsettings.json` / Local. Флаг `--concurrency` перекрывает на запуск. Extract использует отдельный `Translator:ExtractMaxConcurrency`. На Provod каждый in-flight резервирует оценку стоимости отдельно; при узком балансе ставьте 1, иначе `402`.

Ключ в example — пустая строка. В настоящем Local — боевой ключ. Не логировать `ApiKey` и заголовок Authorization.

## Как подхватывается

Host, последний побеждает:

1. `appsettings.json`
2. `appsettings.Local.json` (`optional: true`)
3. env (не обязателен; не заставляем выставлять ключ вручную)

Local лежит рядом с csproj и копируется в output (`PreserveNewest`). Host читает `ContentRootPath = AppContext.BaseDirectory`: и `dotnet run`, и запуск exe из `bin/` берут копию рядом с dll, а не файл в папке проекта напрямую.

Нет файла, пустой `BaseUrl` или пустой `ApiKey` — ошибка до HTTP: скопируй example в `appsettings.Local.json` и заполни.

`--model` перекрывает `Llm:Model` на один запуск. Флага `--provider` нет.

## HttpClient

Один клиент `llm`. `BaseAddress` и `Timeout` из Options (`TimeoutSeconds`). Bearer из `Llm:ApiKey` при регистрации клиента (не из env).

## Большие главы: OpenRouter / Haiku 5.5 (эпоха 14)

Готовый профиль: [appsettings.OpenRouter.Haiku.json.example](../src/Ai.Translator.Cli/appsettings.OpenRouter.Haiku.json.example).
Скопируйте его поля Llm в appsettings.Local.json, сохранив свой ключ OpenRouter. Сам example хост автоматически не загружает.

- ContextWindowTokens=1000000 — полный контекст модели.
- MaxInputTokens=90000 — верхняя оценка всего входа translate, включая правила и рабочий словарь.
- ReservedOutputTokens=128000 — независимый лимит ответа, включая reasoning. В OpenRouter отправляется max_completion_tokens; в режиме CacheMode=none сохраняется max_tokens для совместимости шлюзов.
- TranslationOutputTokenMultiplier=2 — оценка русского ответа как 2 × токены исходного HTML. При ответе до 128000 получится максимум около 64000 исходных токенов в чанке; лимит входа может дополнительно уменьшить его из-за словаря. Глава около 200 КБ часто помещается целиком, но КБ не гарантируют число токенов.
- ReasoningEffort=none выключает thinking для экономного перевода. Для эксперимента задайте low и ReasoningTokenReserve=8192: лимит исходного HTML будет около 59904 токенов. Этот запас нужен планировщику, а не является жёстким ограничением adaptive thinking. Поддержку уровня определяет выбранная модель.
- Stream=true читает SSE постепенно, но EPUB и checkpoint получают только завершённый и проверенный ответ. Обрыв ретраится с начала чанка. TimeoutSeconds=3600 ограничивает всё чтение, а не только получение заголовков.
- CacheMode=openrouter кеширует стабильный system prefix. CacheTtl=1h сохраняет его на час; 5m или отсутствие TTL — короткий кеш. session_id вычисляется из prefix, чтобы независимые чанки использовали одну sticky session. Первый чанк прогревает кеш перед параллельным пулом.
- cached tokens в usage показывают попадание в кеш. У Haiku 5.5 минимальный кешируемый prefix — 512 токенов. Кеширование не гарантировано при коротком prefix, истечении TTL или смене провайдера. Запись кеша 1h стоит дороже 5m; кешируется только повторяемая часть, не вся новая глава.
- Новые лимиты нарезки относятся только к translate. Extract сохраняет свой планировщик; streaming, reasoning и кеш — общие настройки клиента.
- Без новых параметров прежняя нарезка и stream=false сохраняются. Смена настроек нарезки нового профиля требует нового work-dir или возврата сохранённых настроек; resume откажет до перезаписи source/translated.

Оценка length/4 и коэффициент расширения остаются эвристиками. Если ответ заканчивается length, увеличьте коэффициент (уменьшив чанк) либо выключите reasoning. Streaming не увеличивает лимит модели.

Проверено 2026-10-08: [модель и лимиты](https://openrouter.ai/anthropic/claude-haiku-5.5),
[reasoning и общий output budget](https://openrouter.ai/docs/guides/best-practices/reasoning-tokens),
[SSE](https://openrouter.ai/docs/api/reference/streaming),
[кеш, TTL и sticky sessions](https://openrouter.ai/docs/guides/best-practices/prompt-caching).

## Параллельное извлечение словаря (эпоха 16)

Translator:ExtractMaxConcurrency — отдельная настройка extract, по умолчанию 1, диапазон 1–8. Флаг glossary extract --concurrency N перекрывает её на запуск; translate продолжает использовать Translator:MaxConcurrency.

Для четырёх одновременных запросов добавьте --concurrency 4 к существующей команде extract. Первый оставшийся запрос выполняется один для прогрева стабильного префикса. Затем пул отправляет до N запросов; слияние в Markdown и запись --out/state выполняются одним писателем в исходном порядке.

Если поздний ответ уже готов, а предыдущий ещё выполняется, он хранится в state.json / PendingOutputs. При ошибке новые запросы не запускаются, уже запущенные дожидаются; сохранённые ответы используются при --resume без новой оплаты. N можно менять на resume. Не удаляйте work-dir, пока прогон не завершён.
