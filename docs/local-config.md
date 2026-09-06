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
    "CacheMode": "none"
  }
}
```

`CacheMode`: `none` или `openrouter` (явный `cache_control` на префиксе).

`TimeoutSeconds` — лимит одного нестримингового запроса (дефолт 300). Дефолт `HttpClient` 100 с обрывает длинную главу. 504 — ретраимый, не повод включать streaming.

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
