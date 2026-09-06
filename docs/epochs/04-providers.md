# Эпоха 04 — Providers

## Цель

Три бэкенда за одним портом: OpenAI, OpenRouter, Provod.ai. Пайплайн по-прежнему отдаёт `StablePrefix` + `VariableContent`. Нарезка смотрит на окно выбранной модели. Ключи не в логике перевода.

## Вход / выход

**Вход:** `LlmRequest` из пайплайна.

**Выход:** `LlmResponse` с текстом, `FinishReason`, usage и `CachedTokens`, если провайдер их отдал.

## В скоупе

- `ILlmProvider` / `ILlmProviderResolver` как в [architecture.md](../architecture.md)
- один класс OpenAI-compatible клиента (или тонкие обёртки над ним), три именованных HttpClient
- POST `{BaseUrl}/chat/completions`, `Authorization: Bearer`
- стратегии кеша:
  - OpenAI: system = prefix (строка), user = variable
  - OpenRouter: system.content = массив `[{ type: text, text: prefix, cache_control: { type: ephemeral } }]`
  - Provod.ai: как OpenAI, без cache_control
- `max_tokens` (совместимое поле), `temperature`, `model`
- таймаут HttpClient из Options
- маппинг HTTP ошибок: 401/403 понятным текстом «ключ / провайдер», 429 — ретраимый
- `LlmOptions.Providers` и профили моделей: `ContextWindowTokens`, `ReservedOutputTokens`
- если модель не найдена в профилях — дефолтный профиль из Options, не магическое число в коде чанкера
- unit-тесты сериализации тела запроса (мок `HttpMessageHandler`): OpenRouter содержит `cache_control`, OpenAI и Provod — нет; prefix не склеен с variable в одной роли без необходимости (system vs user)
- Cli `--provider` / `--model` доходят до резолвера

Имена провайдеров в CLI и конфиге: `openai`, `openrouter`, `provod`.

## Вне скоупа

- Anthropic Messages API напрямую (только через OpenRouter chat completions + cache_control)
- streaming
- Responses API OpenAI
- embeddings
- учёт денег / биллинг-дашборд
- Python SDK
- подбор модели «кто лучше переводит»

## Контракты

```
ILlmProviderResolver.Resolve(string providerName) -> ILlmProvider
```

Неизвестный provider → ошибка до первого HTTP.

Профили в `appsettings.json` (форма ориентир, ключи можно уточнить, но не размазывать по коду):

```json
{
  "Llm": {
    "DefaultProvider": "openrouter",
    "DefaultModel": "anthropic/claude-sonnet-4",
    "Providers": {
      "openai": {
        "BaseUrl": "https://api.openai.com/v1",
        "ApiKeyEnvironmentVariable": "OPENAI_API_KEY"
      },
      "openrouter": {
        "BaseUrl": "https://openrouter.ai/api/v1",
        "ApiKeyEnvironmentVariable": "OPENROUTER_API_KEY"
      },
      "provod": {
        "BaseUrl": "https://api.provod.ai/v1",
        "ApiKeyEnvironmentVariable": "PROVOD_API_KEY"
      }
    },
    "Models": {
      "anthropic/claude-sonnet-4": { "ContextWindowTokens": 200000, "ReservedOutputTokens": 8000 },
      "default": { "ContextWindowTokens": 128000, "ReservedOutputTokens": 8000 }
    }
  }
}
```

Опциональные заголовки OpenRouter (`HTTP-Referer`, `X-Title`) — в Options, пустые по умолчанию.

## Критерии приёмки

- смена `--provider provod` меняет BaseAddress/клиент, не ветвит `BookTranslationService`
- нет `new HttpClient` в решении
- ключ читается из env-имени из Options; пустой ключ — ошибка до запроса
- тесты запроса не ходят в сеть
- `CachedTokens` парсится из usage, если поле есть (`prompt_tokens_details.cached_tokens` или аналог OpenRouter), иначе null

## Риски

- Разные поля usage у шлюзов — парсер терпимый, пайплайн не падает.
- Складывать prefix в user вместе с главой «для простоты» — сломает кеш OpenAI (префикс должен быть общим началом).
- Keyed services вместо резолвера — отказ по [architecture.md](../architecture.md).
- Логировать Bearer — запрещено.

## Зависимости

Эпоха 03 уже шлёт `LlmRequest`. Если 03 закрыта на одном адаптере, эта эпоха заменяет его резолвером и тремя стратегиями, не меняя пайплайн.
