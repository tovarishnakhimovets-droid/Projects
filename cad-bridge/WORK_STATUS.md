# CAD bridge — состояние работы

## Текущее состояние — 2026-10-08

- Единственные исходники и технические инструкции: `team/Projects/cad-bridge`
  в репозитории `git@github.com:tovarishnakhimovets-droid/Projects.git`.
  Пользователь выполнил merge PR #2 и pull; исходный импорт
  `a54a174d2459bc063085450e04c15d2c970bd0ab` принят merge `0e81f07`.
  База подготовки текущего документационного коммита — `develop`, `7ba4b30`
  (уборка и ссылка на пакет); удалённая `develop` сверена через ls-remote.
  Изменения кода после этого коммита при текущей проверке не обнаружены.
  Git-команды, публикацию и merge выполняет пользователь.
- Снимок upstream v2.0.1 (`4d0f75646631186612af926944d7e33affc4757d`)
  содержит все локальные исправления. Пять патчей уже применены; повторно
  применять их не надо. Нет вложенного .git, Autodesk DLL и рабочих чертежей.
- До уборки в этом checkout прошли 708/708 .NET offline-тестов, SDK
  initialize/tools-list подтвердил 42 инструмента и style-схемы без CAD-вызовов.
  Сверены 286 source hashes и 40 файлов ZIP; оба плагина скомпилированы.
  Compile-only AutoCAD.NET 25.1.0 используется для 2026 / .NET 8;
  версия 25.1.1 требует .NET 10 и не заменяет эти ссылки.
- Единственный ZIP для передачи:
  `releases/team/2.0.1-team.2/DwgMcp.Setup-v2.0.1-team.2-win-x64.zip`.
  SHA256: `d74a568bbfccb462987d7cff82f9d05c8108e9c1b267a45338f83a7c9bee092d`.
  Архив уже проверен и при уборке не изменён. Manifest хранит исходный
  commit/dirty=true времени сборки; переписывать его на merge commit нельзя.
- Пользователь заменил публичный архив: https://disk.yandex.ru/d/tzTFb_evK8vwJg.
  Публичный API Яндекс Диска подтвердил имя, размер 39622751 байт и SHA256
  выше — файл совпадает с единственным локальным ZIP. Старая ссылка больше
  не используется; архив агентом не пересобирался и не публиковался.
- На компьютере автора работающий dwg-2027 и загруженный плагин по-прежнему
  используют `D:\CAD-Automation\releases\dwg-2027\2026-10-06-style-tools`.
  Этот runtime сохранён по исходному пути, чтобы не ломать открытые приложения.
  Источник кода переключён на принятый Git checkout; runtime обновляется отдельно.
- AutoCAD 2026 у ГИПа: пользователь прислал успешный вывод install.ps1 для
  team.2 / Years 2026 / Client none; bundle и server установлены, check OK.
  Автозагрузка и нативное подключение пока не подтверждены. Первый запрос при
  доступном MCP — dwg_get_drawing_info; изменения и сохранение не нужны.

## Civil 3D 2027: загрузка и read-only связь — 2026-10-08

- Пользователь вручную загрузил существующий Acad27 plugin из действующего
  style-tools runtime через NETLOAD в Civil 3D 2027. Плагин сообщил канал
  BimwrightDwg-2027-15816; процесс 15816 запущен с /product C3D и C3D_Metric.
  Discovery dwg-2027 подтвердил тот же PID и pipe до вызова инструмента.
- Прямой dwg_get_drawing_info из текущего чата вернул ok=true:
  Чертеж1.dwg, Model, слой 0, единицы Meters, исходный шаблон acadiso.dwt.
  Изменения и сохранение чертежа не выполнялись.
- Подтверждены ручная загрузка существующей DLL и чтение метаданных DWG в Civil.
  Это не проверка всех операций или специальных объектов Civil. Native Civil
  tools пока отсутствуют; фильтр автозагрузки Platform=AutoCAD не менялся.

## Подтверждённые возможности

