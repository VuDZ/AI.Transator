# Правки после эпохи 00

Статус: **принята с замечаниями**. Сборка и 6 тестов зелёные. `--help` показывает `glossary` / `translate`. PDF на `--input` даёт exit 1 и текст про конвертацию в EPUB. `appsettings.Local.json` в git не попал.

Исправляет чат эпохи 00 или 01 до новой логики. Не расширять скоуп.

## До эпохи 01

1. **Host не доходит до команд.** `Program` строит и сразу `Dispose` у `IHost`; `CommandTree` сервисы не видит. Для заглушек 00 хватает (контейнер хотя бы собирается), но `glossary compile` в 01 уже нужен `IEpubBookService` / компилятор. Держать host до конца `InvokeAsync` и отдавать `IServiceProvider` в дерево команд.

## Документация

2. **ContentRoot.** [local-config.md](../local-config.md) обещает, что `dotnet run` читает Local из папки проекта. Сейчас `ContentRootPath = AppContext.BaseDirectory` и файл подхватывается копией в output (`PreserveNewest`). Так правильнее для exe из `bin/`. Поправить local-config под факт: источник — файл рядом с csproj, чтение — копия в output после сборки.

## Не дефект 00

- Интерфейсы `ILlmProvider` и прочие не объявлены — в 00 разрешено.
- `glossary extract` PDF не проверяет — в 00 только compile и translate.
- Живой `appsettings.Local.json` на диске — так и надо; в git его нет. Ключ в чаты и в `docs/` не копировать.
