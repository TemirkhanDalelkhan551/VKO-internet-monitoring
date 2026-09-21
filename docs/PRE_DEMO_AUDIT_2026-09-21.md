# Pre-demo audit — VKO Internet Monitoring

**Дата:** 21.09.2026  
**Версия репозитория:** `77ff066` (`main`)  
**Режим:** аудит без исправлений.  
**Легенда:** **VERIFIED** — проверено запуском/запросом; **CODE REVIEW** — подтверждено чтением кода и тестами, но не в реальном окружении; **NOT VERIFIED** — не проверялось либо для проверки не было безопасных учётных данных/условий.

## A. Executive summary

Это не макет. Основная цепочка действительно работает: Windows-служба собирает измерение, ставит событие в файловую очередь, доставляет его по HTTPS в API, а API сохраняет данные и отдаёт их веб-интерфейсу. Изолированный API-контур прошёл 154 интеграционные проверки; все 228 .NET и 28 web-тестов зелёные. В реальном установленном агенте вручную запрошен замер, он был собран и доставлен, очередь стала пустой.

Но проект **не готов к безусловному production-внедрению**. Главные причины: единственный бесплатный Render/API/PostgreSQL без подтверждённого backup/monitoring, ручной деплой, неподписанный EXE, слабая видимость отказов агента, отсутствие политики хранения/агрегации на квартал и неподтверждённая на production-креденшлах карта/AI-сценарий. Для хакатонной демонстрации он может быть убедительным после устранения P0/P1 ниже. Для пилота в школе — только с явной оговоркой «контролируемый пилот».

### Реально проверенная цепочка

`ПК → Windows Agent → замер/JSON outbox → HTTPS API → PostgreSQL → API/статический web → пользователь`

| Участок | Результат | Доказательство |
|---|---|---|
| Windows-служба | **VERIFIED PASS** | `VkoInternetMonitoringAgent` запущена, `AUTO_START`; настроены 3 перезапуска через 60 секунд. |
| Замер с реального ПК | **VERIFIED PASS** | 21.09 агент принял штатный trigger, проверил нагрузку, измерил Download/Upload/Ping и записал событие `Online`. |
| Очередь и доставка | **VERIFIED PASS** | После замера журнал сообщил об успешной отправке одного queued measurement; очередь — 0 файлов. |
| API и БД | **VERIFIED PASS (fixture)** | PostgreSQL fixture + API: 154/154 сценариев, включая идемпотентность, инциденты, роли, ошибочные payload. |
| Backend → frontend | **VERIFIED PASS (fixture)** | Логин, dashboard, рейтинг, детали школы и история открыты в браузере против изолированной БД. |
| Production readiness | **VERIFIED PASS** | `https://vko-internet-monitoring-api.onrender.com/health/ready` вернул 200; неавторизованный `/api/schools` — 401. |
| Production данные/карта/AI | **NOT VERIFIED** | Не использовались реальные учётные данные и не извлекались чужие данные. |

## 1. Карта проекта и архитектура

### Назначение

Система измеряет качество интернета на компьютерах школ ВКО, хранит историю измерений, строит аналитику/инциденты/рейтинг и позволяет ролям школы, региона и администратора управлять объектами и обращениями провайдеру. Дополнительно агент собирает статический hardware inventory.

### Компоненты

| Компонент | Реализация | Роль | Состояние аудита |
|---|---|---|---|
| Windows Agent | .NET 10 Worker Service, `src/VkoMonitoring.Agent` | расписание, замер, heartbeat, очередь, inventory | VERIFIED на реальном ПК частично |
| Agent Core/Infrastructure | `Agent.Core`, `Agent.Infrastructure` | домен, WMI, ICMP, throughput, HTTP, JSON outbox | CODE REVIEW + unit tests |
| Setup/EXE | WinForms `Agent.Setup` | активация, регистрация службы, локальный статус, trigger | VERIFIED установленный экземпляр; UI full-flow NOT VERIFIED |
| API | ASP.NET Core Minimal API, `src/VkoMonitoring.Api` | auth/RBAC, приём данных, отчёты, инциденты, рейтинг, AI-draft | VERIFIED fixture |
| БД | PostgreSQL на Render; JSON fallback для dev | история измерений, устройства, inventory, аудит | VERIFIED fixture; prod backup NOT VERIFIED |
| Web | статический HTML/CSS/JS в `Api/wwwroot` | dashboard, карта, рейтинг, инциденты, admin | VERIFIED desktop fixture; mobile NOT VERIFIED |
| Внешние сервисы | Render, PostgreSQL, OSM tiles, Groq-compatible AI | хостинг/БД/карта/AI | health VERIFIED; SLA/keys/config NOT VERIFIED |