- Сервер dwg-mcp 2.0.1; профиль query,modify,drawing,view — 42 инструмента.
- AutoCAD 2027: plugin-acad27 net10.0-windows. Ранее пользователь подтвердил
  живую связь, создание/чтение/удаление Line и изменение/восстановление DIMSCALE.
  Строка 2.5 принимается независимо от Windows-региона; 2,5 отклоняется.
- 2026-10-06 пользователь подтвердил dwg_set_lineweight, dwg_set_layer_lineweight
  и dwg_create_hatch: 0.30 мм / ByLayer / ByBlock, SOLID Circle/Rectangle,
  белая RGB и ACI30, отказ для 0.31 мм и границы Line без изменений.
  Пять созданных объектов и GUID-слой удалены; сохранение не выполнялось.
  Это не проверка всех 42 операций и не live-проверка 2026.
- Для ГИПа AutoCAD 2026 / .NET 8: plugin-acad26 net8.0-windows, официальные
  compile-only ссылки AutoCAD.NET 25.1.0. Общая реализация совпадает с 2027.
  Установка у ГИПа подтверждена выводом install.ps1; автозагрузка и нативное
  подключение 2026 пока не подтверждены.
- Пакет team.2 включает оба года и self-contained win-x64 сервер. team.1
  был только для 2027. ZIP вне Git; установка описана в docs/TEAM_SETUP.md.

## Очередь доработок из рабочих задач

Все пункты ниже ожидают распределения между участниками. Реализация общих
инструментов не начата; сценарии НВ остаются прототипами. Перед началом указать
исполнителя и рабочую ветку в строке задачи. Детали проверок сохранены ниже в
истории НВ; порядок строк не задаёт приоритет.

| ID | Доработка | Следующий шаг |
| --- | --- | --- |
| CAD-01 | Сохранение текущего открытого DWG: eFilerError; SaveAs не переименовал вкладку | Проверить имена документа/БД, доступ к записи и блокировки; определить правильный жизненный цикл сохранения. Live-save только по запросу. |
| CAD-02 | Проверка документа по полному пути; has_saved_path учитывает DWT | Определить контракт диагностики и проверки ожидаемого DWG перед записью; проверить сохранённый и несохранённый документ. |
| CAD-03 | Инвентаризация Civil/Map proxy-объектов | Ограничить объём/время, вернуть классы, владельцев, разрешения и признак полноты результата. |
| CAD-04 | Удаление явно выбранных proxy-объектов | Проверять документ, fingerprint, класс и разрешение удаления; одна транзакция, без неявного удаления служебных корней. |
| CAD-05 | Разбиение сети в узлах | Выделить геометрию и preflight из прототипа: заданные handles/допуски, сохранение изгибов/свойств, карта разбиения и проверка конечных точек. |
| CAD-06 | Совместное изменение конца полилинии и блока потребителя | Заданные объекты и ожидаемая геометрия, одна транзакция для конца трубы, блока и атрибутов; точное совпадение конечных координат. |

ГИП пока собирает свои задачи, конкретные запросы ещё не получены. Добавлять их
в эту очередь с примером и ожидаемым результатом, затем согласовывать исполнителя.
Совместный процесс описан в docs/CAD_WORKFLOW.md.

Другие ограничения и проверки:

- AutoLISP блокируется upstream; не обходить отказ через C#.
- SOLID hatch поддерживает одну Circle/closed planar lightweight Polyline,
  без островов и произвольных узоров. Наклонные границы и видимый draw order
  отдельно live не подтверждены.
- Civil 3D 2027: ручная загрузка и чтение метаданных проверены; автоматическая
  загрузка пока не настроена. Контракты специальных Civil-инструментов не заданы.

## Уборка workspace — 2026-10-07

- Общие правила структуры записаны в личный AGENTS.md. Корневые AGENTS.md
  и WORK_STATUS.md содержат только указатели; рабочие правила и состояние
  редактируются в cad-bridge. Runtime-инструкция для будущей упаковки берётся
  из того же AGENTS.md, отдельный редактируемый экземпляр удалён.
