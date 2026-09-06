# Правки после эпохи 04

Статус: **принята**. 75 тестов зелёные. `ILlmProvider` — `ChatCompletionsLlmProvider`: POST `chat/completions`, `stream: false`, system = prefix, user = variable. `UnconfiguredLlmProvider` убран.

Сошлось с контрактом:

- один HttpClient `llm`, `BaseAddress` / `Timeout` / Bearer из Options; нет `new HttpClient` в проде
- `TimeoutSeconds` дефолт 300; ≤ 0 и пустые BaseUrl/ApiKey — ошибка до HTTP, отсылка к example
- `CacheMode`: `openrouter` → `cache_control` на system; `none` — нет
- 401/403 не ретраимые; 429/5xx/504 — ретраимые
- `CachedTokens` из `usage.prompt_tokens_details` или плоского `usage.cached_tokens`, иначе null
- смена BaseUrl меняет адрес клиента, не пайплайн
- тесты на моке `HttpMessageHandler`, живой Local не читают

## Не дефект 04

- `glossary extract` всё ещё заглушка (05).
- Живой smoke шлюза в unit нет — так в epoch.
- HAP-rewrite XHTML — наблюдение из 02.