### Поток данных и границы отказа

1. Worker ждёт окно измерения или локальный trigger.
2. `SystemLoadGuard` ждёт допустимые CPU/сетевую нагрузку не более 600 секунд.
3. Измерение валидируется и атомарно сохраняется JSON-файлом в локальную очередь.
4. Outbox отправляет `POST /api/measurements` с device token; `EventId` даёт at-least-once/idempotency.
5. API проверяет токен и соответствие device/school/line, пишет PostgreSQL, обновляет presence и инциденты.
6. Web получает данные по bearer session и показывает сводку, историю, карту и рейтинг.

Критические единые точки: DNS/интернет на ПК, Render free web-service, единственный PostgreSQL, внешний OSM и внешний AI, локальный диск с очередью.

## 2. Карта функций и результаты

| Функция | Где | Как должна работать | Проверка | Результат |
|---|---|---|---|---|
| Регистрация/активация агента | Setup + `/api/device-activation` | одноразовый код привязывает ПК к школе/линии | 25 fixture сценариев, invalid/recovery/rebind | **VERIFIED PASS** |
| Защита от неправильной школы/линии | API token binding | token не позволяет писать в чужой ресурс | API anti-spoof tests | **VERIFIED PASS** |
| Плановый и ручной замер | Agent worker/setup trigger | собрать метрики без UI-блокировки | реальный trigger, лог, delivery | **VERIFIED PASS** |
| Offline/degraded замер | Measurement service | зафиксировать отсутствие соединения, не «норму» | unit + integration | **CODE REVIEW PASS** |
| Outbox, повторная доставка | JSON outbox/dispatcher | не потерять событие при API failure | unit: failure/corrupt/capacity; API duplicate | **CODE REVIEW PASS** |
| Heartbeat/config | Agent/API | показывать живость агента и обновлять конфиг | API integration + production log | **VERIFIED PASS**, transient DNS warning found |
| Автоинциденты | API policy | открыть/закрыть после нужных измерений | integration transitions | **VERIFIED PASS** |
| Ручной инцидент/обращение | web/API | создать обращение и не отправлять его автоматически | fixture incidents 25/25 | **VERIFIED PASS (fixture)** |
| AI-черновик | `OpenAiAppealDraftGenerator` | сформировать только черновик из фактов | code/tests; real provider not invoked | **CODE REVIEW; NOT VERIFIED production** |
| Рейтинг школ/линий | reports web/API | 7/30d, показатели и явная формула | browser fixture + 50 reports | **VERIFIED PASS (fixture)** |
| Карта | `/map`, OSM | точки школ с координатами | map fixture 33/33 | **VERIFIED mechanics; production coordinates NOT VERIFIED** |
| Ролевой доступ | auth middleware/API | не видеть чужие организации/ресурсы | roles/IDOR fixture tests | **VERIFIED PASS (fixture)** |
| Hardware inventory | WMI → outbox → API | отправить снимок железа | real service logs “accepted” | **VERIFIED transport; field accuracy NOT VERIFIED** |
| Администрирование | web/API | users, schools, lines, device lifecycle | integration/directory tests | **VERIFIED PASS (fixture)** |
| Локальный статус/«Сообщить» | Setup WinForms | показать состояние, инициировать обращение | screenshot/code known; full manual flow not rerun | **NOT VERIFIED end-to-end** |

## 3. Functional testing

### Выполненные прогоны

| Сценарий | Ожидание | Факт | Статус |
|---|---|---|---|
| `dotnet test` Release | все unit/API тесты проходят | 228/228 passed | **PASS** |
| `node --test tests/web/*.test.mjs` | web logic проходит | 28/28 passed | **PASS** |
| Release build | без ошибок/предупреждений | build success, 0 errors/warnings | **PASS** |
| API integration с PostgreSQL | корректные и ошибочные операции | 154 checks passed | **PASS** |
| report/map/directory/activation/incidents | browser/API contracts | 50 + 33 + 32 + 25 + 25 passed | **PASS** |
| Несуществующий bearer | не отдавать данные | 401 | **PASS** |
| Malformed login JSON | не принимать запрос | 400 | **PASS** |
| Rate limiting | ограничивать brute force | 429 после лимита | **PASS** |
| Реальный агент → Render | измерение/очередь/доставка | `Online`, затем `Successfully sent 1 queued measurements` | **PASS** |
| Production readiness | сервер отвечает | `/health/ready` 200 | **PASS** |

