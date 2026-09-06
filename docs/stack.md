# Стек

## Решение

**Пайплайн и CLI — .NET 10.**  
**Python — не в MVP.** Появляется только в backlog как скрипты набивки полного корпуса с вики/форумов. Двухязычного рантайма в CLI нет.

Это конвейер (EPUB, словарь, стабильный префикс, ретраи, чекпоинты, сменные провайдеры), а не ML-прототип. Сопровождение — C# с уже зафиксированными правилами: DI, `IOptions<T>`, `IHttpClientFactory`, async/await, xUnit + Moq.

## Почему не Python на пайплайн

Python быстрее для промптов и парсинга вики. Здесь узкое место другое: повторяемый конвейер, тестовые контракты, HttpClient и Options, долгоживущий CLI.

Вики/форумы — грязный одноразовый ingest. Их место в `tools/glossary-ingest` (Python, позже), выход — Markdown, который ест .NET.

## Целевая платформа

- SDK: .NET 10
- TFM: `net10.0`
- Nullable включён, implicit usings включены
- ОС разработки: Windows, но пути и ZIP не должны зависеть от `\` в именах EPUB-entry

## Пакеты v1

Только это, плюс транзитивные зависимости SDK.

| Пакет | Где | Зачем |
| --- | --- | --- |
| `System.CommandLine` | Cli | Команды и help |
| `Microsoft.Extensions.Hosting` | Cli | Generic Host, DI, конфиг, логи |
| `Microsoft.Extensions.Options` | Core / Cli | `IOptions<T>` |
| `Microsoft.Extensions.Http` | Core / Cli | `IHttpClientFactory` |
| `VersOne.Epub` | Core | Чтение EPUB (spine, XHTML, метаданные) |
| `HtmlAgilityPack` | Core | Блоки, курсив, plain text для матчера |
| `xunit` | Tests | Юнит-тесты |
| `Moq` | Tests | Моки провайдера и файловых портов |
| `Microsoft.NET.Test.Sdk` | Tests | Хост тестов |
| `coverlet.collector` | Tests | Оставляем шаблон xunit как есть |

Конфиг читается стандартными провайдерами Host: `appsettings.json`, переменные окружения, аргументы командной строки.

## Что сознательно не берём

- PDF-библиотеки (`PdfPig`, iText и т.п.) — PDF вне v1
- `EpubSharp` / коммерческие редакторы EPUB — не пересобираем книгу с нуля
- Официальный OpenAI SDK как единственный клиент — все три бэкенда OpenAI-compatible, один свой клиент на `HttpClient`
- Polly как обязательная зависимость — ретраи сначала простым циклом + `TimeProvider`
- Keyed services в DI
- База данных, очередь, веб-хост
- Python runtime внутри CLI

## Переменные окружения для ключей

Имена фиксируем сразу, чтобы эпохи 00 и 04 не разъехались:

| Провайдер | Переменная | Base URL по умолчанию |
| --- | --- | --- |
| OpenAI | `OPENAI_API_KEY` | `https://api.openai.com/v1` |
| OpenRouter | `OPENROUTER_API_KEY` | `https://openrouter.ai/api/v1` |
| Provod.ai | `PROVOD_API_KEY` | `https://api.provod.ai/v1` |

Ключ не хранится в репозитории и не логируется. В `appsettings.json` — только URL, имя переменной и профили моделей.

## Язык и вход

- Целевой язык перевода v1: русский (`ru`). Других target language в опциях не плодим.
- Вход v1: только `.epub` (регистр расширения неважен).
- `.pdf` — отказ до разбора файла, сообщение: сначала сконвертируй в EPUB.
