# Эпоха 08 — Optional temperature

## Цель

Не слать `temperature` в Chat Completions, пока в Local нет `Llm:SendTemperature: true`. На Provod + GPT-5 (`openai/gpt-5.6-terra`) `temperature: 0.3` даёт 503 «No provider is currently available for this request combination» (запрос висит минуты, потом отказ). Без поля — 200 за пару секунд.

`Translator:Temperature` остаётся. Клиент решает, класть ли его в JSON.

## Вход / выход

**Вход:** тот же `LlmRequest` (в том числе `Temperature`). `Llm:SendTemperature` из Options / Local.

**Выход:** тот же POST `chat/completions`. Дефолт — тело без свойства `temperature`. При флаге `true` — `temperature` равен `LlmRequest.Temperature`.

## В скоупе

- `LlmOptions.SendTemperature` (`bool`, дефолт `false`)
- `BuildRequestJson`: не добавлять `temperature`, если флаг выключен
- `max_tokens`, `stream: false`, system = prefix, user = variable — как в 04
- example Local: `"SendTemperature": false`
- unit-тесты сериализации (мок handler): без флага свойства нет; с флагом — значение из request
- форма — [local-config.md](../local-config.md)

## Вне скоупа

- угадывать модель (`gpt-5` и т.п.) и ветвить провайдер
- второй `ILlmProvider` / keyed DI
- `max_completion_tokens` вместо `max_tokens` (зонд: оба принимаются)
- ретраи extract на 5xx
- учёт стоимости / usage-файл (backlog)
- живой extract в чате реализации
- менять дефолт `Translator:Temperature` в `appsettings.json`

## Контракты

```
Llm:SendTemperature  // bool, default false; только Local / example, не committed appsettings.json
```

Пайплайн (translate / extract) по-прежнему пишет `LlmRequest.Temperature`. Сериализация — только в `ChatCompletionsLlmProvider`.

Не коммитить `appsettings.Local.json`.

## Критерии приёмки

- дефолт Options / `SendTemperature: false` — в JSON нет `temperature`; `max_tokens` и `stream: false` на месте
- `SendTemperature: true` — `temperature` равен числу из `LlmRequest`
- тесты 04 на `cache_control` / prefix в system зелёные
- тесты не ходят в сеть и не читают боевой Local

## Риски

- Оставить безусловный `temperature` — extract снова 503 на Provod.
- Склеить флаг с именем модели — сломается на следующем id.
- Закоммитить ключ в Local.

## Зависимости

Эпоха 04. Доказательство зонда: `tools/probe-chat-completions.ps1` (не обязателен в CI).