### Наблюдение по процедуре тестов

Первый запуск API-скрипта сразу после `docker compose up` упал с `database system is starting up`: скрипт не ждёт readiness PostgreSQL. Далее после ожидания readiness — 154/154. Ещё одно ограничение: следующие web-сценарии на той же fixture наткнулись на login rate-limit 429; на свежих fixture все прошли. Это **не доказанный дефект бизнес-API**, но дефект воспроизводимости regression-процедуры.

## 4. Edge cases и агент

| Ситуация | Что проверено | Итог |
|---|---|---|
| API/DNS недоступен | реальный лог зафиксировал DNS `11001`; outbox unit tests | **VERIFIED:** heartbeat пишет warning; измерение имеет очередь. Пользователь не получает активное оповещение. |
| Соединение восстановилось | успешная реальная доставка после недоступности в разные моменты | **VERIFIED частично**; длительный outage не моделировался. |
| Corrupt queue file | unit test | **VERIFIED:** перенос в quarantine. |
| Переполненная очередь | unit test | **VERIFIED:** возникает `OutboxCapacityExceededException`, новые записи не вытесняют старые. Нет заметного админ-алерта. |
| Дубликат события | API integration | **VERIFIED:** идемпотентно принимается. |
| Пустой/negative/future payload | API integration | **VERIFIED:** 400. |
| Высокая нагрузка | unit + реальный normal load | **CODE REVIEW:** максимум ожидания 600 сек; бесконечного ожидания внутри одного запуска нет. |
| Нагрузка днями | не моделировалась | **NOT VERIFIED:** последовательные scheduled runs могут всё время пропускаться; пользователю видна только последующая stale-свежесть. |
| reboot/service crash | configuration inspected | **VERIFIED конфигурация:** auto-start, 3 recovery restarts; фактический crash/reboot не провоцировался. |
| sleep/firewall/no admin/full disk | не выполнялись разрушительно | **NOT VERIFIED** |
| два экземпляра агента | не запускались намеренно | **NOT VERIFIED**; service-копия одна, ручной EXE потенциально требует отдельной проверки. |

### Ответ: может ли агент безопасно работать месяцами?

**Для контролируемого пилота — вероятно да, но это не доказано длительным испытанием. Для “установил и забыл на месяцы” — пока нет оснований так обещать.**

Плюсы: Windows Service auto-start и recovery; CPU в простое около 0%, working set около 65.9 MB, private memory около 21.3 MB; очередь пуста; ограничение задержки нагрузки 600 s; JSON outbox и идемпотентность.

Что мешает обещанию: нет долговременного soak test, не проверены sleep/reboot/firewall/full-disk; при повторяющихся пропусках под нагрузкой нет активного алерта; queue ограничена 5000 и без явного централизованного тревожного сигнала; transient DNS failures уже наблюдались; inventory обновляется только при старте процесса; EXE неподписан.

## 5. Устройства и hardware inventory

### Идентификация

Идентификатор установки — persistent random GUID в локальной data directory, а не hostname. Это хорошо против простого переименования ПК; токен хранится с DPAPI LocalMachine и ACL только SYSTEM/Administrators. API использует отдельный token и проверяет привязку к школе/линии.

| Событие | Ожидаемый результат | Оценка |
|---|---|---|
| Изменён hostname | тот же device | **CODE REVIEW:** да, GUID сохраняется. |
| Переустановка агента с сохранённым ProgramData | тот же device | **CODE REVIEW:** да. |
| Переустановка Windows/удаление ProgramData | новый ID, нужна новая активация | **CODE REVIEW:** да. |
| Замена железа | тот же device, inventory может измениться | **CODE REVIEW:** ID сохраняется. |
| Клонирование образа вместе с ProgramData | duplicate identity | **RISK:** конфликт/неверная привязка возможны; процесс клонирования не защищён. |
| Новый/чужой device/organization | запись запрещена | **VERIFIED fixture:** token binding/roles блокируют. |