- Удалены 3242 лишних файла (850.5 МиБ): старые upstream/AutoCAD-MCP,
  копии docs/packaging/patches/.NET helpers,
  старые командные ZIP и временные сборки удалены после сверки исходников.
  .venv и runtime загруженного плагина сохранены. scripts/ и lisp/ содержат
  уникальную работу других чатов и сохранены без изменений.
- Старый COM/Python мост перенесён в legacy/autocad-2023. Root server.py и
  tools/acad-bridge.ps1 оставлены как короткие переходные entrypoints для
  уже запущенных процессов; реализации там нет. Конфигурация новых запусков
  указывает прямо на legacy/autocad-2023. Процессы не перезапускались.
- Перенос legacy: 24/24 файлов совпали по hash после Move-Item; затем изменены
  выбор Python в протокольном тесте и поиск общего .venv в setup. Offline:
  17 passed, 2 live skipped, 9 subtests passed; PowerShell 5.1 parsing успешен.
  AutoCAD не менялся и не сохранялся. Архив не пересобирался.
- Единственный остаток уборки: D:\CAD-Automation\.pytest_cache. Windows
  отказывает в чтении ACL и takeown даже при запуске вне sandbox; каталог
  не удалён. Для него нужны права администратора. Работе моста он не мешает.
- После удаления stage упаковщик заранее отклоняет обычную сборку в каталог,
  где уже есть ZIP или SHA256: существующий архив не перезаписывается.
  PowerShell 5.1 parsing и whitespace checks изменённых файлов прошли.
- Итоговая сверка подтвердила неизменность 10 уникальных scripts/lisp,
  используемого Python, DLL и ZIP; все пути двух MCP-конфигураций существуют.

## Следующий шаг

1. Пользователь оформляет документационные изменения отдельной рабочей веткой
   и PR в develop; состав включает запись Civil и сохранённую запись другого
   чата о вводах НВ. Код, runtime и проверенный ZIP при этом не обновляются.
2. Получить задачи ГИПа и распределить очередь до начала реализации.
3. Подтвердить read-only связь dwg-2026 у ГИПа. Живые modifying-тесты
   включаются отдельно. Установка описана в docs/TEAM_SETUP.md.

## История проверок до переноса структуры

Ниже сохранены уникальные результаты прежнего журнала, включая legacy 2023
и прототипы других проектов. Пути и промежуточные статусы в истории относятся
к тому времени; текущие пути и итог проверки указаны выше.

### Переход на AutoCAD 2027 / .NET 10 — 2026-10-06

Работа продолжается совместно. С 2026-10-06 пользователь выбрал удобный режим:
агент правит код, собирает и запускает offline-тесты; пользователь выполняет
действия в AutoCAD и команды живых проверок. Агент кратко объясняет изменения.

- Пользователь подтвердил AutoCAD 2027: ACADVER=26.0s; acad.exe находится в
  C:\Program Files\Autodesk\AutoCAD 2027. Установлен .NET SDK 10.0.401.
- Эталон для оценки: upstream/dwg-mcp, тег v2.0.1 (bimwright/dwg-mcp).
- Пользователь собрал plugin-acad27 под net10.0-windows и внешний сервер под
  net8.0. Плагин загружен через NETLOAD; MCPSTART подтвердил Named Pipe.
- Ручной клиент на существующем официальном Python MCP SDK успешно выполнил
  initialize и dwg_get_drawing_info: сервер dwg-mcp 2.0.1, ответ ok=true.
- В новом несохранённом Чертеж1.dwg, созданном из acad.dwt, upstream возвращает
  drawing_name=acad.dwt и has_saved_path=true. Флаг учитывает исходный шаблон;
  он не подтверждает сохранение нового DWG и не годится для выбора файла save.
- Пользователь запустил tools/check-dotnet-mcp.py --live-line в тестовом чертеже:
  линия 23E создана на слое 0; чтение подтвердило Line, ACI=1, точки (0,0,0)
  и (100,0,0), длину 100. Удаление и недоступность handle после erase подтверждены.
  Итог: PASS create / read / erase. Сохранение чертежа не вызывалось.
