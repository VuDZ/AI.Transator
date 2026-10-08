# Эпоха 14 — Большие запросы OpenRouter

## Цель
Перевод больших глав через Chat Completions: отдельный предел входа, резерв длинного русского ответа, опциональные SSE и reasoning, настраиваемый кеш стабильного префикса.

## Вход / выход
Те же команды translate; настройки в Llm Options / Local. ILlmProvider по-прежнему возвращает целый LlmResponse после завершения генерации.

## В скоупе
- ContextWindowTokens — полный контекст модели (вход + выход).
- MaxInputTokens — необязательный положительный предел всего входа translate (prefix + HTML). 90000 означает вход, не весь контекст.
- ReservedOutputTokens — положительный лимит completion, включая reasoning; не прибавлять его к MaxInputTokens и не превышать полный контекст.
- TranslationOutputTokenMultiplier — неотрицательный конечный коэффициент расширения исходного HTML в русский ответ; 0 сохраняет прежнюю нарезку.
- ReasoningTokenReserve — неотрицательный запас внутри ReservedOutputTokens для планирования translate, строго меньше лимита ответа. Это оценка, не жёсткий лимит adaptive reasoning.
- Бюджет HTML = min(ContextWindowTokens - prefix - ReservedOutputTokens, MaxInputTokens - prefix при заданном, floor((ReservedOutputTokens - ReasoningTokenReserve) / multiplier) при multiplier > 0).
- Новые ограничения нарезки только для translate; extract сохраняет свой алгоритм. Общие настройки транспорта действуют на оба режима.
- В новом профиле паковать по HTML-блокам, оценивать целый склеенный чанк через существующий length/4; legacy сохраняет прежний алгоритм суммирования оценок блоков. Одна глава — один чанк, если удовлетворяет всем ограничениям; одиночный слишком большой блок — явная ошибка.
- Stream (default false): ResponseHeadersRead, SSE comments / multiline data / пустые choices / повтор finish в usage; объединять только delta.content. Требовать [DONE] и finish_reason. Незавершённый поток, JSON framing error, I/O и сетевые ошибки — ретраимые; partial не считать done.
- TimeoutSeconds ограничивает весь запрос, включая чтение тела. Внешняя отмена пробрасывается, таймаут ретраимый.
- ReasoningEffort: null (не отправлять), none / minimal / low / medium / high / xhigh / max. Отправлять только reasoning, без reasoning_effort; none -> enabled:false, остальные -> effort + exclude:true. Модель определяет поддерживаемые уровни. Reasoning не включать в перевод; ReasoningTokens читать из usage.
- CacheMode openrouter: cache_control только на стабильном system prefix, CacheTtl null (дефолт провайдера) / 5m / 1h. session_id — SHA-256 от prefix, стабилен между чанками для sticky routing. Без openrouter этих полей нет.
- Resume: новый профиль нарезки хранит hash настроек планирования. Несовпадение — отказ до перезаписи чанков; старый checkpoint можно продолжить только с прежним режимом. Stream / effort / TTL не меняют границы чанков.
- Профиль Haiku — отдельный committed example с пустым ApiKey, живой Local не переписывать.

## Вне скоупа
Новые провайдеры, пакеты, проекты, API, токенизатор Anthropic, продолжение оборванного ответа, перевод токенов на stdout, изменение словаря, автоподбор модели или лимитов из сети.

## Критерии приёмки
- MaxInputTokens=90000 допускает отдельный ReservedOutputTokens=128000 при ContextWindowTokens=1000000.
- Нарезка соблюдает вход и оценку расширения ответа, не теряет HTML-блоки.
- Legacy профиль сохраняет прежний бюджет.
- SSE собирает русский HTML, usage и finish; reasoning не попадает в content.
- HTTP/SSE 429/5xx ретраимы; 400/401/402/403 не ретраимы; неизвестная mid-stream ошибка ретраима.
- EOF без DONE/finish, malformed event, timeout после заголовков — ошибка; внешняя отмена не превращается в retry.
- TTL и session_id в OpenRouter-запросе корректны; прочие режимы не получают эти поля.
- Сборка и тесты через Roslyn MCP; после реализации сверка с этим файлом, замечания только в docs/fixes/14-openrouter-large-requests.md.

## Риски и источники
Размер файла в КБ не равен токенам. length/4 и multiplier — эвристики; length остаётся ошибкой валидатора, повышение коэффициента уменьшает чанки.
Haiku 5.5: контекст 1000000, completion до 128000 (проверено 2026-10-08). Поэтому 90000 входа не гарантирует перевод одной генерацией, если русский текст превышает выходной лимит.
Reasoning расходует общий output budget, exclude не отключает его. Для обычного перевода example выключает reasoning; включение low требует запаса и проверки качества.
Кеш зависит от минимального размера prefix, TTL и провайдера; cache write оплачивается, 1h дороже 5m. Кешировать каждую новую главу как стабильную часть нельзя.
- https://openrouter.ai/anthropic/claude-haiku-5.5
- https://openrouter.ai/docs/api/api-reference/chat/create-a-chat-completion
- https://openrouter.ai/docs/api/reference/streaming
- https://openrouter.ai/docs/guides/best-practices/reasoning-tokens
- https://openrouter.ai/docs/guides/best-practices/prompt-caching

## Зависимости
03/04 (нарезка и клиент), 08 (temperature), 09/11 (usage), 13 (прогрев и параллельность).
