# AI Translator

Литературный перевод EPUB EN → RU через LLM, со словарём вселенной.

Как запускать и какие флаги есть — этот файл. Архитектура и эпохи — [docs/README.md](docs/README.md).

Требования: .NET SDK 10 (`global.json`), ключ шлюза в локальном конфиге. PDF на входе CLI отвергает.

Из корня репозитория:

```powershell
dotnet run --project src\Ai.Translator.Cli -- <команда>
```

Всё после `--` уходит в CLI.

## Один раз: локальный конфиг

1. Скопировать [src/Ai.Translator.Cli/appsettings.Local.json.example](src/Ai.Translator.Cli/appsettings.Local.json.example) → `src/Ai.Translator.Cli/appsettings.Local.json`.
2. Заполнить `Llm:BaseUrl`, `Llm:Model`, `Llm:ApiKey`.
3. Файл в git не коммитить.

Поля `Llm` (подробности — [docs/local-config.md](docs/local-config.md)):

| Поле | Смысл |
| --- | --- |
| `BaseUrl` | Корень OpenAI-compatible API, например `https://api.provod.ai/v1` |
| `Model` | Id модели в каталоге шлюза |
| `ApiKey` | Ключ |
| `ContextWindowTokens` | Окно модели |
| `ReservedOutputTokens` | Лимит выхода (`max_tokens`) |
| `TimeoutSeconds` | Таймаут одного запроса, дефолт 300 |
| `CacheMode` | `none` или `openrouter` (явный `cache_control`). На Provod обычно `none` |
| `SendTemperature` | `false` (дефолт) — поле `temperature` не слать. На GPT-5/Provod `0.3` даёт 503 |

`--model` на команде перекрывает `Llm:Model` на один запуск. Флага `--provider` нет.

## Что за файлы

| Файл | Кто пишет | Куда дальше |
| --- | --- | --- |
| Корпус вселенной `.md` | ты + `glossary extract` | вычитать глазами, потом `compile` |
| Mapping `.pairs.txt` | ты после `--list-pairs` | `--pairs` у extract |
| Рабочий словарь `.md` | `glossary compile` | `--glossary` у `translate` |
| Выходной `.epub` | `translate` | чтение |

Сырой extract в `translate` не совать. Сначала глаза, потом `compile` на **ту** английскую книгу, которую переводишь.

## По шагам

### 1. Пары глав (без LLM)

Официальный EPUB и фанперевод почти никогда не совпадают по spine. Сначала печать kept-порядка:

```powershell
dotnet run --project src\Ai.Translator.Cli -- glossary extract --original C:\books\en\книга_en.epub --translation C:\books\rus\книга_ru.epub --list-pairs
```

`--list-pairs` нельзя мешать с `--out` / `--merge-into` / `--model` / `--pairs`.

Пишешь mapping: точечные пары и блоки путей из этой печати. Формат — [docs/extract-pairs.md](docs/extract-pairs.md). Пример: [docs/examples/dawn-of-fire-1.pairs.txt](docs/examples/dawn-of-fire-1.pairs.txt).

Без `--pairs` extract парует индекс к индексу и на BL vs фан сдвигает всю книгу.

`--chapters` у extract нет. Короткий прогон — `--max-pairs N` после разворота mapping.

### 2. Наполнить корпус

Первая книга:

```powershell
dotnet run --project src\Ai.Translator.Cli -- glossary extract --original C:\books\en\книга1_en.epub --translation C:\books\rus\книга1_ru.epub --out C:\books\corpus.md --pairs docs\examples\книга1.pairs.txt
```

Коротко (первые N развёрнутых пар):

```powershell
... --pairs docs\examples\книга1.pairs.txt --max-pairs 3
```

Вторая книга — в уже **вычитанный** корпус:

```powershell
dotnet run --project src\Ai.Translator.Cli -- glossary extract --original C:\books\en\книга2_en.epub --translation C:\books\rus\книга2_ru.epub --out C:\books\corpus.md --merge-into C:\books\corpus.md --pairs путь\книга2.pairs.txt
```

`--out` = `--merge-into` — допись in-place. Старый `ru` не затирается. Новые статьи в конец.

Снова вычитать MD. Автослияние двух сырых extract — плохая идея.

### 3. Рабочий словарь на книгу перевода

```powershell
dotnet run --project src\Ai.Translator.Cli -- glossary compile --corpus C:\books\corpus.md --book C:\books\en\книга_которую_переводишь.epub --out C:\books\working-книга.md
```