- Пользователь запустил штатный offline-набор upstream/tests/Bimwright.Dwg.Tests:
  исходная сборка — 606 тестов, 605 passed, 1 failed, 0 skipped. Ошибка строки
  DIMSCALE="2.5" связана с Convert.ToDouble без явно заданной культуры.
- Пользователь применил patches/dwg-mcp-invariant-numbers.patch: Double.Parse
  с NumberStyles.Float/InvariantCulture для строк и две culture-регрессии.
  Повторный offline-набор: 608/608 passed. Изменены только SystemVariableCatalog.cs
  и DrawingVariableCatalogTests.cs; наличие правки подтверждено чтением и git diff.
- После указания пересобрать плагин и загрузить DLL в новой сессии пользователь
  запустил --live-variable. Вывод: ORIGINAL DIMSCALE=1.0, RESTORED DIMSCALE=1.0,
  затем ExceptionGroup. Строки SET/PASS отсутствуют; причина ещё не установлена.
  Отдельное подтверждение пересборки и загрузки обновлённой DLL не получено.
  Повторять мутацию до выяснения ошибки не следует.
- В tools/check-dotnet-mcp.py исправлен вывод ошибок: MCP isError проверяется
  до JSON-разбора; ExceptionGroup печатается с вложенными исключениями.
  Добавлен --inspect-variable: initialize/list_tools и схема setter, без вызовов
  CAD-инструментов. Это следующий диагностический шаг, регион Windows не меняем.
  Существующий журнал сервера в LOCALAPPDATA недоступен агенту по правам чтения.
  Живые проверки записи требуют явного --live-line или --live-variable.
- Пользователь выполнил --inspect-variable: value в фактической MCP inputSchema
  имеет type=array с рекурсивным $ref. Причина неверной схемы — Newtonsoft JToken
  в параметре DrawingWriteTools.SetSystemVariable при System.Text.Json SDK.
  Подготовлен patches/dwg-mcp-variable-json.patch: JsonElement на входе, затем
  JToken.Parse(GetRawText()) для сохранения типов в запросе плагину; добавлен один
  тест фактической схемы SDK по образцу существующего ViewImageResponseTests.
  git apply --check прошёл; применение и проверки подтверждены ниже.
  Меняется только внешний сервер; этот патч не требует перезагрузки плагина.
- Скриншот пользователя подтвердил применение JSON-патча и 609/609 passed.
  --live-variable завершился PASS: строка "2.5" установила DIMSCALE=2.5;
  "2,5" отклонена без изменения; исходное 1.0 восстановлено. Один Failed в toast
  соответствует ожидаемому отказу. Поддержка invariant-правки в плагине также
  подтверждена этим живым результатом.
- Агент запустил --list-tools в sandbox: initialize и tools/list успешны;
  профиль query,modify,drawing,view содержит 39 инструментов, схема value теперь
  без type=array. CAD-инструменты в этой проверке не вызывались.
- .NET MCP зарегистрирован в реальном пользовательском Codex как dwg-2027 через
  codex mcp add (вне sandbox). enabled=true подтверждён через codex mcp get.
  .mcp.json, README и AGENTS.md обновлены для 2027; прежняя cad-automation
  регистрация и COM-код для 2023 сохранены отдельно. Python adapter не создавался.
- Подключение MCP в Codex обновлено. По запросу пользователя «проверь связь»
  агент вызвал dwg_get_drawing_info напрямую через mcp__dwg_2027 из этого чата:
  ok=true, активный Чертеж1.dwg, слой 0, пространство Model, единицы Inches.
  Запись в чертёж не выполнялась. Связь Codex → .NET MCP → AutoCAD 2027 подтверждена.
  Upstream v2.0.1 блокирует AutoLISP; поддержка наших .lsp остаётся открытым
  требованием. 39 зарегистрированных инструментов не равны 39 живым проверкам.

