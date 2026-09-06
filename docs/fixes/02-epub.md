# Правки после эпохи 02

Статус: **пункты 1–2 закрыты** (проверено по коду). Пункт 3 не чинить заранее.

1. `GetBlockFragments`: нет `p`/`h*`/`blockquote`/`li`, но body не пустой → один фрагмент = `InnerHtml`.
2. `WriteCopyAsync`: `CopyToAsync` + `ReadToEndAsync` / `WriteAsync`.
3. HAP по-прежнему пересобирает XHTML. Если живая книга разъедется — править этот writer.
