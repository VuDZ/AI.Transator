# Эпоха 00 — Foundations

## Цель

Поднять пустой репозиторий до собираемого CLI: три проекта, Host, Options, каркас команд, отказ PDF, русский как единственный target, локальный LLM-конфиг. Без реальной логики словаря, EPUB и перевода.

## Вход / выход

**Вход:** пустой git-репозиторий (кроме `docs/`).

**Выход:**

- `Ai.Translator.sln`
- `src/Ai.Translator.Cli`
- `src/Ai.Translator.Core`
- `tests/Ai.Translator.Tests`
- `appsettings.json` копируется в output Cli (без URL/модели/ключа)
- `appsettings.Local.json.example` в git; `appsettings.Local.json` в `.gitignore` и CopyToOutputDirectory
- `.gitignore` для .NET (bin/obj, Local.json, `.env`)
- `dotnet build` и `dotnet test` зелёные
- `dotnet run --project src/Ai.Translator.Cli -- --help` показывает дерево команд

## В скоупе

- SDK .NET 10, TFM `net10.0`
- пакеты Host / Options / Http / System.CommandLine в Cli; Core пока может ссылаться на Options и Abstractions
- `TranslatorOptions`: `TargetLanguage = "ru"`, `MaxRetries`, `Temperature`
- `LlmOptions` как в [local-config.md](../local-config.md): BaseUrl, Model, ApiKey из Local
- Host: `AddJsonFile("appsettings.Local.json", optional: true)`
- один именованный HttpClient `llm`, даже если ещё никто не вызывает LLM
- корневая команда `ai-translator` (AssemblyName)
- подкоманды объявлены: `glossary compile`, `glossary extract`, `translate`
- `glossary compile` и `translate` проверяют расширение входа: PDF → exit code ≠ 0 и текст про конвертацию в EPUB
- один smoke-тест, что DI контейнер строится (или что Options биндятся из тестового JSON)

Команды могут возвращать «not implemented» (кроме проверки PDF), это нормально для эпохи 00.

## Вне скоупа

- парсер словаря
- чтение EPUB
- вызовы LLM
- Python, вики
- README продукта с туториалом (достаточно `--help` и `docs/`)
- четвёртый проект

## Контракты

Команды (имена и флаги — стабильны):

```
ai-translator glossary compile --corpus <path> --book <path> --out <path>
ai-translator glossary extract --original <path> --translation <path> --out <path>
ai-translator translate --input <path> --glossary <path> --out <path>
    [--model <id>] [--work-dir <path>] [--resume]
```

Интерфейсы в Core можно объявить пустыми или не объявлять до своих эпох. Если объявляете сразу — имена из [architecture.md](../architecture.md):

- `ILlmProvider`
- `IEpubBookService`
- `IGlossaryCompiler`
- `ICheckpointStore`

Регистрация: `AddTranslator(IServiceCollection, IConfiguration)` в Core.

Секреты: `ApiKey` только в Local.json, см. [local-config.md](../local-config.md).

## Критерии приёмки

- solution собирается на чистой машине с .NET 10 SDK
- тесты проходят
- `--help` перечисляет `glossary` и `translate`
- вызов `translate --input book.pdf ...` не идёт в сеть и завершается ошибкой про EPUB
- в репозитории нет ключей и `.env` с секретами

## Риски

- Слишком толстый скелет «на все эпохи» — не писать чанкер и клиент заранее.
- Keyed DI и три HttpClient «на вырост» — запрещено. Один клиент `llm`.

## Зависимости

Нет. Первая эпоха.