2026-10-06: для запрошенной пользователем таблицы условных обозначений
потребовались отсутствующие в typed-профиле вес линии и SOLID-заливка.
Добавлены scripts/network-legend.csx и tools/run-dotnet-code.py — исполнение
доверенного синхронного C# через штатный dwg_send_code временного MCP SDK-клиента.
Прямой .NET API; AutoLISP/COM не вызываются. Сценарий предварительно скомпилирован
с Autodesk DLL 2027 (0 ошибок), выполнен одной транзакцией: 27 объектов, слои
НВ=ACI150, НК1=RGB139/69/19, НК2=ACI30, вес ByLayer=0.30 мм, 6 белых заливок.
Сценарий защищён проверками документа, пространства, единиц и пустого контекста.
dwg_zoom_window и dwg_capture_view_image успешно показали итоговую таблицу;
изображение подтвердило белые колодцы поверх линий и русские подписи.
Постоянный профиль dwg-2027 остаётся query,modify,drawing,view; файл не сохранялся.

### Общие инструменты условников — собраны и проверены в AutoCAD 2027

- Вес линии: общий typed-инструмент для объектов по handles и для именованных
  слоёв. Параметры: миллиметры из допустимого набора AutoCAD; для объектов также
  ByLayer/ByBlock. Возвращать изменённые объекты/слои и ошибки. Проверять весь
  запрос до изменения; учитывать заблокированные слои.
- Штриховка: общий typed-инструмент создания Hatch по заданным boundary handles.
  Первый объём — SOLID по Circle и замкнутой плоской Polyline, цвет ACI/RGB,
  слой, связь с границей и порядок отображения. Возвращать handle Hatch.
  Открытые/неподдерживаемые границы отклонять до записи. Другие паттерны и острова
  не заявлять до реализации и проверки.
- Проверенный прототип обеих операций — scripts/network-legend.csx. Теперь
  реализованы dwg_set_lineweight, dwg_set_layer_lineweight и dwg_create_hatch:
  typed-массивы/числа, обработчики плагина, dispatch, схемы и toolset modify.
  Чтение объектов/слоёв дополнено весом; объектов — true RGB; Hatch — параметрами.
  Все цели проверяются до write, одна транзакция на вызов, без частичных успехов.
- Патч patches/dwg-mcp-lineweight-hatch.patch сохраняет 16 файлов изменений.
  git apply --check --reverse подтвердил соответствие патча текущим исходникам.
  Исходные два исправления локали/JsonElement сохранены.
- Offline: 708/708 passed (99 новых проверок). Первые попытки запуска из иных
  выходных каталогов выявили привязку upstream source-тестов к глубине bin;
  итоговый проверенный путь tests/Bimwright.Dwg.Tests/bin/StyleTools/net8.0.
  Также проверкой найдены и добавлены write-метаданные методов MCP.
- Выпуск releases/dwg-2027/2026-10-06-style-tools/: server/ — .NET 8 (0 ошибок,
  0 предупреждений); plugin/ — .NET 10 (0 ошибок, 4 прежних предупреждения
  MSB3277/SYSLIB0014). DLL собраны отдельно от предыдущей работающей версии.
- tools/check-style-mcp.py без --live через настоящий stdio SDK подтвердил
  42 инструмента и корректные схемы трёх новых вызовов. Ни одного CAD-вызова
  в этой проверке не было. py_compile нового скрипта успешен.
- Live-сценарий подготовлен: --live --document с точным именем; собственные
  GUID-слой и объекты, cleanup, запрет повторных мутаций при неизвестном исходе.
  Пользователь выполнил живую проверку 2026-10-06; результат ниже.
  Инструкции и параметры — docs/STYLE_TOOLS.md. Успешную проверку не повторять.
- Подтверждённый вывод пользователя: MCP dwg-mcp 2.0.1, TOOLS=42;
  PASS: layer/entity lineweights, SOLID circle/rectangle hatches, unchanged refusals.
  Это подтверждает работу новых нативных обработчиков загруженного плагина:
  вес слоя 0.30 мм, веса объекта explicit/ByLayer/ByBlock, белая RGB SOLID-заливка
  Circle и ACI30 SOLID-заливка Rectangle, отказ для 0.31 мм и границы Line без
  побочных изменений. Тест выполнен в Чертеж1.dwg, слой
  MCP_STYLE_71f25480e19f4658a354e41681bef402 (handle 23D).
  Созданы и удалены 23E/23F/240/242/243; REMOVED LAYER подтверждён.
  Сохранение не вызывалось. Наклонные границы, другие формы полилиний и видимый
  порядок отрисовки этим выводом отдельно не проверены. Все 42 инструмента
  не считаются живо проверенными только на основании tools/list.
