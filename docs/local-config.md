# Локальный доступ к модели

Один OpenAI-compatible шлюз (Provod.ai, OpenRouter, свой прокси). URL и имя модели — только на этой машине. Ключ — только в env. В git этого нет.

## Файлы

| Файл | Git | Назначение |
| --- | --- | --- |
| `src/Ai.Translator.Cli/appsettings.json` | да | язык, ретраи, температура. Без URL, модели и ключа |
| `src/Ai.Translator.Cli/appsettings.Local.json.example` | да | пустой образец формы |
| `src/Ai.Translator.Cli/appsettings.Local.json` | нет | твой BaseUrl и Model |

`.gitignore` содержит `appsettings.Local.json` и `**/appsettings.Local.json`.

Образец (значения выдуманные, не канон провайдера):

```json
{
  "Llm": {
    "BaseUrl": "https://api.provod.ai/v1",
    "Model": "openai/gpt-5.4",
    "ApiKeyEnvironmentVariable": "TRANSLATOR_API_KEY",
    "ContextWindowTokens": 128000,
    "ReservedOutputTokens": 8000,
    "CacheMode": "none"
  }
}
```

`CacheMode`: `none` (Provod и прочие без кеша) или `openrouter` (явный `cache_control` на префиксе). Для обычного OpenAI достаточно `none`: одинаковый system-префикс кешируется шлюзом сам.

Ключ в JSON не класть. Значение берётся из env с именем `ApiKeyEnvironmentVariable` (по умолчанию `TRANSLATOR_API_KEY`).

## Как подхватывается при сборке и запуске

Host читает, в таком порядке (последний побеждает):

1. `appsettings.json`
2. `appsettings.Local.json` (`optional: true`)
3. переменные окружения

`appsettings.Local.json` лежит рядом с csproj **и** копируется в output (`PreserveNewest`). `dotnet run` берёт файл из проекта (ContentRoot). Запуск exe из `bin/` берёт копию из output. Пересобрал — снова актуальный Local, если менял файл в проекте.

Нет файла, пустой `BaseUrl` или пустой ключ в env — ошибка до HTTP, с текстом что создать Local из example и выставить переменную.

`--model` в CLI перекрывает `Llm:Model` на один запуск. Флага `--provider` нет.

## HttpClient

Один именованный клиент `llm`. `BaseAddress` и таймаут из Options после мержа Local. Не три клиента и не keyed DI.