### Hardware

Collector пытается собрать CPU, RAM, диски, GPU, network adapters, OS, manufacturer/model, BIOS, serials/UUID. Он выдерживает недоступность отдельных WMI-классов пустыми полями/массивами, а доставка inventory с реального ПК подтверждена логом. **Точность конкретных полей на разных моделях ПК, виртуальных адаптерах, нескольких GPU/дисках не проверялась.**

Статическое железо логично собирать при activation и на старте/явном refresh. OS, драйвер GPU, сетевые адаптеры — при startup и по расписанию (например, раз в 7–30 дней). CPU/RAM/disk utilisation — не inventory, а отдельная телеметрия с другой частотой. Сейчас inventory собирается один раз на запуске: изменение железа во время непрерывной работы не будет замечено.

## 6. API audit

**VERIFIED**: корректные запросы, wrong/missing auth, RBAC/IDOR, activation/recovery, heartbeat/config, accepted/duplicate/old queued measurements, invalid negative/future/missing metrics, lifecycle device, auto/manual incidents, analytics, user lockout, audit fail-closed, rate 429, malformed login (400). Проверено против отдельного PostgreSQL, не mock.

Ограничения: не проведён destructive fuzzing, нагрузочный тест, реальные production write-запросы, независимый pen-test. Поэтому отсутствие найденной уязвимости не является доказательством её отсутствия.

## 7. Security audit

### Подтверждённые меры

- repository scan не нашёл рабочих ключей, паролей или connection strings; найдены только dev-fixtures/placeholder.
- HTTPS production endpoint; неавторизованная защита API подтверждена (401).
- device tokens хешируются сервером; локальный token — DPAPI LocalMachine + ACL SYSTEM/Administrators.
- RBAC и resource-scope покрыты integration tests; audit before mutation fail-closed.
- CSP `default-src 'self'`, same-origin CORS by отсутствию настройки, `X-Content-Type-Options: nosniff`, `Referrer-Policy` observed.
- AI generator задаёт `store:false`, передаёт факты и генерирует черновик, не отправку провайдеру.

### Риски

| Приоритет | Риск | Факт/последствие |
|---|---|---|
| P1 | Неподписанный installer | Authenticode не подтверждает publisher; SmartScreen/политика школы могут блокировать EXE. |
| P1 | Избыточные hardware identifiers | Серийные номера/UUID/MAC — персонально/организационно чувствительная инвентаризация; нет доказанной политики retention/consent. |
| P2 | Нет HSTS/Permissions-Policy в проверенных ответах | HTTPS есть, но hardening неполный. |
| P2 | Логи содержат stack trace/внутренний DNS host | полезно для поддержки, но слишком подробно при доступе к логам. |
| P2 | Нет независимого security test | SQLi/XSS/command injection покрыты частично архитектурой/валидацией, но не pen-test. |
| P3 | Локальная очередь не шифруется | она содержит измерения; доступ ограничивается диском/Windows, но это нужно явно решить политикой. |

Не обнаружено доказательств SQL injection, XSS, path traversal, подмены устройства или межорганизационного чтения в проверенных сценариях. Это **не** равнозначно «уязвимостей нет».

## 8. UX audit

### Что понятно с первого экрана (VERIFIED desktop fixture)

Dashboard показывает количество организаций, online devices, problem lines, stale/missing, период и средние показатели. В навигации видны «Школы и линии», «Инциденты», «Рейтинг качества», admin. Детальная карточка школы разделяет network status, freshness agent и историю. Рейтинг раскрывает формулу: 100 минус штрафы за проблемные замеры, speed, ping/jitter/loss, freshness и incidents.

### Что непонятно или рискует ввести в заблуждение

- В fixture строки «Нет данных» всё равно получили числовое место рейтинга. Для нетехнического пользователя это выглядит как честное сравнение, хотя сравнивать нечего. **P1 UX.**
- В карточке виден длинный internal Device ID — полезен support, но шум для директора. **P3.**
- Инвентаризация с пустым состоянием корректно сообщает «агент ещё не передал», но не подсказывает срок/следующее действие. **P2.**
- Термины Jitter, Packet loss, stale и методика метрик требуют tooltip/короткого «что делать». **P2.**
- UI на desktop проверен; mobile width, keyboard-only, screen reader, slow network, loading/error/empty states на всех экранах — **NOT VERIFIED**.
- Production-карта с реальными координатами не проверялась; fixture честно показала 0 из 3 точек с координатами и не может доказать production-данные.