- Общая регистрация Codex dwg-2027 переключена на DLL server/ этого выпуска;
  enabled=true, command/args подтверждены через codex mcp get. .mcp.json,
  README, AGENTS и ручные SDK helpers используют тот же выпуск. Текущий старый
  процесс/подключение сам по себе не подтверждает загрузку новых DLL.
- Правила переноса общих операций зафиксированы в AGENTS.md и docs/CAD_WORKFLOW.md.
- Создан глобальный C:\Users\taa6651111\.codex\AGENTS.md с требованием читать
  инструкции общего моста при CAD-задачах из любого проекта. Предыдущих глобальных
  AGENTS.md/AGENTS.override.md не было; чужие инструкции не перезаписывались.
  Запись и содержимое проверены. Следующий контекст Codex уже включил глобальную
  инструкцию. Этот этап меняет документацию, не добавляет новые MCP-инструменты.

Ниже — результаты предыдущего этапа с AutoCAD 2023, а не проверка версии 2027.

Запрос: довести до рабочего инструмента мост AutoCAD через MCP, самостоятельно
проверить инструменты, продолжить после сброса лимита при необходимости.

## Выбранная структура

- `server.py`, `cad_mcp/`: небольшой stdio MCP на официальном SDK.
- `tools/acad-bridge.ps1`: единственная реализация COM, CLI и JSON stdin.
- `lisp/`: пользовательские задачи AutoLISP.
- `tests/`: модульные, протокольные и явно включаемые проверки живой сессии.
- `AutoCAD-MCP/`: исходный импортированный репозиторий, справочный материал.
- `.venv/`: локальное окружение Python 3.12, MCP 1.30.0.

## Установленные факты

- Прямой COM внутри песочницы не видит ROT. Вне песочницы bridge status работает.
- AutoCAD 2023, ProgID `AutoCAD.Application.24.2`.
- Открыт `D:\Рабочий стол\Старосырово СХ\НВ v2.dxf`, есть несохранённые правки (DBMOD=21).
- LISPSYS=1, SECURELOAD=1. Папка проекта пока отсутствует в TRUSTEDPATHS.
- Импортированный сервер имеет 108 инструментов, но отсутствует LISP-диспетчер,
  неверно определяет приложение, кэширует старый документ, возвращает фиктивные
  успехи при headless fallback. Civil3D/Map3D/GIS не включены в рабочий набор.

## Подтверждённые результаты

- Все 17 автономных и протокольных тестов прошли.
- Реальный stdio initialize/tools/list/tools/call проверен из другой папки.
- 18 инструментов со схемами и аннотациями; структурированные ошибки.
- Вызовы COM вынесены из MCP event loop и сериализованы; во время запроса
  MCP tools/list остаётся отзывчивым.
- `cad-automation` зарегистрирован глобально в Codex CLI, с абсолютными путями
  и PYTHONUTF8=1, CAD_AUTOMATION_ROOT=D:\CAD-Automation.
- Создан hourly heartbeat cad-mcp для продолжения после сброса лимита.
- Первый полный живой сценарий прошёл: все 18 инструментов через реальный stdio
  MCP, шесть типов создаваемой геометрии, поиск и пагинация, русский текст/emoji,
  выделение, перенос/копирование/поворот/масштаб, paper-space, LISP результаты,
  синтаксические/runtime ошибки, `.lsp` файл, zoom, save и erase.
- Второй живой сценарий прошёл: старые 2D-полилинии с Elevation, 3D-полилинии,
  изменяемые/постоянные атрибуты блока, поиск в блоке, спецсимволы имени слоя,
  блокировки слоёв, отклонение UNDO One и дробных целочисленных переменных,
  настоящий групповой Undo (две созданные линии исчезают за один U).
