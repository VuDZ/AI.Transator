# Правки после эпохи 01

Статус: **закрыто** (оба пункта проверены).

1. Парсер читает рабочий MD без `##`. Compile корпуса без записей — ошибка.
2. `IBookTextExtractor` поглощён: compile идёт через `IEpubBookService`, plain text — `XhtmlToPlainText`.
