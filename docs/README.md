# Документация CLI-переводчика

Литературный перевод книг через LLM: EPUB на английском → EPUB на русском, со словарём вселенной и кешируемым префиксом промпта.

Как запускать CLI и какие флаги есть — корневой [README.md](../README.md). Этот каталог — **архитектурный контракт** для реализации в других чатах. Концепция продукта зафиксирована отдельно и здесь не пересказывается заново.

Правила работы агентов: [AGENTS.md](../AGENTS.md) и `.cursor/rules/`.

## Правило надзора

Любое отклонение от этих файлов сначала правится здесь, потом в коде.

- Не расширять скоуп эпохи «заодно».
- Не менять формат словаря без правки [glossary-format.md](glossary-format.md).
- Не добавлять проекты, пакеты или провайдеры без правки [architecture.md](architecture.md) и [stack.md](stack.md).
- Одна эпоха — один чат реализации, если возможно.
- В промпте реализации указывать конкретный `docs/epochs/0X-*.md` и [architecture.md](architecture.md).
- После реализации эпохи надзор сверяет код с epoch-файлом. Замечания — только в `docs/fixes/0X-*.md`, не чинить «заодно» в том же чате, если это не секрет в git.

## Как читать

1. [stack.md](stack.md) — почему .NET 10, где Python, какие пакеты.
2. [architecture.md](architecture.md) — проекты, слои, потоки, DI, границы MVP.
3. [local-config.md](local-config.md) — BaseUrl, модель и ключ локально.
4. [glossary-format.md](glossary-format.md) — канон Markdown-словаря.
5. [`prompts/translate-system.md`](../prompts/translate-system.md) — общие правила перевода (слой 1 префикса).
6. [extract-pairs.md](extract-pairs.md) — mapping глав для extract `--pairs`.
7. Эпохи по порядку: следующая не начинается, пока предыдущая не закрыта по критериям приёмки.
8. [backlog.md](backlog.md) — сознательно отложенное. Это не скоуп v1.

## Эпохи

| Файл | Суть |
| --- | --- |
| [epochs/00-foundations.md](epochs/00-foundations.md) | Репозиторий, три проекта, CLI-скелет, конфиг, отказ PDF |
| [epochs/01-glossary.md](epochs/01-glossary.md) | Парсер/писатель MD, компилятор рабочего словаря, матчер. Без LLM |
| [epochs/02-epub.md](epochs/02-epub.md) | Чтение spine, замена XHTML в копии ZIP, сохранение структуры |
| [epochs/03-translation.md](epochs/03-translation.md) | Префикс, чанки, ретраи, чекпоинты, кандидаты терминов |
| [epochs/04-providers.md](epochs/04-providers.md) | OpenAI / OpenRouter / Provod.ai за одним контрактом |
| [epochs/05-extract.md](epochs/05-extract.md) | Наполнение корпуса из пары оригинал+перевод |
| [epochs/06-extract-list-pairs.md](epochs/06-extract-list-pairs.md) | Печать пар глав extract без LLM (spine + превью ролей) |
| [epochs/07-extract-pairs.md](epochs/07-extract-pairs.md) | extract --pairs: mapping-файл вместо индекса spine |
| [epochs/08-send-temperature.md](epochs/08-send-temperature.md) | temperature в Chat Completions только при Llm:SendTemperature |
| [epochs/09-cli-progress.md](epochs/09-cli-progress.md) | тихие HTTP-логи, прогресс Spectre, ETA и usage в консоли |
| [epochs/10-extract-resume.md](epochs/10-extract-resume.md) | extract: ретраи 429/5xx, `--out` после шага, `--resume`, тело ошибки шлюза |
| [epochs/11-live-progress.md](epochs/11-live-progress.md) | один живой бар+таблица usage на stderr, без ленты на каждую главу |
| [epochs/12-protocol-english.md](epochs/12-protocol-english.md) | валидатор: протокол не считать недопереводом, в Reason — слово |
| [epochs/13-translate-concurrency.md](epochs/13-translate-concurrency.md) | translate: несколько чанков в модель сразу, `--concurrency` |

Эпоха [14 — Большие запросы OpenRouter](epochs/14-openrouter-large-requests.md): отдельный предел входа, расширение русского ответа, SSE, reasoning и TTL кеша.

Эпоха [15 — Проверка английского и полезные повторы](epochs/15-translation-validation.md): одиночное слово — предупреждение, английская фраза — ошибка с контекстом, feedback при повторе.

## Шаблон эпохи

Каждый epoch-файл отвечает на одно и то же:

- цель
- вход / выход
- в скоупе / вне скоупа
- контракты (команды, файлы, имена интерфейсов — без реализации)
- критерии приёмки
- риски (кеш, канон, EPUB)
- зависимость от предыдущей эпохи

## Два режима

1. **Наполнение словаря** — модель экстрактор/редактор. Человек в цикле. Результат: полный MD-корпус вселенной.
2. **Перевод** — модель подчиняется рабочему словарю книги. Префикс стабилен на всю книгу. Новые термины не подмешиваются в текущий прогон.

Рабочий словарь книги = пересечение полного корпуса с текстом оригинала. Его нельзя резать заново на каждый абзац: это убивает prompt cache.
