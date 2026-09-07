# Правки после эпохи 08

Статус: **принята**. 141 тест зелёный. `temperature` в Chat Completions только при `Llm:SendTemperature: true`.

Сошлось с контрактом:

- `LlmOptions.SendTemperature` дефолт `false`; committed `appsettings.json` флаг не содержит
- без флага — в JSON нет `temperature`; `max_tokens` и `stream: false` на месте; prefix в system
- `SendTemperature: true` — `temperature` из `LlmRequest`
- `CacheMode: openrouter` по-прежнему кладёт `cache_control`; модель по имени не ветвится
- пайплайн по-прежнему пишет `LlmRequest.Temperature`; сериализация только в клиенте

## Не дефект 08

- Живой extract — не unit. После этой приёмки можно гнать `--pairs` + `--max-pairs 3` без `SendTemperature`.
- Кеш GPT-5.6 / `cache_write_tokens` — не этот скоуп.