### Personas

| Пользователь | Поймёт сразу | Где застрянет/чего не хватает |
|---|---|---|
| Директор | цвет/проблемные линии/инциденты | что такое jitter и что делать; рейтинг без контекста; нужен короткий action plan. |
| Сисадмин школы | устройство, история, статус агента | диагностика DNS/queue/логов и recovery не доступны централизованно. |
| Региональный специалист | таблицу школ, период, рейтинг | «Нет данных» в рейтинге, достоверность/свежесть и координация работы с провайдером. |
| Новый пользователь по ссылке | назначение dashboard | роли, организация, значения и следующий шаг без onboarding. |

## 9. Scaling and DevOps audit

| Масштаб | Реально приемлемо | Главные ограничения |
|---|---|---|
| 1 школа | Да, как пилот | ручная активация, free-hosting, отсутствие central alert. |
| 10 школ | Возможно | onboarding/поддержка вручную; одновременный login/rate limit, нужно проверить backup. |
| 100 школ | Рискованно на текущем hosting | single free API/DB, ручной deploy, нет observability, update channel/MDM. |
| 1000 ПК | Не готово | throughput/DB/connection/load test отсутствуют; NAT rate limits, массовое восстановление очередей, backup/retention/операции. |

При 1000 ПК и 4 замерах в день — около **360 000 measurement events за квартал** до повторов/heartbeat/inventory. Сейчас нет доказанной политики retention, rollups/partitioning, backup/restore и capacity budget. Рекомендация организаторов о хранении квартала не закрыта доказательно: «данные не удаляются» не равно управляемому квартальному хранению.

Дополнительно: `render.yaml` использует free web/free PostgreSQL и `autoDeployTrigger: off`. Render может иметь cold start, а push в GitHub не становится production до ручного deploy. Это допустимо для прототипа, но нельзя подавать как fully managed production.

## 10. Failure modes и silent failures

| Компонент | Что ломается | Вероятность | Последствие | Обнаружит ли система | Восстановление | Улучшение |
|---|---|---:|---|---|---|---|
| DNS/Internet агента | heartbeat/send не уходит | средняя | stale данные | только позже по freshness/логу | retry/outbox | alert по отсутствию heartbeat/queue age |
| Agent under load | 600s defer и skip | средняя | нет замеров | не сразу | следующее окно | измерение причины/alert после N skips |
| Queue full/disk full | новые замеры не пишутся | средняя | gap истории | лог, не явный UI alert | освободить диск/доставить | queue health + policy/quota alert |
| Render free service | sleep/fail | средняя | demo/API outage | health endpoint, но нет external monitor shown | cold start/manual deploy | paid plan/uptime monitor/runbook |
| PostgreSQL | outage/corruption | низкая-средняя | data loss/unavailable | app error | restore unknown | managed backup + restore drill |
| OSM | tiles unavailable | средняя | пустая карта | визуально | retry browser | fallback/notice/cached tiles |
| Groq/AI | timeout/quota/key issue | средняя | AI draft fails | UI error likely | manual appeal still exists | explicit fallback + demo preflight |
| Inventory WMI | missing/slow class | средняя | неполный inventory | empty data can look valid | restart/retry delivery | per-field error/state/refresh |
| Device clone | same installation ID | средняя в image rollout | duplicate/mis-bound device | activation conflict | re-activate/recover manually | clone-safe enrollment process |
| EXE/Windows policy | SmartScreen/EDR blocks | средняя | cannot install | installer error | admin exception | code signing/pre-flight guide |

**Главные silent failures:** (1) repeated heartbeat/DNS failure; (2) repeated load-skips; (3) queue capacity/disk pressure; (4) hardware partial empty snapshot; (5) stale data being read as current if user ignores freshness.

## 11. Jury perspective

### Чего бояться на защите

Не обещайте “автономную систему для области, готовую на 1000 ПК”. Честная и сильная позиция: **работающий end-to-end пилот с защищённой идентификацией, очередью, incident/rating/AI-assist, плюс понятная дорожная карта industrialization**. Не демонстрируйте live AI или production map, пока не сделан preflight на конкретной учётной записи.

