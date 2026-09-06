# Правки после эпохи 02

Статус: **принята с замечаниями**. 30 тестов зелёные. `IEpubBookService` читает spine и пишет копию ZIP; CSS/картинки/`mimetype` на месте; `<em>`/`<i>` в `BodyInnerHtml` и блоках; пустые spine-элементы пропускаются; compile идёт через этот порт; `IBookTextExtractor` снят.

## До эпохи 03

1. **Пустые `BlockFragments` при живом тексте.** Глава из `<div>…</div>` или голого текста в `body` попадает в `Chapters` (plain text не пустой), а список блоков пуст (`p`/`h*`/`blockquote`/`li`). Чанкер не должен молча проглатывать такую главу: fallback на `BodyInnerHtml` или явная ошибка.

2. **`WriteCopyAsync` синхронный внутри.** `File.Copy`, ZIP и `ReadToEnd` блокируют поток. Для 02 тесты зелёные; перед длинными книгами в 03 — `async` I/O или хотя бы `Task.Run` не надо, лучше настоящий async на чтение/запись entry.

3. **HAP `OptionOutputAsXml` + `OuterHtml`.** Replace пересобирает весь XHTML. На фикстуре VersOne открывает копию. На живых книгах возможны сюрпризы с xmlns/self-closing. Если после первой реальной книги разъедется вёрстка — смотреть сюда, не плодить второй writer.

## Не дефект 02

- PDF по-прежнему на CLI, не в `EpubBookService`.
- `translate` не реализован — это 03.
