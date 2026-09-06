# Правки после эпохи 00

Статус: **закрыто** (проверено после правки).

1. Host живёт до конца `InvokeAsync` (`using var host`). `CommandTree.Create(host.Services)` — провайдер доходит до compile / extract / translate.
2. [local-config.md](../local-config.md): источник Local — рядом с csproj, чтение — копия в `BaseDirectory` после сборки.

Новых замечаний нет. Эпоха 01 может резолвить сервисы из переданного `IServiceProvider`.