### 30 неудобных вопросов

| Вопрос | Почему зададут / опасность | Хороший ответ уже есть? | Что подготовить |
|---|---|---|---|
| 1. Какую проблему решаете? | Нужен value, высокая | Частично | 1 реальный incident → реакция/время. |
| 2. Почему не Zabbix/PRTG? | Дифференциация, высокая | Частично | сравнение: school workflow, offline queue, role analytics. |
| 3. Где реальные данные? | Доверие, высокая | Частично | заранее открытый реальный замер с timestamp. |
| 4. Что доказывает, что агент не фейк? | Доверие, высокая | Да | service + manual trigger + новая строка dashboard. |
| 5. Что без интернета? | Core, высокая | Да частично | outbox/idempotency, честно показать pending/recovery. |
| 6. Что при падении сервера? | Reliability, высокая | Частично | queue; признать backup/monitoring gap. |
| 7. Как не потеряете данные? | Reliability, высокая | Частично | EventId/outbox; backup plan needed. |
| 8. Почему данные достоверны? | Methodology, высокая | Частично | методика, duration, endpoint, timestamp, limits. |
| 9. Можно ли подделать замер? | Security, высокая | Частично | device token/RBAC; честно: endpoint owner can manipulate local agent. |
| 10. Где AI? | Claimed feature, высокая | Частично | pre-generated dry-run; no auto-send. |
| 11. Почему AI не галлюцинирует? | Safety, средняя | Частично | facts-only prompt/manual edit; no guarantee. |
| 12. Что храните из железа? | Privacy, высокая | Нет полностью | data inventory + consent/retention policy. |
| 13. Законно ли собирать serial/MAC? | Privacy, высокая | Нет | согласование владельца/минимизация. |
| 14. Кто ставит агент? | Adoption, средняя | Да частично | installer/admin guide. |
| 15. Нужен ли admin? | Deployment, средняя | Да | service install needs admin; status UI afterward. |
| 16. Что после reboot? | Reliability, средняя | Да частично | auto-start/restart configuration screenshot. |
| 17. Как обновляете 100 агентов? | Scale, высокая | Нет | MDM/winget/update channel roadmap. |
| 18. Что на 1000 ПК? | Scale, высокая | Нет | load test/capacity plan/managed infra. |
| 19. Сколько стоит? | Viability, средняя | Нет | cost model hosting/AI/support. |
| 20. Почему Render free? | Credibility, высокая | Частично | prototype; paid plan before pilot. |
| 21. Есть backup? | Data loss, высокая | Нет verified | backup/restore evidence. |
| 22. Где квартальная история? | Organizer requirement, высокая | Частично | retention policy + chart/sample. |
| 23. Как рейтинг считается? | Fairness, высокая | Да | expand formula and explain no-data handling. |
| 24. Почему “нет данных” имеет место? | UX/data quality, высокая | Нет | fix or hide rank pre-demo. |
| 25. Что означает “нестабильно”? | Actionability, средняя | Да частично | reason/threshold shown at detail. |
| 26. Кто видит другую школу? | Security, высокая | Да fixture | role isolation test/demo account. |
| 27. Что если код активации украли? | Security, средняя | Частично | one-time code/revoke/rotation; operational process. |
| 28. Что при clone Windows? | Device identity, средняя | Частично | fresh activation; document image procedure. |
| 29. Почему карта пустая/неверная? | Demo risk, высокая | Не verified | production coordinates preflight. |
| 30. Где пилот и результат? | Credibility, высокая | Частично | exact measured pilot facts, not synthetic claims. |

## 12. Demo audit — 3–5 минут

### Оптимальный flow

1. **20 сек.** Проблема: регион видит не “есть интернет/нет”, а качество, свежесть и инциденты.
2. **45 сек.** Открыть заранее проверенную dashboard с сегодняшним timestamp; показать школу, line, freshness и причину статуса.
3. **45 сек.** Детали проблемной школы: history Download/Ping, incident, что измерение пришло с Windows Agent.
4. **35 сек.** Открыть рейтинг и раскрыть формулу; не акцентировать строки без данных до их исправления.
5. **35 сек.** Открыть агент: service/status, нажать/показать заранее совершённый manual measurement и появившуюся запись.
6. **30 сек.** Показать draft обращения **только если заранее проверен ключ/лимит**; сказать «черновик, не автоотправка».
7. **20 сек.** Закрыть roadmap: backups, signed installer, pilot scaling.

