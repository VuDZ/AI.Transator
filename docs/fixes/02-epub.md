# Правки после эпохи 02

Статус: **пункты 1–2 закрыты** (проверено). Пункт 3 — наблюдение на живых книгах, заранее не чинить.

## Закрыто

1. **Пустые `BlockFragments` при живом тексте.** Если в `body` нет `p`/`h*`/`blockquote`/`li`, но InnerHtml не пустой (`div`, голый текст), `GetBlockFragments` кладёт `BodyInnerHtml` одним фрагментом. Чанкер не получит пустой список при непустой главе.
2. **`WriteCopyAsync` async I/O.** Копия файла — `CopyToAsync`; entry — `ReadToEndAsync` / `WriteAsync`. Без `Task.Run` и без синхронного `File.Copy`/`ReadToEnd`.

## Наблюдение

3. **HAP `OptionOutputAsXml` + `OuterHtml`.** Replace по-прежнему пересобирает XHTML через HAP. На фикстуре VersOne открывает копию. Если после первой реальной книги разъедется вёрстка — править этот writer, не плодить второй.

## Не дефект 02

- PDF по-прежнему на CLI, не в `EpubBookService`.
- `translate` не реализован — это 03.