- После последних исправлений обычный pytest: 17 passed, 2 skipped (live opt-in).
  Оба живых сценария отдельно подтверждены вне песочницы. pip check прошёл.
- Исходный `НВ v2.dxf` вновь активен: 156 объектов model-space, 0 paper-space,
  saved=false как до тестов. Рабочий файл ни разу не сохранялся.
- `.runtime` пуст: все одноразовые DWG и файлы подтверждения LISP удалены.
- setup.ps1 -SkipRegistration проверен из C:\Windows\Temp: зависимости
  воспроизводимы, импорт MCP успешен. Исправлены кавычки Python -c для PS 5.1.
- Исправлены выявленные ошибки: перечисление пустых COM-коллекций, JSON null
  в фильтре типов, сброс PICKFIRST при native SelectionSet search, координаты
  старой полилинии, постоянные атрибуты в текстовом поиске, wildcard escaping.
- Завершены замечания ревью: атомарный LISP completion marker, LISPSYS=0
  preflight, UNDOCTL One/group проверка, execution_unknown при сбое rollback,
  распознавание COM busy через InnerException, целочисленные переменные.

## Завершение

Работа завершена. MCP зарегистрирован в реальном пользовательском профиле Codex.
Песочница скрывает пользовательскую конфигурацию: внутри неё `codex mcp list`
может показывать пустой список; вне песочницы cad-automation enabled подтверждён.
Для загрузки новых инструментов нужен новый чат либо перезапуск MCP-подключения.
README.md и AGENTS.md обновлены. Heartbeat cad-mcp приостановлен после завершения.

## Практические ограничения

- Это source checkout, не самостоятельный wheel с COM-активами; при отдельной
  установке Python-пакета нужен CAD_AUTOMATION_ROOT на папку проекта.
- Требуется доступная локальная сессия AutoCAD и LISPSYS 1/2 для LISP.
- COM SendCommand может быть синхронным. Внешний таймаут 55 секунд ограничивает
  bridge-процесс, но не отменяет уже запущенный код внутри AutoCAD.
- rolled_back=true означает подтверждённое завершение U; при ошибке отката
  возвращаются rolled_back=false, rollback_error, execution_unknown=true.
- Civil3D/Map3D/GIS/ZuluGIS не заявлены как проверенные функции этого MCP.

## Прототип подготовки НВ к Zulu — 2026-10-07

- Производственная задача в чужом drawing-project выполнена через общий dwg-2027;
  upstream source, рабочие DLL и регистрацию MCP не меняли. Исходные чужие правки
  просмотрены и сохранены. Пользователь загрузил style-tools plugin через NETLOAD.
- scripts/nv-zulu7-inventory.csx: bounded full-handle inventory (500000 / 22 с),
  complete и ошибки, metadata ProxyObject/Entity, owner chains, свежая сеть/блоки.
  Это сценарий-прототип, не новый typed MCP-инструмент.
- scripts/nv-zulu7-cleanup-civil-objects.csx: explicit handles/classification,
  только nongraphical Civil ProxyObject с native erase permission, transaction.
  В НВ v6/v7 исходно2922 proxies; effectively removed2742 (75 explicit Erase,
  остальные имеют явно erased owning Civil roots). 170 Civil в AEC_DISP_REPS252
  и10 Map оставлены по прямому ответу пользователя «Оставить остаток».
- Auto-review отклонил первоначальное удаление широкого набора service roots.
  Более узкая пообъектная очистка разрешена и подтверждена; rejected сценарий
  не запускали. Read-only inspection252 выявил ещё149 native AEC definitions,
  поэтому dictionary.Remove/отрыв owner chain не выдавали за очистку proxies.
- scripts/nv-zulu7-install-noding.csx и verify-noding: project-specific plan,
  exact v6/fingerprint/geometry guards, одна transaction, без AutoLISP/COM.
  Первый vertex replacement дал eDegenerateGeometry, rollback подтверждён;
  исправленная замена сохраняет >=2 вершины. 95 исходных handles сохранены,
  добавлены32 pipe entities и24 export consumers; итог127pipes/116nodes,
  exact endpoints, no interior uncut nodes, length6756.882196m,619hoses retained.
