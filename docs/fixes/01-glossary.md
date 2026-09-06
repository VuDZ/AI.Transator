# Правки после эпохи 01

Статус: **пункт 1 закрыт** (проверено). Пункт 2 — в эпохе 02.

## Закрыто

1. Парсер читает рабочий MD без `##` (заголовок + преамбула), write→parse пустой. Compile корпуса без записей по-прежнему `GlossaryFormatException` в `GlossaryCompileService`.

## До эпохи 02

2. **Поглотить `IBookTextExtractor`.** Не заводить второй plain text. `IEpubBookService` переиспользует VersOne + `XhtmlToPlainText`.
