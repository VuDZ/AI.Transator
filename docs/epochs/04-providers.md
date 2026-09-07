# Эпоха 04 — Providers

## Цель

Один OpenAI-compatible клиент на **нестриминговом Chat Completions**. URL, модель и ключ — из `appsettings.Local.json`. Пайплайн отдаёт `StablePrefix` + `VariableContent`. Нарезка смотрит на окно из Local.

## Вход / выход

**Вход:** `LlmRequest` из пайплайна.

**Выход:** `LlmResponse` с текстом, `FinishReason`, usage и `CachedTokens`, если шлюз их отдал.

## В скоупе

- `ILlmProvider` как в [architecture.md](../architecture.md) — без резолвера по имени вендора
- один HttpClient `llm`, `BaseAddress` из `Llm:BaseUrl` после мержа Local
- POST `{BaseUrl}/chat/completions`, `stream: false`, `Authorization: Bearer` из `Llm:ApiKey`
- `CacheMode` из Local: `none` или `openrouter` (`cache_control` на префиксе)
- `max_tokens`, `model`; `temperature` — только если `Llm:SendTemperature` (эпоха 08)
- `Llm:TimeoutSeconds` из Options на именованный HttpClient `llm` (дефолт **300**; дефолт BCL 100 с слишком короток для главы). Значение ≤ 0 — ошибка до HTTP
- HTTP 401/403 — «ключ / шлюз»; 429 и 5xx включая **504** — ретраимые
- unit-тесты сериализации (мок `HttpMessageHandler`): при `openrouter` есть `cache_control`, при `none` — нет; prefix в system, variable в user
- `--model` перекрывает Local на запуск

Форма файла — [local-config.md](../local-config.md).

## Вне скоупа

- три захардкоженных вендора в git
- Responses API, Anthropic Messages, streaming / SSE (таймаут шлюза не повод сдавать `stream: false`), embeddings
- ключ в committed json или обязательный env
- подбор модели «кто лучше переводит»

## Контракты

Нет `ILlmProviderResolver`. Пайплайн получает `ILlmProvider` из DI.

Нет `BaseUrl` / `Model` / `ApiKey` в committed `appsettings.json`. Example содержит пустой `ApiKey`.

## Критерии приёмки

- нет `new HttpClient`
- `HttpClient.Timeout` = `Llm:TimeoutSeconds` (дефолт 300); ≤ 0 — ошибка до запроса
- пустой ключ или нет Local/`BaseUrl` — ошибка до запроса, с отсылкой к example
- тесты не ходят в сеть и не читают настоящий Local с машины агента как фикстуру (подкладывать тестовый JSON)
- `CachedTokens` парсится из usage, если поле есть, иначе null
- смена BaseUrl в тестовом Options меняет адрес клиента, не ветвит `BookTranslationService`

## Риски

- Закоммитить заполненный Local — не должен пройти `.gitignore`
- Склеить prefix с главой в одном user — ломает кеш
- Логировать Bearer — запрещено
- Длинная глава без стрима на прокси ловит 504: таймаут 300 с + ретрай, не SSE

## Зависимости

Эпоха 03 уже шлёт `LlmRequest`. Эта эпоха подключает живой Chat Completions к Local, не меняя пайплайн.