- Typed dwg_save_drawing создал запрошенный НВ v7.dwg, v6 on-disk неизменён.
  Database.SaveAs не переименовал открытую вкладку v6; пользователь уведомлён
  открыть сохранённый v7. Это ограничение save/document lifecycle upstream.
- НЕЗАВЕРШЁННЫЙ общий контракт: typed full-DB proxy inventory (scope/budget,
  classification/owners/permission, complete) + explicit proxy-object erase
  (expected document/fingerprint/class, atomic transaction, no implicit roots).
  Network noding: explicit line/node handles, tolerances, preflight, preserve
  bends/style/ownership, split/merge map, exact endpoint validation, transaction.
  Следующий шаг: выделить tested pure geometry/preflight, затем server method,
  plugin handler/dispatch/schema, offline tests; собрать отдельный release.
  Загрузка новой DLL и live tests с отдельным согласием выполняются пользователем.
  Прототипы не означают, что эти общие typed capabilities уже доступны.

### Слои экспорта v7 / current-file save error — 2026-10-07

- Пользователь открыл НВ v7.dwg и запросил слои: PG ВК_ПГ, камеры ВК_УЗЛЫ,
  физическая сеть НВ. Выполнены typed dwg_change_layer: 35PG и28камер;
  все127pipes уже наНВ. Per-item results и readback подтверждены (46/46/127).
- Typed dwg_save_drawing с текущим уже открытым v7 output_path и
  overwrite_existing=true вернул failed to save drawing: eFilerError.
  Это известный отказ, не timeout; слои применены, повторных мутаций не было.
  Пользователю предложено Ctrl+S в v7 по правилам UI actions; подтверждение ожидается.
- Bridge gap: SaveDrawingService/Database.SaveAs current-open-DWG lifecycle.
  Не считать существование сохранённого v7 доказательством сохранения новых слоёв.
  Следующий шаг: read-only current document/DB filename/read-only/file-lock
  diagnostics (включая другие открытые документы после предыдущего SaveAs),
  затем bounded fix и offline checks; live save test только с согласия пользователя.
- Пользователь подтвердил Ctrl+S в v7: «сохранил». Запрошенная правка слоёв завершена; ошибка typed current-file SaveAs остаётся отдельным bridge issue.

### Inlets one drawing unit inside buildings — 2026-10-07

- Production task in active НВ v7, through installed dwg-2027; no bridge source,
  runtime DLL or registration changes. User clarified 1 drawing unit = 1 m;
  INSUNITS=6, authoritative building/PZU insert scales remain 1.
- scripts/nv-zulu7-read-building-contours.csx reads bounded native Hatch loops
  from the actual building block, including courtyard holes and sampled arcs.
  Area unavailable on school hatch; other 18 native areas match reconstructed
  footprints within 0.02 square units. This is a project prototype.
- scripts/nv-zulu7-place-consumers.csx changed only 24 inlet endpoints and their
  24 existing consumer blocks in one guarded transaction. Document/fingerprint,
  expected geometry, IDs/layers, unlocked layers and unit depth were checked
  before mutation. Directions and all other vertices retained. One previously
  deep consumer at garage668 was shortened from depth9.814442 to depth1.
- Native final positions: all24 inside at distance1.000000 from actual building
  boundary; endpoint gaps exactly0. Network remains127 pipes/116 nodes; length
  6768.638446820303m. No full hose/coverage recalculation for endpoint-only edit.
  User confirmed Ctrl+S in v7. Known typed current-open-file SaveAs error was
  not repeated; both save branches use Database.SaveAs.
- General typed gap extends the earlier network-noding contract: guarded atomic
  polyline endpoint edit plus corresponding block/attribute displacement, with
  exact endpoint verification. No claim of a released reusable typed tool.
  Next bridge step remains bounded endpoint/noding API, schema/offline checks,
  separate release and user-enabled loading/live checks.
