# Эпоха 00 — Foundations

## Цель

Поднять пустой репозиторий до собираемого CLI: три проекта, Host, Options, каркас команд, отказ PDF, русский как единственный target, конфиг трёх провайдеров. Без реальной логики словаря, EPUB и перевода.

## Вход / выход

**Вход:** пустой git-репозиторий (кроме `docs/`).

**Выход:**

- `Ai.Translator.sln`
- `src/Ai.Translator.Cli`
- `src/Ai.Translator.Core`
- `tests/Ai.Translator.Tests`
- `appsettings.json` копируется в output Cli
- `.gitignore` для .NET (bin/obj, user secrets, `.env`)
- `dotnet build` и `dotnet test` зелёные
- `dotnet run --project src/Ai.Translator.Cli -- --help` показывает дерево команд

## В скоупе

- SDK .NET 10, TFM `net10.0`
- пакеты Host / Options / Http / System.CommandLine в Cli; Core пока может ссылаться на Options и Abstractions
- `TranslatorOptions`: `TargetLanguage = "ru"`, `MaxRetries`, `Temperature`
- `LlmOptions`: default provider/model, словарь провайдеров `openai` / `openrouter` / `provod` с BaseUrl и именем env-переменной ключа
- именованные HttpClient зарегистрированы, даже если ещё никто не вызывает LLM
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
    [--provider <name>] [--model <id>] [--work-dir <path>] [--resume]
```

Интерфейсы в Core можно объявить пустыми или не объявлять до своих эпох. Если объявляете сразу — имена из [architecture.md](../architecture.md):

- `ILlmProvider`
- `ILlmProviderResolver`
- `IEpubBookService`
- `IGlossaryCompiler`
- `ICheckpointStore`

Регистрация: `AddTranslator(IServiceCollection, IConfiguration)` в Core.

Секреты: только env, имена из [stack.md](../stack.md).

## Критерии приёмки

- solution собирается на чистой машине с .NET 10 SDK
- тесты проходят
- `--help` перечисляет `glossary` и `translate`
- вызов `translate --input book.pdf ...` не идёт в сеть и завершается ошибкой про EPUB
- в репозитории нет ключей и `.env` с секретами

## Риски

- Слишком толстый скелет «на все эпохи» — не писать чанкер и клиент заранее.
- Keyed DI «чтобы было три клиента» — запрещено, только имена HttpClient + будущий resolver.

## Зависимости

Нет. Первая эпоха.
