# Эпоха 04 — Providers

## Цель

Один OpenAI-compatible клиент. URL и модель — из локального файла, ключ — из env. Пайплайн по-прежнему отдаёт `StablePrefix` + `VariableContent`. Нарезка смотрит на окно из Local.

## Вход / выход

**Вход:** `LlmRequest` из пайплайна.

**Выход:** `LlmResponse` с текстом, `FinishReason`, usage и `CachedTokens`, если шлюз их отдал.

## В скоупе

- `ILlmProvider` как в [architecture.md](../architecture.md) — без резолвера по имени вендора
- один HttpClient `llm`, `BaseAddress` из `Llm:BaseUrl` после мержа Local
- POST `{BaseUrl}/chat/completions`, `Authorization: Bearer` из env
- `CacheMode` из Local: `none` или `openrouter` (`cache_control` на префиксе)
- `max_tokens`, `temperature`, `model`
- таймаут HttpClient из Options
- HTTP 401/403 — «ключ / шлюз», 429 — ретраимый
- unit-тесты сериализации (мок `HttpMessageHandler`): при `openrouter` есть `cache_control`, при `none` — нет; prefix в system, variable в user
- `--model` перекрывает Local на запуск

Форма файла — [local-config.md](../local-config.md).

## Вне скоупа

- три захардкоженных вендора в git
- Anthropic Messages API напрямую
- streaming, Responses API, embeddings
- ключ в JSON
- подбор модели «кто лучше переводит»

## Контракты

Нет `ILlmProviderResolver`. Пайплайн получает `ILlmProvider` из DI.

Нет `BaseUrl` / `Model` в committed `appsettings.json`.

## Критерии приёмки

- нет `new HttpClient`
- пустой ключ или нет Local/`BaseUrl` — ошибка до запроса, с отсылкой к example
- тесты не ходят в сеть и не читают настоящий Local с машины агента как фикстуру (подкладывать тестовый JSON)
- `CachedTokens` парсится из usage, если поле есть, иначе null
- смена BaseUrl в тестовом Options меняет адрес клиента, не ветвит `BookTranslationService`

## Риски

- Закоммитить заполненный Local — не должен пройти `.gitignore`
- Склеить prefix с главой в одном user — ломает кеш
- Логировать Bearer — запрещено

## Зависимости

Эпоха 03 уже шлёт `LlmRequest`. Эта эпоха подключает живой клиент к Local+env, не меняя пайплайн.