### Не открывать без preflight

- live deployment/Render console;
- live AI при не проверенном ключе/quota;
- карта без проверенных production coordinates;
- пустые ranking rows;
- installer на новом PC без заранее проверенного admin/SmartScreen;
- slow/cold Render after idle.

## 13. Priorities

### P0 — критично до сцены

1. **Preflight production demo:** реальные login, dashboard timestamp, coordinates, agent write, AI (если заявляете) должны быть проверены за 15 минут до показа. Сейчас этот факт NOT VERIFIED.
2. **Не заявлять production-scale/backup/AI-live как факт.** Это красный флаг доверия, если жюри попросит доказательство.

### P1 — исправить перед demo/pilot

1. Подписать installer либо подготовить официальный controlled-installation ответ и исключение SmartScreen/EDR.
2. Ввести внешнюю доступность API/DB + alert при stale agent/queue age/повторных skips; observed DNS warnings уже были.
3. Сделать retention/backup/restore policy на квартал; выполнить и сохранить один restore drill.
4. Убрать numeric rank для `Нет данных`, явно отделить absent data от плохого score.
5. Документировать/защитить inventory: consent, minimum fields, retention/access.
6. Явно показывать причину skip/queue/full disk/inventory incomplete в admin UI.
7. Подготовить production coordinates audit и fallback карты.

### P2/P3

- HSTS/Permissions-Policy, сокращение stack trace в production logs.
- Responsive/accessibility/slow-network audit.
- Периодический inventory refresh и device clone-safe process.
- Нагрузочное тестирование, update channel/MDM, DB partitioning/rollups.

## M. TOP-10 действий перед презентацией

| # | Проблема → почему важно | Что изменить / где | Сложность | Эффект |
|---:|---|---|---|---|
| 1 | Demo может показать stale/пустые данные → убивает доверие | Сделать checklist preflight: production health, login, one fresh measurement, AI/key, map; `docs/DEMO_SCRIPT.md` | S | Максимальный |
| 2 | Free Render/manual deploy → падение/холодный старт на сцене | Разогреть URL перед показом, закрепить fallback screenshots; затем paid/monitoring | S | Очень высокий |
| 3 | «Нет данных» получает место → ложный рейтинг | UI/API rating: no rank/no score badge для отсутствующей базы | S | Высокий |
| 4 | DNS/queue/skip могут молча сделать данные старыми | Alert/health: stale heartbeat, queue age/count, skip count; Agent + API/admin | M | Высокий |
| 5 | Нет доказанного backup/quarter policy | 90d retention + DB backup + restore test/runbook; infra/docs | M | Высокий |
| 6 | Installer может блокироваться | Code-sign EXE или formal pilot deployment instruction; release pipeline | M | Высокий |
| 7 | Production карта не доказана | CSV/DB validation 15 schools, screenshot + coordinate audit | S | Высокий |
| 8 | Privacy inventory | удалить ненужные serial/MAC либо consent/role/retention notice; agent/API/docs | M | Средне-высокий |
| 9 | AI может не сработать live | explicit error/fallback to normal draft + preflight model/quota; API/web | S | Средний |
| 10 | 100+ ПК неоперабельны вручную | activation bulk/onboarding + update/MDM plan + 100-device load test | L | Высокий для пилота |

## Final answer to the main question

**Пять вещей, которые беспокоят больше всего, если завтра одновременно демонстрация и реальный пилот:**

1. Free single-instance Render/PostgreSQL без доказанных backup, external monitoring и restore — сервис может быть недоступен ровно в момент demo/пилота.
2. Агент уже фиксировал transient DNS failures; без явного alert повторные пропуски/очередь/full disk могут долго выглядеть как «система просто давно не обновлялась».
3. Неподписанный EXE и отсутствие массового update/onboarding процесса делают реальное школьное внедрение хрупким.
4. Квартальное хранение, capacity и масштабирование не доказаны; на 100–1000 ПК текущую инфраструктуру нельзя уверенно обещать.
5. Production AI/map/real-data demo ещё нужно подтвердить конкретным preflight, иначе сильная функциональность рискует выглядеть как визуальная декларация.

