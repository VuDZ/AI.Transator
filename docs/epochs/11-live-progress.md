# Эпоха 11 — Live progress на stderr

## Цель

Долгий extract / translate не растёт лентой баров и таблиц. Одна живая область: бар + таблица last/total обновляются на месте.

Сейчас `SpectreRunProgress` на каждый шаг печатает новую строку `… label`, новую строку бара и новую таблицу (`MarkupLine` / `Write`). На 28 парах — 28 баров и 28 таблиц.

## Вход / выход

**Вход:** те же команды и `IRunProgress`. Пайплайн, чекпоинты, префикс и провайдер не менять.

**Выход:** stderr. TTY — один блок (бар, текущий label, ETA, таблица last + total), который перерисовывается. stdout `--list-pairs` не трогать. `compile` / `--list-pairs` — без бара, как в 09.

## В скоупе

- Править только Cli-рендер (`SpectreRunProgress`). `IRunProgress` и Core **не** расширять, если живой блок собирается из тех же `Begin` / `StepBegin` / `StepEnd` / `Complete`.
- TTY: `Spectre.Console` Live (или эквивалент) — один renderable: строка бара (`done/total`, label, ETA) + та же таблица last/total, что сейчас. `StepBegin` меняет подпись текущего шага, не дописывает новую ленту `… path`.
- `Complete`: не печатать третью таблицу total под живым блоком. Остановить live; на экране остаётся последнее состояние (100% / итог).
- Non-TTY (CI, редирект stderr): живой redraw не обязателен. Достаточно короткой строки на шаг **без** повторной полной таблицы; итоговую таблицу — в `Complete`. Не ломать тесты, которые ловят `IRunProgress`, не Spectre.
- Поля таблицы те же: elapsed, prompt, cached, completion, steps, failed. ETA — формула эпохи 09.
- Spectre только в Cli. Сеть не звать. Новый пакет не добавлять.

## Вне скоупа

- секундный тик бара во время висящего HTTP (09: границы шагов)
- SSE / streaming
- файл стоимости / прайс в рублях
- Spectre в Core или Tests
- менять ретраи, чекпоинты, `--pairs`, префикс
- TUI/окно сверх бар+таблица

## Контракты

```
IRunProgress          // без новых методов
SpectreRunProgress    // живой блок на IAnsiConsole (stderr)
```

Команды и флаги не переименовывать.

## Критерии приёмки

- два шага на TTY-подобном консоле: в выводе не два полных копии таблицы last/total подряд; один живой блок или перезапись ANSI, не лента `MarkupLine`
- `StepBegin` не дублирует историю `… path` на каждую главу; текущий label виден в баре
- Core-тесты `IRunProgress` / extract resume / translate зелёные; контракт порта тот же
- нет Spectre в csproj Core; нет `new HttpClient`
- unit бара Spectre не обязателен (как в 09); если есть `IAnsiConsole` test console — можно проверить, что после двух `StepEnd` таблица в дампе не повторяется дважды как самостоятельные блоки

## Риски

- `Live` на не-TTY ломает CI — ветка по `Profile.Capabilities.Interactive` / не-interactive fallback.
- `Live` + обычный `Write` ошибки LLM под живым блоком смешает экран: перед печатью `LlmException` остановить live (`Complete` или dispose).
- Тянуть Spectre в Core ради теста — запрещено.

## Зависимости

Эпоха 09 (порт и таблица usage). 10 не трогать.