`--book` — английский EPUB. В `--out` только статьи, чей English или alias есть в тексте этой книги.

Другая книга на перевод — тот же `--corpus`, другой `--book` и другой `--out`.

### 4. Перевод

Вся книга:

```powershell
dotnet run --project src\Ai.Translator.Cli -- translate --input C:\books\en\книга.epub --glossary C:\books\working-книга.md --out C:\books\out-книга.epub
```

Одна–две главы (проверка):

```powershell
... --chapters 6-7
```

`--chapters` — **1-based kept reading order**: spine, пустой XHTML (обложка/карта без текста) уже выкинут. Это не `playOrder` из TOC. На Dawn of Fire 9 (*The Silent King*) Chapter One = `6`, Chapter Two = `7`.

Выход всегда полный EPUB: выбранные главы на русском, остальные — копия оригинала.

Чекпоинты:

```powershell
... --work-dir C:\books\work-книга --resume
```

`--resume` с другим `--chapters` или другим хешем префикса (после правки `prompts/translate-system.md` или working MD) — отказ. Нужен новый `--work-dir` или прогон с нуля.

## Команды и опции

### `glossary compile`

Рабочий словарь книги из полного корпуса. Без LLM.

| Опция | Обяз. | Смысл |
| --- | --- | --- |
| `--corpus <md>` | да | полный корпус вселенной |
| `--book <epub>` | да | английский EPUB, по нему фильтр |
| `--out <md>` | да | рабочий MD |

### `glossary extract --list-pairs`

Печать kept-пар и превью ролей в stdout. Без LLM.

| Опция | Обяз. | Смысл |
| --- | --- | --- |
| `--original <epub>` | да | оригинал |
| `--translation <epub>` | да | существующий перевод |
| `--list-pairs` | да | режим печати |

Нельзя вместе с `--out`, `--merge-into`, `--model`, `--pairs`.

### `glossary extract` (наполнение)

Модель предлагает `##` записи. Человек ревьюит.

| Опция | Обяз. | Смысл |
| --- | --- | --- |
| `--original <epub>` | да | оригинал |
| `--translation <epub>` | да | литературный перевод того же произведения |
| `--out <md>` | да* | куда писать MD (*не нужен только с `--list-pairs`) |
| `--pairs <file>` | нет | mapping путей; без флага — индекс к индексу |
| `--max-pairs N` | нет | первые N развёрнутых пар; только вместе с `--pairs`; N > 0 |
| `--merge-into <md>` | нет | слить в существующий корпус; `ru` священен |
| `--model <id>` | нет | перекрыть `Llm:Model` |

`--list-pairs` и `--pairs` вместе — ошибка. `--max-pairs` без `--pairs` — ошибка.

### `translate`

Перевод EPUB. Префикс = [prompts/translate-system.md](prompts/translate-system.md) + рабочий словарь. Стабилен на книгу.

| Опция | Обяз. | Смысл |
| --- | --- | --- |
| `--input <epub>` | да | английский EPUB |
| `--glossary <md>` | да | **рабочий** словарь после `compile` |
| `--out <epub>` | да | выходной EPUB |
| `--model <id>` | нет | перекрыть `Llm:Model` |
| `--chapters n` или `from-to` | нет | kept-индекс, включительно; без флага — вся книга |
| `--work-dir <path>` | нет | каталог чекпоинтов |
| `--resume` | нет | добить незакрытые чанки из `--work-dir` |

Ненулевой exit, если остались дырки, даже если частичный EPUB записан.

## Частые поломки

- **503 на GPT-5/Provod** — в Local не ставить `SendTemperature: true`.
- **Extract сдвинул роман** — не гоняй без `--pairs`.
- **`--chapters 7` взял не ту главу** — это не TOC `playOrder`. Считай kept-файлы с текстом.
- **Resume не встаёт** — сменился префикс (промпт или working MD) или диапазон глав.
- **Кеш extract не хитит, translate хитит** — у extract user огромный (EN+RU); у translate system толстый и тот же.

## Документация

| Файл | О чём |
| --- | --- |
| [docs/README.md](docs/README.md) | оглавление контракта |
| [docs/local-config.md](docs/local-config.md) | Local.json |
| [docs/glossary-format.md](docs/glossary-format.md) | канон MD-словаря |
| [docs/extract-pairs.md](docs/extract-pairs.md) | синтаксис `--pairs` |
| [docs/architecture.md](docs/architecture.md) | слои и CLI-поверхность |
| [AGENTS.md](AGENTS.md) | правила агентов |
