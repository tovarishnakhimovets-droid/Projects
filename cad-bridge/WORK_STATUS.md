# CAD bridge — состояние работы

## v3.6 собран из andrew без merge — 2026-10-29

- Пользователь прямо разрешил сборку до merge с накоплением исправлений.
  CAD_WORKFLOW/TEAM_SETUP обновлены; коммит 10ff6c11ea5c834ecdf9b210b329bc9746d84ad0
  отправлен в origin/andrew. Из его чистого checkout без переключения ветки
  собран releases/3.6/DwgMcp.Setup-v3.6-win-x64.zip. Merge агент не выполнял.
- Manifest: version3.6, commit10ff6c1, dirty=false, годы2026/2027. Все40 файлов
  stage сверены по SHA256. EXE и обе native DLL3.6.0.0, Product3.6.0+10ff6c1.
  ZIP SHA256: b6921ec0bf9965bd9b3eee33712ced6c275b260b1373ad0bc32e978df438ab7e.
- Настоящий MCP SDK запустил packaged EXE для обоих target years и режимов:
  advertised version3.6, standard79/read-only32, planner присутствует, writer
  исключён из read-only, default dry_run и required guards верны. Только
  initialize/tools-list, ни одного CAD-вызова.1910 успешных unit/SDK тестов и обе
  native-сборки из записи ниже действительны; ради этого итога они не повторялись.
- Установлена/загружена покаv3.5. Native исправление прозрачности ещё не проверено;
  геометрия обрезки, отверстия и Undo не подтверждены. Следующее: пользователь
  сохраняет рабочие файлы, закрывает тестовую копию без сохранения и устанавливает
  v3.6; затем повторяем те же8 явно разрешённых случаев в тестовой копии.
  Source/receipts/logs НК2 остаются по своим существующим путям.

## Native-проба v3.5: остановка planner; исправление прозрачности готово — 2026-10-29

- Пользователь явно разрешил8 проверок в __MCP_TEST_CLIP_NK2.dwg без сохранения.
  Реально loaded Acad27 DLL3.5.0.0 +d8b712a, AutoCAD PID25632. Fresh target/path
  и fingerprint проверены; исходная модель677 entities. create_entities dry-run
  прошёл, затем26 контрольных объектов созданы одной транзакцией (703 total).
- Первый plan_hatch_clip (keep,51F75B внутри маски51F75C) вернул eInvalidKey.
  clip_hatches не вызывался, остальные7 случаев не запускались. AutoCAD отвечает.
  Все26 созданных объектов и count703 подтверждены повторным чтением именно
  тестового документа после явной фиксации его document_id. Пользователь между
  чтениями переключил активную вкладку на Поверхности; ошибочный follow-active
  readback другого документа не принят за удаление объектов теста.
- В AppearanceSignature найдено безусловное Transparency.Alpha для ByLayer/ByBlock,
  хотя Autodesk разрешает alpha() только для kByAlpha. Исправлено: отдельный
  снимок режима с ленивым чтением Alpha только при IsByAlpha, без подмены
  наследуемой прозрачности числом и без изменений DWG/системных переменных.
  Тесты доказывают отсутствие вызова запрещённого getter для inherited/invalid,
  точность явных alpha и различие guards ByLayer/opaque. Handler теперь пишет
  стек native ошибки в существующий PluginLog, чтобы дальнейший отказ локализовался.
  В v3.5 стек был потерян обработчиком; полный native путь ошибки ещё не доказан.
  Основание: https://help.autodesk.com/cloudhelp/2019/ENU/OARX-RefGuide/files/OREF-__MEMBERTYPE_Methods_AcCmTransparency.html
- Перед правками: clean andrew/origin-andrew ba0c3f6, develop/origin-develop d8b712a,
  один worktree, незавершённых операций нет;3 FF-команды и ancestry check прошли.
  Полный suite1910/1910, actual SDK/schema в составе; обе Release-сборки прошли:
  Acad27 — 0 ошибок/2 известных Roslyn warnings; Acad26 — 0 ошибок/0 предупреждений,
  compile-only AutoCAD.NET25.1.0. git diff --check прошёл.
- Это source/offline исправление. Установлена v3.5; исправление в AutoCAD ещё
  не проверено, пакет3.6 не собран. Пользователь затем разрешил собирать без merge
  из чистой личной ветки: накапливаем несколько исправлений, сливаем позднее.
  Коммит/push выполняет агент; следующая сборка из clean andrew и повтор тех же проб.
  26 временных объектов остаются только в открытой тестовой копии; закрыть без
  сохранения. Ни рабочие Поверхности, ни Очередность агентом не изменены/не сохранены.
  Подробные receipts — проектный НК2/hatch_clip_result.json; definition/fixture —
  НК2/hatch_clip_checks.json. Undo, отверстия и native clipping пока не подтверждены.

## v3.5 собран; merge остаётся пользователю — 2026-10-29

- Новое прямое уточнение пользователя заменяет предыдущее поручение в части
  merge: коммиты/push и подготовка PR остаются агенту без повторных разрешений,
  обычные и PR-слияния выполняет пользователь. AGENTS/TEAM_SETUP/CAD_WORKFLOW
  приведены к этому правилу. GitHub CLI/плагин не является обязательным:
  при отсутствии API публикуется ветка и предоставляется compare link.
- Агент сделал и отправил andrew: 3474776 (правила Git) и 48c0244 (обрезка HATCH).
  Пользователь выполнил merge PR#16. Fetch подтвердил develop
  d8b712acc7ec80b4c38226ff792ff9af7713b6c1; содержимое кода совпало с проверенным.
  Личные/локальные ветки продвинуты fast-forward, обычных merge агент не выполнял.
- Из чистой develop d8b712a собран releases/3.5/DwgMcp.Setup-v3.5-win-x64.zip.
  Manifest dirty=false, годы2026/2027, версии обеих DLL3.5.0.0 +d8b712a;
  все40 файлов stage сверены по manifest SHA256. ZIP SHA256:
  2e93049188265c870802616bbdf3c296629a13c68b1878cc9010bb4c7bb7e847.
- Готовый packaged dwg-mcp.exe проверен через настоящий Python MCP SDK:
  initialize/tools-list для2026/2027, стандартный профиль79, read-only32;
  advertised version3.5, plan_hatch_clip присутствует, writer отсутствует
  в read-only, default dry_run=true и required guards верны. CAD-tools не вызывались.
  Прежние1903 теста и обе native-сборки остаются действительными; docs-only
  уточнение повторных тестов или пересборки пакета не требует.
- Установщик не установлен, AutoCAD не перезапускался, native обрезка/Undo/rollback
  ещё не проверены. Пакет immutable, собран до этого docs-only уточнения merge;
  текущий AGENTS и прямое указание пользователя имеют актуальное правило.
  Сама правка правил/этот итог публикуются в andrew для следующего ручного merge.
  Следующее: пользователь устанавливает v3.5, затем отдельная разрешённая проба
  обрезки в тестовом DWG. Рабочие Поверхности и Zulu не менялись и не сохранялись.

## Постоянное поручение Git агенту — 2026-10-29

Пользователь прямо поручил весь Git агенту. Убрана передача commit/push/PR/merge
человеку из AGENTS, TEAM_SETUP и CAD_WORKFLOW. Агент ведёт полный Git-цикл
порученной работы без повторных подтверждений; чужие изменения сохраняет,
неоднозначные конфликты и вход в аккаунт разбирает по фактической необходимости.
Это продолжение подготовленной доработки обрезки HATCH; повторный pull на грязном
checkout не запускался. Существующие 1903 успешных теста и обе сборки не повторяются
ради изменения правил. Следующее действие — коммиты, публикация, принятие в develop
и сборка v3.5 с уже подготовленным кодом; native-проба остаётся отдельным этапом.

## Обрезка HATCH для НК2 — подготовлена в исходниках, не выпущена — 2026-10-29

- По поручению пользователя обрезать покрытия по очередям выбрана новая typed
  операция: сохранение дуг/отверстий, preview с площадями, guarded native запись
  и source→output handles требуют собственного контракта. Ручное смещение
  проектных границ не применяется. Код только в shared/Hatches; контракт в TOOLS.
- До первых правок проверены чистая andrew с origin/andrew, develop с
  origin/develop, единственный worktree и отсутствие незавершённых Git-операций.
  Обе ветки e2f2194; три разрешённых FF-команды выполнены по AGENTS, ancestry
  develop→HEAD подтверждён. В середине работы pull/переключений не было.
- Добавлены dwg_plan_hatch_clip (query) и dwg_clip_hatches (modify, default dry-run),
  full identity/session/geometry hash, native Region intersection и BREP loops,
  одна транзакция, сохранение оформления/порядка, readback площадей и геометрии
  до удаления источников и перед commit. Native входы — только unopened transient
  curves; DwgOut/custom filers отсутствуют. Инструмент не сохраняет DWG.
- create_entities дополнен optional Polyline bulges для точного переноса масок;
  его HATCH creation по-прежнему запрещает curved polyline boundaries.
- Проверки: полный suite 1903/1903, включая реальный MCP SDK/schema и stdio
  profiles; стандартный профиль79 tools, read-only32, query23/modify37.
  После последних native-only правок обе сборки повторены успешно:
  Acad27 Release — 0 ошибок/2 существующих Roslyn conflict warnings;
  Acad26 Release — 0 ошибок/0 предупреждений, compile-only AutoCAD.NET25.1.0.
  git diff --check прошёл. Это offline результаты, геометрия в AutoCAD не доказана.
- Нет коммита/push/merge, нового установщика, установки или live-прогона.
  Установлена прежняя v3.4. Native HATCH clipping, clone appearance/BREP ownership,
  отверстия, касания, rollback и один Undo ещё требуют отдельного явно разрешённого
  прогона в тестовом DWG. Подготовлены8 случаев/26 объектов в проектном
  НК2/hatch_clip_checks.json. Не запускать первый тест на Поверхности.dwg.
- Следующий шаг: принять diff и включить в develop по TEAM_SETUP, выпустить
  следующую версию с обоими плагинами, установить; затем разрешённый native тест.
  После него — перенос7 масок и малые пакеты обрезки. Школа содержит36HATCH
  внутри local block; новый clip не обходит вложенные блоки и не считает их
  обработанными. Проектный план/состояние — НК2/queue_masks.json и WORK_STATUS.md.

## v3.4 загружена и проверена в НК2 — 2026-10-29

- Пользователь подтвердил установку. EXE и реально loaded Acad27 DLL3.4.0.0,
  Product3.4.0+44ccffc; серверPID21788, AutoCADPID33876. Manifest находится
  server/current/bridge-docs/release.json.
- По отдельному разрешению выполнен один bind-current газона4 в подготовленном
  __MCP_TEST_BIND_NK2.dwg. completed/postcondition_verified=true, xref30→29,
  source_revision0/preflight_revision1; вставка/placement/XCLIP сохранены.
  В definition видны все5известныхHATCH; полный reader остановлен1MiBлимитом.
  SHA sourcexref до/после совпал. Тест закрыт пользователем без сохранения.
- Продолжено исходное поручение на Поверхности.dwg:13из14оставшихсяxref
  внедрены.12имеют completed receipt/locality/placement readback и сверенный
  decrement графа. УВК2(383219) вернула read timeout, затем connect timeout;
  запись не повторялась. После восстановления полный reader подтвердил1xref
  (только УВК1350); вставка УВК2 на месте с прежними owner/layer/transform.
  Native receipt13-го bind отсутствует; readback состояния соответствует Bind.
- УВК1350(382FCB): eNotAllowedForThisProxy, metadata readback всё ещёxref,
  placement сохранён, complete rollback не доказан. Пользователь подтвердил
  аналогичный ручной отказ. Нет повторного native вызова этой ссылки.
  PID33876 жив, связь восстановилась, новых WER о падении не найдено.
- Нынешний explode plan на уже local газоне4 отказывает до записи:
  block XData would be lost by explode. XData readers отсутствуют; не стирать
  данные/не менять scale ради обхода. Раскрытие/экспорт HATCH ещё не выполнены.
  Поддержанная промежуточная очистка определений:213MTEXT удалены по свежим
  handles (подписи посадок/МАФ),213/213ok; exact нетекстовые handles сохранены,
  включая2HATCH и17LINE второго определения.
- Drawing receipts и подробный текущий этап: НК2/cleanup_attempt.json и
  НК2/WORK_STATUS.md. Никаких bridge source edits, rebuild, Git mutations,
  saves или повторов uncertain write в этом проходе. Single Undo не проверен.
  Следующий шаг: согласованное ручное раскрытие блоков либо bounded доработка
  после отдельного решения пользователя; проблемную школу разбирать отдельно.


## v3.4 собран и проверен — 2026-10-29

- По явному поручению пользователя первый локальный коммит исправления:
  44ccffc9da3e64196cf1838116ed7bc2e2ac1908, ровно23согласованныхфайла.
  Перед коммитом убрана только лишняя пустая строка EOF в новом тесте;
  логика после1878PASS не менялась, успешные unit checks не повторялись.
  Перед упаковкой и после неё source tree чистое.
- Официальный package-team-dwg.ps1 -Version 3.4 создал releases/3.4/stage
  и DwgMcp.Setup-v3.4-win-x64.zip: 40 060 533байт, SHA256
  3bc67a2b18bd5b371648f974bd7fd6495b80fa1d930cdf2a5c5cef6b0d82771b.
  Manifest version3.4, commit44ccffc..., dirty=false,40файлов,2026/2027,
  self-contained server. Прежние выпуски не изменялись.
- Проверены размеры/SHA всех40файлов, полный набор и каждый байт ZIP,
  sidecar SHA, release.json, XML и точные canonical bridge-docs.
  Packaged EXE прошёл6actual MCP SDK initialize/tools-list profiles:
  standard77/query22/read-only31 для2026 и2027, server version3.4.
  Новый bind_current_xref присутствует только в модифицирующем профиле,
  annotations/defaultdry_run/identity guard schema проверены.
  Ни одного CAD-вызова в package verification.
- Independent verification: обе DLL892928байт, PE3.4.0.0,
  Product3.4.0+44ccffc..., XML AppVersion3.4.0, ровно2components R25.1/R26.0,
  Win64 AutoCAD|Civil3D, правильные module paths. PE metadata без исполнения
  target DLL подтверждает отсутствие AssocNetworkSnapshot/LockBoundaryProof
  TypeDefinitions и DwgFiler TypeReference в обеих DLL; новые bind-current
  handler/input присутствуют.
- Независимый baseline installed3.3 не изменился: SHA installed EXE,
  bridge-docs/release.json и фактической Roaming-bundle Acad27DLL совпали
  с контрольными значениями до упаковки. Установка не выполнялась.
  Последняя подтверждённая native-сессия —3.3; новые DLL не загружались
  этим процессом работы, live-проверок3.4 не было.
- Проверочный результат — НК2/release_3_4_check.json; реализация прежняя
  НК2/check_release.py. Никакого второго моста в проект не копировали.
  Intermediate releases/3.4/plugin-build удалён после проверки exactpath,
  отсутствия reparse points и загруженных CAD/MCP modules из этого дерева;
 120временныхфайлов удалены, готовые stage/ZIP/SHA сохранены.
- Второй разрешённый локальный коммит содержит только эту запись результата
  в WORK_STATUS.md. Пакет ради неё не пересобирается: его manifest остаётся
  привязан к первому чистому коммиту44ccffc.
- Push, merge, установка, AutoCAD-тесты и DWG-save не выполнялись.
  Следующий этап после установки — отдельно разрешённый один bind-current
  на __MCP_TEST_BIND_NK2.dwg с fresh identity/metadata/dryrun и проверкой
  local definition/placement/HATCH/source-file. Не переходить к рабочему
  Поверхности.dwg до подтверждённого результата. Undo/native Bind ещё
  не объявляются проверенными.


## Подготовка выпуска v3.4 — 2026-10-29

- Пользователь явно поручил два локальных коммита: подготовленные23файла
  исправления, затем только итог выпуска в WORK_STATUS.md; между ними —
  официальный пакет3.4. Push, установка и CAD-тест в поручение не входят.
- Код после1878PASS, двух финальных native-сборок и independent review
  не меняется; успешные проверки не повторяются. Перед фиксацией сверены
  согласованные23пути, matching upstream и отсутствие unfinishedGitoperations.
  FF-preflight выполнен до этой доработки, обновление в её середине не требуется.
- Следующий шаг: пакет из чистого первого коммита, проверка bytes/manifest/PE/XML
  и packaged MCP initialize/tools-list, затем второй documentation commit.
  Нативная проверка нового bind_current_xref пока не выполнялась.


## Исправление после фатала v3.3: исходник и offline готовы — 2026-10-29

- По продолжению исходной задачи подготовки поверхностей НК2 убран
  custom DwgOut/FileFiler путь. Удалены AssocNetworkSnapshot, LockBoundaryProof
  и их test double/устаревшие tests. Удаление объявлено заранее; runtime path
  проверен, установленная DLL3.3.0.0 не менялась. История исходника сохранена
  в прежних Git-коммитах, параллельных реализаций/архивов не создано.
- До правок чистая andrew0faa379, upstreamorigin/andrew, единственныйworktree,
  local develop свободна, unfinished operations нет. Три FF-обновления
  выполнены без изменений, ancestry develop→HEAD PASS.
- Старые manage_xrefs/explode_blocks снова требуют строгой source revision
  до/после write lock. Параметры не переписываются, rawcounter не подавляется.
- Добавлен отдельный bind_current_xref: bind-only whole current root,
  default dry_run=true/read lock. Для real — strict source session/revision
  до acquisition, затем полный свежий graph/reference/transform/XCLIP/status/
  layer preflight под write lock. Все guard-поля кроме source revision
  совпадают. Свежая revision проверяется до transaction и после её старта,
  непосредственно перед native Bind/Detach в общей реализации.
  Нет нового serializer, callback bridge или долгоживущего lock lease.
- Native BindXrefs(false), placement/locality readback и bounded uncertainty
  handling остаются единственными общими реализациями. Receipt содержит
  semantics=current_root_definition, source/preflight revisions и флаг
  свежей проверки под lock; равенство всей network/DB не обещается.
- Independent review нашёл два реальных обхода: вложенный batch обходит
  Invoker, а StartTransaction вызывает reactors после прежнего finalguard.
  Batch теперь отклоняет currentbind до любых ранних items, baked whitelist
  проверен регрессией; повторный guard внесён внутрь native transaction.
  Финальное independent review: no actionable issues.
- Итог полного набора: 1878 PASS, 0 FAIL, 0 SKIP, включая20 actual MCP SDK
  stdio profiles без CAD. Новая surface: standard77/query22/read-only31/
  modify36. Локальные wrapper errors не доходят до CAD.
  Два первоначальных targeted failures были неверными test-ожиданиями
  текста ошибок document/fingerprint; исправлены, полный финал PASS.
- Server net8.0 собран; финальные native2026(pinned25.1.0) и native2027
  собраны после transaction-guard patch: 0 errors, у2026 0 warnings,
  у2027 прежние2Roslyn MSB3277. Ни одна DLL не загружалась в CAD.
- TOOLS обновлён. Единственный НК2/check_release.py сохранён и расширен
  version-aware counts: прежние пакеты76, начиная3.4 ожидается77/currentbind.
- Подготовлена отдельная __MCP_TEST_BIND_NK2.dwg рядом с host в НК2,
  чтобы сохранить относительные xref-пути:3678424байт, SHA256
  60aa92270a6fe3bbae7e54e85e024c132730789e9578af0c2e3cc6401015082d.
  Стабильные source hash до/после чтения и copy hash совпали. Обычный
  Get-FileHash не открыл активный DWG из-за file sharing; read-only
  FileStream с ReadWrite/Delete sharing подтвердил точные байты.
  Copy в AutoCAD не открывалась, в рабочем DWG ничего не менялось.
- Временный НК2/remove_unsafe_filer.py после применения удалён.
- Состояние: исходник/offline ready; installed/loaded остаётся3.3 с прежним
  запретом real writers. Новых коммитов, пакета, установки, native-теста
  и DWG-save не было. Следующий шаг — отдельное поручение фиксации изменения
  и выпуска3.4. После установки только явно разрешённый тест на отдельной
  копии; к рабочему DWG не переходить до подтверждённого результата.
  Detach/explode при lock events по-прежнему могут отказывать: новая
  bind-only семантика на них автоматически не распространена.


## ФАТАЛ v3.3: real writers не применять — 2026-10-28

- v3.3 установлен пользователем. Активный Поверхности.dwg подтверждён
  по полному пути/fingerprint739091b3..., DBMOD0. Fresh list30rootmetadata
  complete, dry-run Bind3273CB PASS. Один real вызов вернул через17секунд
  plugin closed connection; пользователь подтвердил fatal и перезапустилCAD.
  Автоматического повторения Bind/Explode/Erase/Save не было.
- WER: acad.exe23932, 22:07, exception0xc0000005, address0x7ff81f4139e0.
  Дамп C:/Users/taa6651111/AppData/Local/CrashDumps/acad.exe.23932.dmp,
  55 829 217байт. Сохранённый dump только читается, на сервер не отправлялся.
- Microsoft dotnet-dump10.0.750501 установлен локально только в НК2/tools.
  ip2md(exceptionAddress) => System.Runtime.InteropServices.GCHandle.get_Target.
  Разрешённые адреса стека образуют цепь Matches→Capture→DBObject.DwgOut→
  native AcMgDwgFiler.dwgVersion→gcroot<DwgFiler>→GCHandle. Сбой локализован
  в custom filer path второго снимка. Полный native unwind не получен;
  точная причина invalid callback context пока не установлена.
- Independent PE/IL review installed acdbmgd: AcMgDwgFiler callbacks читают
  gcroot из native this+0x40; ctor создаёт сильныйhandle, destructor освобождает.
  Добавление KeepAlive вслепую не исправление. Также native dwgOut при error
  status может ASSERT: throws/InvalidInput из callbacks не гарантируют
  безопасного отказа. Это дополнительный риск, не доказанная причина данногоAV.
- После restartPID28544 только readers: то же Поверхности.dwg, DBMOD0,
 30xref, все root graph/reference/identitymetadata совпали с pre-call.
  Receipt упавшего вызова отсутствует, факт входа nativeBind не подтверждён.
  Состояние до завершения упавшего процесса не объявляется откатанным.
- Реальные manage_xrefs и explode_blocks v3.3 используют этот общий helper:
  НЕ ПОВТОРЯТЬ до исправления и отдельной native-проверки. Dry-run/metadata
  readers на helper не заходят; доступность после restart подтверждена.
  Пакет3.3 immutable не перезаписывать/не рекламировать как рабочий для записи.
- Следующий путь: убрать custom filing из production полностью. Для Bind
  рассмотреть явный bind-only контракт текущего root: strictprelockrevision,
  fresh full target/graph/transform/layer/status preflight под полученным lock,
  затем nativeBind+placement readback. Semanticsдолжны бытьявными; не переносить
  эту политику на detach/explode автоматически и не компенсировать+3.
  Любой исправленный native путь сначала проверить в отдельном тестовом DWG
  по явному разрешению, не на рабочей копии.
- Новый код/пакет/коммиты не выполнялись. В source только накопленная запись
  WORK_STATUS.md; новый bridge-edit preflight требует чистого дерева.
  Для её фиксации нужно отдельное поручение по правилам пользователя оGit.
  Диагностика объекта — НК2/cleanup_attempt.json и fatal_v3_3_*.txt.


## v3.3 собран и проверен — 2026-10-28

- По явному поручению пользователя согласованные12файлов зафиксированы
  в andrew: b87e4f302fd37dded22014a250d97609153968bf. Перед упаковкой
  рабочее дерево чистое; успешные1940logic/unit checks не повторялись.
- Официальный package-team-dwg.ps1 -Version 3.3 создал releases/3.3/stage
  и DwgMcp.Setup-v3.3-win-x64.zip: 40 073 048 байт, SHA256
  81323f7f33941cbd0aee63a84abdf2936d468a02242235f6b115c4a3b305e212.
  Manifest version3.3, commit b87e4f3..., dirty=false, оба года2026/2027,
  40файлов; предыдущие версии не изменялись.
- Проверены SHA/размеры всех40файлов, полный набор и каждый байт ZIP,
  sidecar SHA, runtime release.json и точные canonical bridge-docs.
  Упакованный EXE прошёл6actual MCP SDK initialize/tools-list profiles:
  standard76/query22/read-only31 для каждого года, server version3.3,
  writer visibility/annotations/identity/dry_run schema корректны.
  Проверки используют только MCP discovery, ни одного CAD-вызова.
- Independent verification: обе DLL PE3.3.0.0 и Product3.3.0+b87e4f3,
  XML AppVersion3.3.0, ровно2components R25.1/R26.0, Win64 AutoCAD|Civil3D,
  module paths2026/2027 правильные. Установленный3.2 не изменился:
  SHA EXE, release.json и фактической Roaming-bundle Acad27DLL совпали
  с baseline, снятым независимым агентом перед упаковкой.
- Промежуточный releases/3.3/plugin-build удалён после проверки точного
  absolute path внутри release workspace, отсутствия reparse points
  и loaded CAD/MCP modules оттуда. Готовые stage, ZIP и SHA сохранены.
- Единственная QA-реализация — НК2/check_release.py, результат —
  НК2/release_3_3_check.json. В папку объекта мост не копировался.
- Source/offline-package verified. Установлен/загружен по-прежнему3.2;
  push, merge, установка и CAD-save не выполнялись. Новый gate и native
  DwgOut(FileFiler) на рабочем DWG ещё не проверены; Bind/Explode не выполнены.
  Следующий шаг: пользователь закрывает AutoCAD/Codex, устанавливает3.3,
  затем fresh identity/metadata, dry-run и один защищённый рабочий вызов.
  При unknown/timeout читать состояние, автоматически не повторять запись.
- Этот итог добавлен после выпуска без новой Git-фиксации: отдельный коммит
  журнальной записи не входит в делегированный набор12файлов. Пакет не
  пересобирался, manifest остаётся привязан к чистому коммиту b87e4f3.


## Подготовка выпуска v3.3 — 2026-10-28

- Пользователь отдельно поручил «Сделай коммит и v3.3»: согласованные12файлов
  фиксируются в текущей andrew; push, merge и установка не входят в поручение.
- Код после1940PASS и двух native-сборок не меняется. Проверки не повторяются;
  официальный упаковщик задаст версии3.3/3.3.0, предыдущие выпуски не меняются.
- Перед фиксацией сверены все12путей, matching upstream, одинworktree,
  unfinished Git operations нет. FF-проверки уже выполнены до этой доработки;
  в середине задачи новые pull/fetch не запускаются.
- После коммита: package-team-dwg.ps1 -Version 3.3 из чистого снимка, проверка
  manifest/ZIP/metadata и packaged MCP initialize/tools-list без CAD calls.
  Результат и состояние installed/loaded/live будут записаны отдельно.

## Native v3.2 и проверка сохраняемого состояния network — 2026-10-28

- Пользователь сообщил «обновил». Установленные release.json и EXE подтверждают
  3.2/c51cdee; native reader возвращает новую revision_diagnostics, сессия новая.
  Активен прежний Поверхности.dwg, fingerprint739091b3..., DBMOD16.
- Dry-run Bind газона4 (3273CB) PASS, revision0. Один разрешённый production
  вызов отказал до handler: acquire +1, dispose +2, всего revision3.
  Все события modified точного managed AssocNetwork3A8732, шесть false-флагов;
  trace полный, unrecorded/capture failures0. После отказа те же30rootxref,
  graph/reference hashes и DBMOD16. Bind/Explode/Erase/Save не выполнялись.
  Диагностика сохранена в существующем НК2/cleanup_attempt.json.
- Native событие связано с автоматической associative evaluation на смене lock.
  Флаги сами по себе не доказательство no-op. Подготовлен узкий gate:
  rawcounter прежний, strict guard до acquisition; только полный all-false
  exactnetwork trace может быть проверен полным DwgOut(FileFiler) сравнением
  сохраняемых данных default hierarchy до/после. Набор/root/nativeclass/bytes
  должны совпасть. Любые child/structural/unknown events, ошибки и лимиты —
  отказ. Чужие/unlinked networks не объявляются проверенными.
- Только после доказанного равенства меняется revision в приватной копии входа;
  handler повторяет все прежние object guards. События release не подавляются.
  Пределы capture128networks/10000actions/2MiB/3s, без файловой записи.
- Перед исходными правками: чистая andrew2a6a5b6, matching upstream,
  develop свободна, unfinished operations нет. Разрешённые FF-команды выполнены,
  обновлений нет, ancestry PASS. Другая работа не затронута.
- Итог: 1940 .NET tests PASS, 0 FAIL, 0 SKIP, включая 20 actual MCP SDK
  stdio/schema profiles без CAD calls. 158 targeted gate/routing tests PASS.
  Native2026 с pinned25.1.0: 0 warnings/errors; native2027 installed refs:
  0 errors, прежние2Roslyn MSB3277. Server net8.0 собран в полном наборе.
  Independent review всех4productionfiles и tests: no actionable issues.
- Первые12 routing tests упали из-за test double, который не обновлял
  LastLockMode после нового read-lock capture. Модель явного Write исправлена;
  проверки восстановления исключений/строгого отказа сохранены, финал PASS.
  Native helper компилировался с реальными Autodesk refs; unitrouting использует
  snapshot double. Реальные callbacks/успешность и равенство DwgOut hashes
  пока НЕ проверены на живом DWG.
- Технический контракт TOOLS обновлён. Временный проектный patch script удалён
  после применения; единственный исходник остаётся в общем CAD-мосте.
- Установленный/загруженный runtime остаётся3.2. Новый коммит, пакет, установка,
  повторный live writer и сохранение не выполнялись. Следующий шаг после offline
  итогов — отдельное поручение коммита и пакета, затем загрузка пользователем.
  DwgOut callbacks/hashes на этом DWG и production Bind пока live не проверены.


## Диагностический v3.2 собран и проверен — 2026-10-28

- По явному поручению пользователя диагностический патч зафиксирован в andrew:
  c51cdee20d5fb57a6fae9c3f0c15da198f22843c. Только согласованные 12 файлов;
  перед упаковкой исходник чистый. Logic/unit проверки1824PASS не повторялись.
- tools/package-team-dwg.ps1 -Version 3.2 создал releases/3.2/stage и
  DwgMcp.Setup-v3.2-win-x64.zip: 40 054 901 байт, SHA256
  7e14c9d417c021c76a35f3860356ce026c2f15a287601ff6c5feaabf3f57d38f.
  Manifest: version3.2, commit c51cdee..., dirty=false, годы2026/2027.
  Предыдущие выпуски не перезаписывались, новый пакет не переиздавался.
- Проверены bytes/SHA256 всех40файлов manifest, полный набор и байты ZIP,
  sidecarSHA, release.json, точные копии канонических bridge-docs.
  Обе plugin DLL имеют PE-version3.2.0.0, Autodesk PackageContents AppVersion3.2.0,
  ровно два компонента2026(R25.1)/2027(R26.0), Win64, AutoCAD|Civil3D.
- Упакованный EXE проверен official MCP SDK: только initialize/tools-list,
  version3.2; для обоих годов standard76/query22/read-only31, всего6profilesPASS,
  новые readers/writers/annotations/identity/dry_run-schema корректны.
  Ни одного CAD-вызова, runtime-install или DWG-save в этой проверке не было.
- Независимая проверка подтвердила PE/XML/release metadata и неизменность
  установленного3.1 по прежним SHA256 сервера, release.json и Acad27 DLL.
  Native-сборки успешны, у2027 только прежние2Roslyn MSB3277. Intermediate
  plugin-build удалён после проверки точного пути и загруженных модулей;
  stage,ZIP,SHA сохранены. Пакет не пересобирается ради записи этого итога.
- Служебная QA-реализация в НК2 обобщена и переименована из
  check_release_3_1.py в check_release.py: версия — positional argument,
  --commit задаёт ожидаемый коммит manifest. Результат — release_3_2_check.json;
  прежний JSON3.1 сохранён. Второй реализации проверок не создано.
- Source-ready/offline-package verified; установлен/загружен по-прежнему3.1.
  Push,merge,установка и production-очистка не выполнялись. Причина +3native
  lock-events ещё не установлена. Следующий шаг пользователя — установить3.2,
  затем получить diagnostic trace при одном защищённом рабочем вызове;
  неизвестные события всё ещё вызывают отказ, а не подавляются.
## Подготовка диагностического v3.2 — 2026-10-28

- Пользователь явно поручил «Сделай коммит и v3.2» после описания результата
  1824 офлайн-тестов и ещё неизвестных причин lock events. В поручение входят
  фиксация подготовленного патча в andrew и сборка диагностического пакета.
- Новых исходных правок логики не делается, успешные unit-тесты не повторяются.
  Сборка пакета задаст версии 3.2/3.2.0; предыдущие3.1/3 не перезаписываются.
  Это не подтверждение работы Bind/Explode в AutoCAD. После загрузки новой DLL
  предстоит один защищённый рабочий вызов и чтение типов/флагов событий.
- Push, merge, установка и сохранение рабочего DWG не поручены и не выполняются.
  Фактический итог сборки/проверок будет добавлен отдельной записью журнала.
## Первый native dry-run v3.1 и диагностика блокировки — 2026-10-28

- Пользователь удалил явно лишние ссылки вручную и поручил внедрить и
  очистить остальные. В установленном release.json версия 3.1; новые native
  handlers доступны. В рабочем Поверхности.dwg подтверждены 30 resolved/loaded
  корневых ссылок без подрезок. Служебные данные: НК2/xrefs_before_cleanup.json
  и НК2/cleanup_attempt.json. Сводка прямых объектов неполна из-за лимита
  100 000; 417 найденных HATCH — нижняя граница, не реестр готовых поверхностей.
- Первый ManageXrefs(bind,dry_run=true) отклонил свежий guard; немедленный
  повтор только проверки с новым snapshot также отклонён. Контроли:
  list→list под read lock стабилен, любой проверенный write-lock путь даёт
  revision +3: Manage dry-run, отказ InspectBlock на xref, успешный GetVariables.
  Graph/reference hashes остаются прежними. Классы/флаги событий неизвестны;
  компенсировать +3 или игнорировать события целиком нельзя.
- Команды реального Bind/Detach/Explode/Erase/Save не запускались.
  DBMOD исходно 16 после пользовательской очистки, при последующем чтении 17;
  никакой вывод о типе внутренних изменений из этого бита не делался.
  У двух ссылок третьей очереди scale1000/1000/1: текущий guarded explode
  их отклоняет, масштаб ради обхода не менялся.
- Перед исходными правками чистая andrew fb374e3; CAD develop notLoaded,
  unfinished Git operations нет, три ранее разрешённые FF-команды выполнены,
  без обновлений; ancestry develop→HEAD PASS. Чужие файлы не менялись.
- Подготовлено исправление dry_run: request-aware read lock только для
  ManageXrefs/ExplodeBlocks с default/true. Реальный writer проходит shared
  input parser и точный session/revision guard до и после получения write lock.
  При изменении на lock handler не вызывается, ошибка показывает фазу/events.
- DrawingRevision сохраняет прежние счётчики и исключения. Диагностика читает
  native flags только на потоке активного BeginPhase, максимум 16 снимков
  на scope и 16 записей в ring. Вне scope/бюджета/потока — без новых getters,
  counter всё равно растёт. unrecorded_count/truncated обозначают пропуски.
  Xref reader возвращает диагностику, отказ guard — changed_fields.
- Независимое ревью выявило потерю primary outcome при ошибке lock.Dispose.
  Исправлено: сохраняются primary exception и bounded handler receipt,
  handler_invoked/returned, execution_unknown и запрет автоматического повтора.
  Native-ссылки и большие payload в ошибку не сериализуются. Ревью PASS.
- Итог: 1824 .NET tests PASS, 0 FAIL, 0 SKIP, включая 20 actual MCP SDK stdio
  profiles без CAD calls. Ранее два новых теста требовали обновить ожидание
  unscoped diagnostics после performance policy; итоговый полный запуск успешен.
  Server net8.0 PASS; native2026 pinned25.1.0 PASS без warnings/errors;
  native2027 installed refs PASS, прежние два Roslyn MSB3277, ошибок нет.
- Состояние: исправление и диагностика только в исходнике и offline binaries.
  Новый коммит/пакет не создавался, installed/loaded3.1 не менялся; повторный
  native прогон с диагностикой ещё не выполнен. Это не завершённый production
  fix реального Bind: нужен trace классов/флагов lock events после загрузки.
- Следующий шаг: отдельное поручение коммита/диагностического выпуска v3.2,
  установка пользователем, одно чтение и dry-run, затем явный рабочий вызов
  с защитой и анализом trace. До доказанной классификации real writer сохраняет
  отказ. Выпущенный3.1 не перезаписывать. Производственная очистка не выполнена.
## v3.1 собран и проверен — 2026-10-28

- Пользователь поручил коммит доработки и сборку v3.1. Коммит реализации:
  fabf3c3d9d4c9eb8f883eb3f0e7035ca52598e50, ветка andrew. В него вошёл
  только подготовленный набор из 21 файла. Перед упаковкой исходник чистый.
- tools/package-team-dwg.ps1 -Version 3.1 создал releases/3.1/stage и
  DwgMcp.Setup-v3.1-win-x64.zip: 40 045 879 байт, SHA256
  d273e52a0056b8586a8c7c440ceb1d76e098513512a6881aa5284d8f4cb9906e.
  manifest.version=3.1, manifest.commit=fabf3c3..., dirty=false,
  оба года 2026/2027. Готовый пакет не перезаписывался.
- Проверены bytes/SHA256 всех 40 файлов manifest, полный набор и байты ZIP,
  sidecar SHA256, release.json и точные копии канонических bridge-docs.
  PackageContents.xml AppVersion=3.1.0; обе plugin DLL версии 3.1.0.0.
  Обе native-сборки успешны: у 2026 нет предупреждений/ошибок,
  у 2027 только прежние два Roslyn MSB3277, ошибок нет.
- Через официальный MCP SDK запущен именно упакованный EXE. Только
  initialize и tools/list, без CAD-вызовов: для каждого года прошли standard
  (76 tools), query (22), read-only (31); serverInfo.version=3.1.
  Проверены новые readers/writers, annotations, identity/dry_run schema
  и исключение writers из read-only. Всего шесть packaged-профилей PASS.
  До этого успешные unit-проверки без причины не повторялись.
- QA-runner и JSON результата находятся в служебной папке НК2 объекта:
  check_release_3_1.py и release_3_1_check.json. Временный plugin-build
  удалён после проверки точного пути и отсутствия загруженных оттуда модулей.
  Stage, ZIP и SHA сохранены. Этот итог добавлен отдельным документационным
  коммитом; пакет из fabf3c3 не пересобирался ради обновления журнала.
- Push, merge, установка runtime и live CAD tests не выполнялись.
  Следующий шаг пользователя: установка v3.1 по TEAM_SETUP после закрытия
  AutoCAD/Codex, затем проверка чтения и явно разрешённая native-проверка
  новых операций. Производственная очистка Поверхности.dwg ещё не начата.
## Подготовка v3.1 — 2026-10-28

- Пользователь явно поручил зафиксировать подготовленную доработку в andrew
  и собрать установщик v3.1. Push, merge и установка в поручение не входят.
- Выпуск готовится из чистого коммита личной ветки без переключения на develop.
  Порядок сборки — tools/package-team-dwg.ps1 -Version 3.1; параметр задаёт
  номер пакета и техническую версию сборок 3.1.0. Готовые прежние выпуски
  не перезаписываются. Результат и фактические проверки будут записаны после
  окончания упаковки отдельным обновлением журнала без пересборки пакета.
## Внешние ссылки и раскрытие блоков — 2026-10-28

- Запрос из НК2: первым проходом очистить лишние ссылки, внедрить содержащие
  поверхности и раскрыть блоки. Деление водосборов в этот проход не входит.
  Правила объекта находятся в его НК2/README.md; общие контракты — TOOLS.md,
  раздел «Внешние ссылки и раскрытие блоков».
- Перед правками чистая andrew/origin/andrew 01673d4; develop не занят другим
  worktree, незавершённых Git-операций нет, чат CAD develop был idle.
  Первый pull отклонила автоматическая проверка. Пользователь отдельно
  разрешил три команды fast-forward; все выполнены, обновлений не было,
  проверка включения develop в HEAD прошла. Commit, push и merge не выполнялись.
- Добавлены query-инструменты dwg_list_xrefs, dwg_plan_block_explode и
  modify-инструменты dwg_manage_xrefs, dwg_explode_blocks. Новые читатели
  используют read lock; все четыре работают только с активной вкладкой.
  Полные снимки и счётчик изменений DWG защищают запись от устаревшего плана.
- Инвентаризация возвращает граф ссылок, вставки, преобразования, подрезки
  и ограниченную сводку прямых объектов по типам/слоям. Нулевое число прямых
  HATCH не доказывает отсутствие поверхностей во вложенных блоках.
- Управление ссылками: один явно выбранный корень за вызов; bind сохраняет
  различия имён символов, detach проверяет удаление определения и вставок.
  Неполный граф блокирует запись; bind требует загруженных разрешённых узлов
  всего графа. Неопределённый результат передаёт подробности через Error;
  повторять запись автоматически запрещено.
- Раскрытие: один уровень, до 100 обычных вставок и 20 000 объектов.
  Штриховки остаются HATCH; сложные неподдержанные случаи отклоняются.
  До удаления вставок native-выход передаётся владельцу и транзакции;
  до Commit сверяются число и типы объектов. Старый обработчик использует
  тот же helper без повторной реализации. Оба writer по умолчанию dry_run.
- Независимое ревью выявило отсутствие проверки свободной командной строки
  в Xref и потерю подробностей ошибки через Result. Оба дефекта исправлены;
  добавлены проверки отказов, сохранения диагностики на wire и bind-policy.
  Дополнительных actionable замечаний после исправления не осталось.
- Сервер net8.0 и native 2026/2027 собраны. Для 2026 использованы закреплённые
  compile-only references 25.1.0: ошибок и предупреждений нет. Для 2027 —
  установленный AutoCAD: ошибок нет, прежние два предупреждения Roslyn MSB3277.
  Полный набор .NET: 1776 PASS, 1 FAIL, 0 SKIP из 1777. Единственный FAIL —
  дубль в тестовом ожидании списка классов, рабочая регистрация без дублей.
  После его исправления все 9 затронутых ToolsListSnapshotTests прошли;
  остальные успешные проверки не повторялись. В полном запуске прошли
  20 реальных MCP SDK stdio-профилей обоих годов без CAD-вызовов:
  standard 76, query 22, read-only 31, modify 35. git diff --check прошёл.
- Исходник изменён и собран офлайн. Официальный пакет не выпускался;
  установленный и загруженный runtime не менялся. Новые native-операции
  в AutoCAD не запускались. Рабочий DWG только ограниченно прочитан:
  DBMOD=0 при проверке, очистка и сохранение не выполнялись.
- Следующий шаг: review и коммит пользователем либо явное поручение коммита,
  затем выпуск и установка по TEAM_SETUP. После загрузки с отдельным
  разрешением проверить на тестовом DWG координаты, площади/отверстия HATCH,
  ассоциативность, ByBlock/слой 0, отказы и Undo. Общая атомарность native
  binding, полный rollback и жёсткая отмена native-вызова не заявлены.

## Простая нумерация выпусков — 2026-10-28

- Пользователь установил порядок: текущий выпуск v3, мелкие обновления v3.1,
  v3.2, крупные v4/v5. Новые выпуски без team/rc; памятка TEAM_SETUP обновлена.
  Команда упаковки принимает 3/3.1 и необязательный префикс v. Один короткий
  номер используется в имени ZIP, manifest, release.json и ответе MCP initialize.
  Autodesk/.NET получают техническое числовое значение 3.0.0 или 3.1.0.
- Installer сохраняет чтение прежних пакетов. Нормализация числовой версии
  единственная, в package-client-setup; из wrapper удалено повторное XML
  присваивание. Проверены реальные PowerShell AST-фрагменты: допустимые/битые
  версии, прежние team/rc, синтаксис и обе build/publish-команды — PASS.
  После изменения серверной версии 20 реальных MCP SDK профилей PASS.
- Промежуточный RC5 собран из 98201a7 и проверен: оба года, 40 файлов manifest,
  точные байты ZIP и standard/query/read-only SDK-профили. Не устанавливался.
  После смены нумерации его локальный выход удалён с проверкой отсутствия
  запущенных EXE/загруженных DLL из этого каталога. Итоговый пакет — releases/3/.
  Проверки угловой логики ниже действуют; живые DWG/runtime не изменены.
- Итоговый **v3 собран из чистого 5d01a17**, manifest.dirty=false, годы 2026/2027.
  Папка установки releases/3/stage/, ZIP DwgMcp.Setup-v3-win-x64.zip,
  39 992 430 байт, SHA256
  1f6428e3b97254e6a0e344db18a88054c925275585dc8701695a945406d28e31.
  Все 40 файлов manifest и байты ZIP проверены. MCP initialize сообщает 3,
  XML 3.0.0, обе plugin assemblies 3.0.0.0. Упакованный EXE проверен реальным
  SDK для обоих годов: standard72/query20/read-only29, без дублей, CAD tools
  не вызывались. Финальная упаковка: обе сборки без ошибок, у 2027 прежние
  два Roslyn MSB3277. Временный plugin-build удалён после проверки отсутствия
  загруженных DLL; один build.log сохранён в папке версии. Готовый пакет для
  этой записи журнала не пересобирался. Пользователь обновляет runtime сам;
  затем остаётся live-проверка поворота связанной вставки и Undo/Redo.

## Угловой атрибут: только повёрнутая вставка — 2026-10-28

- По запросу пользователя устранён обход всей BlockTable, который на большом
  DWG после каждой команды выдавал angle refresh definition limit exceeded.
  Начальная идея кеша/порционного поиска отменена после уточнения пользователя;
  её helper и тесты не оставлены в проекте.
- При ObjectOpenedForModify сохраняются прежние Rotation/именованные углы
  только переданной dynamic BlockReference. ObjectModified ставит её ID в очередь.
  После завершения работы AutoCAD один deferred callback сравнивает углы.
  Только при изменении угла читаются binding и атрибут этой самой вставки.
  MOVE/цвет без поворота не доходят до binding; соседние вставки того же
  определения не перечисляются и не обновляются.
- При открытии/активации DWG и после посторонних команд скана нет. Постоянная
  подписка angle Idle удалена; временная снимается после одного прохода.
  Нет наблюдения за dimensions, повторов стабилизации или автоповтора ошибки.
  Старый DeferredAngleRefreshState и его тесты удалены, вместо них проверяется
  новая событийная логика. Явный dwg_update_block_angles сохранён.
- До правок чистая andrew/origin/andrew 9e5bc17; три обязательных ff-only
  обновления и ancestry PASS. Пользователь поручил commit/push/пересборку;
  установку и последующую работу выполняет сам. Merge не выполнялся.
- Offline: **1735 PASS, 0 FAIL, 0 SKIP**, включая 20 реальных MCP SDK stdio
  профилей обоих годов. Проверены отсутствие startup/command scan, 12000 чужих
  вставок и один event-target, old/new углы, однократность, own writes,
  busy/ошибки, изоляция DWG и очистка подписок. Native DB/геометрия в этих
  lifecycle-тестах заменены doubles; это не live PASS grip/Undo/Redo.
- Обе native сборки PASS: 2026 без warnings/errors; 2027 с прежними двумя
  Roslyn MSB3277 и без ошибок. Чтение без Commit/write-lock и полный preflight
  перед реальной записью сохранены. Готовится кандидат 2.0.1-team.8-rc.5
  из чистого коммита. Установленные/загруженные DLL и DWG не менялись.
  После обновления проверить поворот связанной вставки и Undo/Redo в AutoCAD.
- Финальное ревью дополнительно закрыло два случая повторного запуска:
  IDs всех target attributes/parents подавляются до первого native write/open;
  исключение preflight также снимает Idle и очищает очередь затронутого контекста.
  После этих локальных изменений 47 lifecycle/startup checks PASS, включая
  три новых случая отказа native getters. Полный набор без причины не повторялся.
  Промежуточный RC4 не устанавливался и удалён после проверки отсутствия
  процессов/модулей из его каталога; пакет с тем же номером не переиздаётся.


## Перестановка существующих MLeader – 2026-10-25

- Перед правкой andrew/origin/andrew 9a2f870 чистая, develop не занят другим
  worktree; три обязательных ff-only обновления и ancestry PASS. Пользователь
  разрешил необходимые исходники, сборки, commit/push; работа делегирована root.
- Добавлен typed `dwg_reposition_leaders` в modify: 1–100 native plain MLeader,
  full DWG path/fingerprint, точные owner/layer, native MText.Contents, actual
  leader/line indices, anchor/2vertices/dogleg. Новый reader возвращает готовый
  reposition_snapshot и content bounds. Чужие исходники/ветки не менялись.
- Только SetDogleg/restore length, TextLocation и SetLastVertex: стрелка не
  получает setter и не двигается вместе с entity. Все targets до setters,
  одна transaction, exact native readback стрелки/обоих заданных положений,
  индексов и сохранённых text/graphics/XData. dry_run по умолчанию без setters;
  нет сохранения, пересоздания, автоматической маршрутизации или обещания Undo.
- Учтён native случай НВ: единственный leader index1, line index0. TextLocation
  – native anchor, может быть правым краем; несовместимая с ним последняя
  вершина отклоняется readback, а не молча сдвигает подпись.
- 47 affected checks PASS: exact/stale snapshots включая оба индекса и
  буквальные escapes, лимиты/hex aliases/геометрия, actual SDK schema,
  tools-list snapshots и 20 real stdio профилей 2026/2027. Начальные ошибки
  тестовых helpers/counts исправлены; финальные проверки успешны.
- Native сборки 2026/2027 PASS по реальным закреплённым references, сервер
  net8.0 собран. В первом 2027 build прежние два Roslyn MSB3277; в итоговом
  выводе они подавлены NoWarn=MSB3277, код зависимостей не менялся.
- Установленные/загруженные DLL не заменялись, нового RC/инсталлятора нет.
  Typed native live-тестов в этом подзадании не было. Root отдельно подтвердил
  одноразовым проектным прототипом перенос216/сохранение228стрелок, actual
  пересечений линий0 и наложений текста0. Это evidence подхода, не live PASS
  нового typed handler. Следующий шаг – штатный будущий выпуск/загрузка и
  отдельно разрешённый bounded native smoke test dry_run/запись/Undo.

## PlotPdf: сохранение единиц при смене листа – 2026-10-25

- Перед новой правкой andrew/origin/andrew cca00a5 чистая; обязательные ff-only
  обновления и ancestry PASS. Пользователь разрешил исходники/commit/push/сборку.
- Загруженный RC2 после успешной Model/A4 печати отказал на СГП/A0: native plot
  validation changed saved setting paper_units. После точной привязки PC3/media
  и RefreshLists копия теперь получает SetPlotPaperUnits(copy, l.PlotPaperUnits).
  Исходный Layout не меняется; оба полных snapshot guards сохранены без ослабления.
- Основной агент подтвердил такой порядок в bounded проектном прототипе:
  saved/ValidatedSettings DeepEquals TRUE, СГП A0 напечатан 3548343байта. Это live
  evidence алгоритма после смены листов, отдельное от новой typed DLL.
- Обе native сборки PASS: 2026 без warnings/errors; 2027 без errors, прежние
  два Roslyn MSB3277. 76 affected/schema checks PASS: существующие политики
  полного snapshot (включая paper_units), foreground и 20 реальных MCP SDK
  stdio-профилей обоих годов. Полный набор не повторяли, API/schema не менялись.
- Код cf852f0 опубликован в origin/andrew. Штатный RC3 собран из чистого commit:
  releases/2.0.1-team.8-rc.3/stage/install.ps1, manifest.dirty=false, оба года,
  40 payload-файлов. ZIP DwgMcp.Setup-v2.0.1-team.8-rc.3-win-x64.zip; SHA256
  9c92f770f3eeb81a08ac17cc375be39a4b6ecbde30071dcbd3c46fd4daea730b.
  Это кандидат личной ветки; RC2 и его ZIP не менялись. Новую DLL в работающий
  CAD не устанавливали, живых CAD-вызовов в этом подзадании не было.
  После безопасного обновления ещё нужна проверка typed печати Model/A4,
  затем СГП/A0; прототип не заменяет этот native smoke test. Пакет и успешные
  проверки для этой записи документации не пересобирать/не повторять.

## PlotPdf RC2: упакован исправленный foreground-путь – 2026-10-25

- Код bc80a5b, штатный кандидат releases/2.0.1-team.8-rc.2/, manifest.dirty=false,
  оба года2026/2027, 40 payload-файлов. ZIP
  DwgMcp.Setup-v2.0.1-team.8-rc.2-win-x64.zip; SHA256
  d4410aa4252abf08f2a4c7bb424aff1eed01cae138a9ead1dd3ca95d526fefa8.
  Пакет включает предыдущий серверный фикс JSON-string d844062. Финальная
  упаковка собрала оба native плагина: 2026 без предупреждений/ошибок, 2027 без
  ошибок с прежними двумя Roslyn MSB3277. 76 affected/schema checks PASS.
- По сообщению основного агента повторный bounded проектный прототип напечатал
  актуальный СГП 3541213байт за18с; собран том54страницы/5552219байт/17закладок.
  Это live evidence самого foreground/ProgressDialog/exact-snapshot алгоритма,
  отдельного от ещё не установленного и не загруженного нового typed RC2.
- RC2 не устанавливался поверх рабочего AutoCAD, программа не перезапускалась
  этим подзаданием, прямых live-вызовов новых методов не было. Нужен штатный
  последующий install из stage после безопасного закрытия CAD/MCP и проверка
  typed dry_run/печати. Старый RC1 и его неизменяемый ZIP сохранены как предыдущий
  кандидат. Дополнительную упаковку для этой записи документации не выполнять.

## PlotPdf: exact media, foreground и локальный вывод – 2026-10-25

- До новой native правки чистая andrew/origin/andrew d844062; обязательные
  ff-only обновления и ancestry PASS. Пользователь разрешил необходимые
  исходники/коммиты/push/сборки. Другие компоненты и таблицы не менялись.
- Native теперь обновляет списки transient PlotSettings, проверяет точное наличие
  сохранённых устройства и canonical media, задаёт те же имена. MatchDisabled
  заменён на MatchEnabled только с полным exact guard ValidatedSettings до
  BeginPlot. Подмена любого сохранённого поля отклоняется до создания PDF.
- По фактической диагностике основного агента MatchDisabled отказывал
  eNoMatchingMedia даже при существующем exact media, тогда как MatchEnabled
  вернул полный snapshot без отличий. Первый прототип с BACKGROUNDPLOT2 не
  создал PDF. Затем foreground0, стандартный невидимый ProgressDialog и localTemp
  дали PDF 3540622 байта и восстановили BACKGROUNDPLOT2. Это проверка одноразового
  проектного прототипа, не live-тест нового typed handler.
- В shared service перенесён foreground scope с finally-restore/readback,
  невидимые OnBeginPlot/OnBeginSheet/OnEndSheet/OnEndPlot, локальный временный PDF.
  Готовый файл копируется в уникальный staging рядом с целью, затем rename с
  overwrite guard. Оба собственных temp очищаются; cleanup_warnings отдельно.
  Нет автосохранения или обнуления DBMOD: при native печати наблюдался DBMOD5.
- 76 affected checks PASS: политики exact device/media/полного snapshot,
  восстановление BACKGROUNDPLOT при успехе/ошибке, отказы setter/restore и реальные
  MCP SDK схемы/20 stdio-профилей обоих годов. Полный 1709-набор не повторяли.
  Обе сборки PASS: 2026 без warnings/errors, 2027 без errors и с прежними двумя
  Roslyn MSB3277. Новый typed handler не вызывался живьём, CAD не переключался,
  не печатался и не сохранялся этим подзаданием. Кандидат RC2 готовится отдельно;
  установленные рабочие DLL не заменялись.

## PlotPdf: явная JSON-строка на входе MCP – 2026-10-25

- В рабочем ПОС после пользовательской переустановки обнаружено расхождение:
  официальный installed EXE tools/list отдаёт expected_plot_settings:true
  (неограниченный JSON Schema), но адаптер чата представляет его строкой.
  JSON.stringify snapshot при прежнем JsonElement-параметре доходил до native
  как строка и отклонялся: expected_plot_settings must be object. Контракт
  установленного EXE прочитан официальным MCP SDK; CAD из этой проверки не вызывался.
- Исправлен только сервер: вход PlotPdf явно string, с JSON object parsing в
  JObject до plugin request. Предел 65536 знаков, глубина16, дубликаты и остаток
  после объекта отклоняются локально. Числа в paper_size/origin/window/custom_scale
  преобразуются к native double: JSON.stringify может убрать дробную часть .0.
- Native handler, schema, plugin DLL и прочие операции не менялись. 36 целевых
  LayoutPlotTests PASS, включая 10 новых regression cases и реальную MCP schema.
  Server net8.0 собран. Новый сервер доступен как source bin/Release DLL для
  отдельного официального MCP stdio вызова; установка текущего runtime не менялась.
- По сообщению основного агента direct installed stdio с object уже преодолел
  JSON-контракт, но native dry_run отказал eNoMatchingMedia. Это отдельный вопрос
  PC3/media initialization, данный серверный фикс его не устраняет. Native/live
  диагностика или печать в этом подзадании не выполнялись.

## Листы и печать PDF: кандидат собран – 2026-10-24

- Код commit c563409 в andrew. Штатный пакет 2.0.1-team.8-rc.1 собран из
  чистого commit, manifest.dirty=false, оба года 2026/2027. Финальная сборка
  включает последние ограничения reader: 2026 без warnings/errors, 2027 без
  errors и с прежними двумя Roslyn MSB3277. 1709 offline PASS остаются актуальны;
  повтор полного набора после этой записи документации не требуется.
- ZIP: releases/2.0.1-team.8-rc.1/DwgMcp.Setup-v2.0.1-team.8-rc.1-win-x64.zip.
  SHA256 4bd89c481ec30aac551e94e548f970f72c01568f211901fd3258fc9951902307.
  Это локальный кандидат из личной ветки, не принятие в develop и не публикация
  для коллег. Установленные и загруженные рабочие DLL не заменялись.
- По явному поручению выполнен один read-only запрос list_layouts через новый
  упакованный штатный stdio server/official MCP SDK к текущему СГП v6. Identity
  подтверждена, DBMOD=0, Model. Ответ старого загруженного плагина:
  unknown command: list_layouts. Новая читающая схема присутствует в tools/list.
  Новая native реализация при этом не исполнялась. Результат записан в ignored
  releases/native-tests/layout-plot-readonly-probe-20261024.json.
- CAD не закрывался и не переключался. Следующий шаг: безопасно сохранить и
  закрыть открытые пользовательские документы, штатно обновить через install.ps1
  из stage, перезапустить AutoCAD и MCP. Затем прочитать layouts и штамп, выполнить
  явное select_layout/печать по основной задаче. Не пытаться выдавать новую DLL
  на диске за уже загруженную; hot-reload в текущем плагине отсутствует.

## Листы и печать PDF: исходники и offline-проверки – 2026-10-24

- Прямой запрос из задачи ПОС: чтение штампа paperspace, правка исходных DWG и
  печать тома. Пользователь отдельно разрешил необходимые Git-коммиты, push,
  сборки и обновление. До правок чистая andrew/origin/andrew 0d587ef, develop
  не занят другим worktree; три обязательных ff-only обновления и ancestry PASS.
  Первый git pull ранее был отклонён auto-review до этого явного разрешения;
  повтор состоялся только после разрешения пользователя.
- Добавлены четыре typed метода профиля drawing: list_layouts,
  get_layout_entities, select_layout, plot_pdf. Читатели используют read-lock и
  OpenCloseTransaction, точную identity активного DWG, ограниченные результаты.
  Прямые тексты, геометрия и атрибуты BlockReference из paperspace доступны без
  перехода UI. SelectLayout отдельно и явно меняет существующий layout/PSPACE.
- PlotPdf проверяет snapshot сохранённых print settings, активный layout,
  PDF-device, отсутствие текущей печати и абсолютный выходной PDF-путь. Копия
  PlotSettings transient; масштаб, формат, окно, CTB и исходный Layout не меняются.
  PlotEngine печатает одну страницу; проверяется %PDF, размер и SHA256. Сначала
  создаётся уникальный временный PDF рядом с целью; готовый файл заменяет цель
  только с явным overwrite_existing. DWG не сохраняется.
- Чистая политика и реальные SDK-схемы: 1709 PASS, включая 26 новых проверок.
  Обновлены ожидаемые каталоги: стандартный writable 71, read-only 29;
  drawing writable 14, read-only 4. Первоначальные 13 snapshot/count отказов
  устранены только добавлением четырёх фактически новых методов в ожидания.
- Первые builds API2026/2027 PASS; 2026 без предупреждений, 2027 с прежними двумя
  Roslyn MSB3277. Финальная упаковка после ограничений размера reader ещё впереди.
  Live-вызовов новых методов, переключений DWG, правок или печати не выполнялось.
- Ограничение: синхронный native PlotEngine может пережить транспортный таймаут;
  не повторять неизвестную печать автоматически. Очередь заданий не реализована.
  В текущем плагине нет hot-reload: MCPSTOP/MCPSTART оставляют старую DLL.
  Для новых методов нужны новые server/plugin и штатная загрузка при перезапуске
  AutoCAD. Установка поверх загруженной рабочей DLL не выполнялась.
## TABLE: кандидат 2.0.1-team.7-rc.2 собран и проверен — 2026-10-23

- Исправления native-теста: commit edbcc1b в andrew, merge
  1c847b1e6dcfc2582678474f03a77b60141ba29b в develop. Из чистой develop собран
  RC2, manifest.dirty=false, оба года. Полный набор 1683 PASS и обе сборки
  относятся к этому коду; итоговая запись журнала их не меняет.
- Готовый пакет releases/2.0.1-team.7-rc.2/
  DwgMcp.Setup-v2.0.1-team.7-rc.2-win-x64.zip, 39 945 043 байта. SHA256:
  d9a7d404c60e526f7e960e38c398b81e7d7fcf8457fdbe2e5919823c31e45f89.
  Папка установки — stage рядом. Независимо проверены все 40 manifest payload
  files и точный ZIP из 41 файла, bytes/hashes/sidecar, оба года, XML,
  release.json, шесть runtime-docs и пять patch copies. Autodesk compile-only
  DLL отсутствуют. Упакованный EXE: все 20 MCP SDK профилей PASS, TABLE schemas
  и read/write annotations верны; CAD tools из этой проверки не вызывались.
- После последней native-диагностики снова подтверждено: 84 model entities
  совпадают с исходным query JSON, новых TABLE нет; DBMOD=1 после отката layout.
  Excel saved=true, не менялся. Сохранения DWG не было.
- Установлена/загружена пока RC1. RC2 не устанавливался поверх активного AutoCAD.
  Для следующего теста пользователь закрывает тестовые DWG без сохранения,
  AutoCAD/Codex, устанавливает RC2 stage (Years2027, Client none) и запускает
  их снова. После этого проверить typed create/read на 4x4 и данных Excel,
  сохранность значений/merges/format/bounds, затем пользовательский Undo/Redo
  и cleanup без сохранения. Нативная проверка RC2 ещё не завершена.
- Итоговый журнал включается в обе ветки перед ранее порученным atomic push.
  Исходный commit/manifest и готовые байты RC2 остаются неизменными; упаковку
  и успешные проверки для этой записи не повторять.


## TABLE: native-проверка RC1 и исправления для RC2 — 2026-10-23

- Пользователь переустановил RC1, открыл тестовые DWG/Excel и прямо поручил
  native-проверки. Installed/loaded подтверждены: server/current 15/15 файлов,
  установленный 2027 bundle 8/8 файлов совпадают с RC1; AutoCAD PID 7716 загрузил
  штатную DLL 2027 с совпавшим SHA256, оба stdio-процесса используют current EXE.
  Плагин 2026 не установлен; нативная проверка этого года не заявляется.
- Цели: D:\тест acad-2027\05 ЗР-ПМ_2026-П-НК Схема НК2.dwg и ТК НК2.xlsx
  из той же папки. Excel прочитан через существующий Office-мост: A1:AQ23,
  43 столбца, 17 колодцев, формульных ошибок в считанных значениях нет.
  Excel не редактировался, saved=true. Другие открытые копии плана/профиля
  не редактировались. Исходная готовая таблица на листе не переделывалась.
- Dry-run 4x4 прошёл, native Data style разрешён как _DATA. Создание 4x4 и
  минимального 1x1 через RC1 отказало eInvalidInput до фиксации. Существующая
  Table 9F043 ожидаемо отклонена reader как форматированный MText вне scope.
- Локализация через штатный send_code: доверенные синхронные диагностические
  фрагменты на точной тестовой identity, сначала transient Table, затем одна
  откатываемая transaction без Commit/Save. Выявлено:
  1. Table.FlowDirection принимает native AcDb коды 0/1, managed MText enum
     TopToBottom имеет код 3 и вызывает eInvalidInput. Raw0 setter проходит.
  2. Неякорные merged cells могут возвращать IsMerged=false, null style/alignment
     и height=-1; определять их надо по диапазонам опорных ячеек.
  3. Запись пустого TextString сбрасывает native оформление. После перестановки
     text-before-appearance обычная пустая ячейка сохраняет ГОСТ/2.5.
     Пустые merged nonanchors могут иметь 0 или 1 plain empty content.
- Исправления: TableFlowDirectionValue с native0/1 mapping, TableCellMap с
  bounds/overlap/coordinate guards; Describe возвращает nonanchor marker и
  merge_anchor вместо отсутствующего оформления, проверяет пустое содержимое.
  TextString назначается один раз до оформления; добавлены этапы создания
  и координаты ошибок readback. Контракт дополнен в TOOLS#tables.
- Native low-level контроль исправленного порядка 4x4: 16 текстов, Unicode,
  001/7,78, пустая строка/дефис/0, два объединения, шрифт/высота, поворот90,
  выравнивание, размер110x50 и bounds совпали, errors=[]. Это диагностический
  прототип, не завершённая проверка нового typed handler/Commit/Undo/Redo.
- После отката все 84 исходных model-space entities совпали с исходным снимком
  по handles и полному query JSON; новых TABLE не осталось. Native layout
  в откатываемой transaction всё же поднял DBMOD с0до1. DWG не сохранялся:
  тестовую копию закрывать без сохранения. View не перемещался.
- Перед исправлениями чистая andrew/origin186fcbd проверена по AGENTS;
  обязательные fast-forward updates/ancestry успешны. Оба плагина собраны:
  2026 без warnings/errors, 2027 без errors и с прежними двумя Roslyn MSB3277.
  Полный offline набор 1683 PASS, включая 28 новых merge/flow regressions и
  фактические SDK-профили обоих годов. diff --check PASS.
- Следующий шаг: кандидат 2.0.1-team.7-rc.2 из чистого принятого commit,
  оба года; затем пользовательская переустановка/перезапуск. Новый typed
  create/read, full Excel matrix, native Commit, один Undo/Redo и visual fit
  остаются pending до загрузки RC2. Успех прототипа их не подменяет.


## TABLE: commit, merge и проверенный пакет 2.0.1-team.7-rc.1 — 2026-10-23

- По прямому поручению пользователя сделан commit
  6f394d25e8f2f43a44562cbf3c8cec2885d6527c в andrew и merge
  c60c3d70b0b2dd5b5e7f3c8f91c78c9dc30a9764 в локальную develop.
  Перед merge fetch origin успешен: новых изменений develop не было, конфликтов
  нет; merge не изменил проверенный код относительно andrew. Push не выполнялся.
- Из чистой develop/c60c3d7 штатным tools/package-team-dwg.ps1 собран кандидат
  2.0.1-team.7-rc.1, manifest.dirty=false, AutoCAD 2026/2027. Выход:
  releases/2.0.1-team.7-rc.1/DwgMcp.Setup-v2.0.1-team.7-rc.1-win-x64.zip,
  39 942 107 байт. SHA256:
  fefb8a970ab21e9f367a64720b56c6fab9bfb6365545c2efa47b0ff0d2cb0857.
  Готовая папка установки — stage рядом с ZIP. Упаковка успешна с первого раза;
  оба плагина собраны, у 2027 только прежние два Roslyn MSB3277.
- Независимая read-only проверка: все 40 payload-файлов manifest имеют верные
  размеры/SHA256; ZIP содержит ровно эти 40 файлов и manifest, без дублей и
  лишних файлов, bytes совпадают со stage. Sidecar SHA совпадает. XML, оба года,
  release.json, шесть runtime-документов и patch copies проверены. Compile-only
  Autodesk AcMgd/AcDbMgd/AcCoreMgd в поставку не попали.
- Упакованный self-contained EXE проверен реальным Python MCP SDK: initialize
  и tools/list, 20 профилей (10 для каждого года) PASS, без вызовов CAD tools.
  Стандартный каталог 67, read-only 27; TABLE create/read доступны в нужных
  профилях, typed matrix/point/merge/format schemas и read/write annotations
  верны. Дублей нет. Ранее успешные 1655 .NET тестов того же кода не повторялись.
- Installed/loaded/live: установка и AutoCAD UI не запускались; работающий мост
  ещё прежний. Native TABLE/Undo/Redo/merged getters/visual fit остаются pending.
  До такой проверки это RC, не проверенный native stable. Чертежи не менялись
  и не сохранялись. Следующий шаг — установка готового stage при закрытом
  AutoCAD/Codex, пользовательский перезапуск, затем отдельно разрешённая
  native-проверка TABLE. Выпуск на NAS не копировался.
- andrew включена в develop; рабочая копия возвращена в личную andrew.
  Эта запись фиксирует результат уже готового неизменяемого пакета, не меняет
  его исходный commit/manifest и не требует повторной сборки или упаковки.


## TABLE: исходники и подготовка кандидата выпуска — 2026-10-23

- Пользователь поручил добавить единый native TABLE, затем прямо поручил
  коммит и выпуск обновления, затем merge в develop. Работа продолжена в
  andrew после чистого старта
  от c0abebc: обязательные fast-forward updates и ancestry-check были успешны
  до первых правок 22 октября. Повторного pull посреди задачи не выполнялось.
  Прежние изменения document routing/Undo/LISP сохранены.
- Единственная реализация — shared/Tables/TableService.cs, общий TableInput
  и обработчики create_table/read_table. Старая ограниченная фабрика TABLE
  удалена. Typed dwg_create_table включён в drawing/annotation, dwg_read_table —
  в query/drawing/annotation, включая read-only. Правила — docs/TOOLS.md#tables.
- Поддержаны размеры строк/столбцов, объединения с пустыми неякорными ячейками,
  высота/поворот/выравнивание текста. Fresh identity, активный DWG, native
  зависимости и Undo-state проверяются до записи; один объект и transaction,
  полный readback размеров/текста/форматов/merges/bounds до Commit. String/Unitless
  и пустой DataFormat сохраняют буквальный текст. Fields/formulas/MText/%%
  controls отклоняются; неизвестные и неоднозначные значения не подставляются.
- Reader: только TABLE непосредственно в local model/paper layout, без xref,
  block definition, numeric/formatted values, links/fields/formulas/blocks
  и multiple contents. Read-lock и OpenCloseTransaction без Commit, setters,
  native layout regeneration и field evaluation. Dry-run не создаёт entity;
  native layout и text fit при этом не проверяются.
- Offline 22 октября: сервер собран; оба плагина собраны, 2026 с compile-only
  AutoCAD.NET 25.1.0 (UseAutoCadNuGetRefs=true), 0 errors/warnings; 2027 —
  0 errors и прежние два Roslyn MSB3277. Полный набор: 1655 PASS, 0 failed/skipped.
  Реальный MCP SDK initialize/tools/list проверил по 10 профилей для target
  2026 и 2027: стандарт 67, read-only 27, query 20, drawing 10, annotation 5,
  read-only drawing 2/annotation 1, без дублей, typed schemas/annotations верны.
  Дополнительно проверены bounds, merges/data loss, Unicode/newlines, null/dash/0,
  format limits и отказ до transport. Эти успешные проверки не повторялись
  для последующей правки документации.
- Независимое source review выявило неоднозначность block-table coordinates
  и legacy %% controls; оба замечания исправлены. Исправлены nullable managed
  API, явный строковый тип и native Data-class style resolver. diff --check PASS.
- План выпуска: коммит в andrew, порученный пользователем merge в локальную
  develop, затем кандидат 2.0.1-team.7-rc.1 из чистой develop, manifest.dirty=false,
  оба года. Это RC до native-проверки. Push и распространение стабильной версии
  не выполняются этим поручением.
- Installed/loaded/live: новый runtime ещё не установлен/не загружен.
  Native TABLE/Undo/Redo, getters merged non-anchor cells и visual fit не
  проверены. Сетка/цвет/вес наследуются от текущего TableStyle. Чертежи и Excel
  этой доработкой не менялись и не сохранялись. После выпуска следующий шаг —
  локальная установка при закрытом AutoCAD/Codex и отдельная явно разрешённая
  native-проверка; перезапуск/UI выполняет пользователь.


## Проверка после перезапуска MCP и публикация — 2026-10-22

- Пользователь перезапустил MCP и явно поручил push/merge/тесты. Рабочая копия
  andrew чистая; fetch не обнаружил новых удалённых изменений. Независимое
  read-only ревью document routing, Undo и LISP не нашло блокеров.
- Новые document tools доступны непосредственно в этом чате. Через них прочитан
  неактивный профиль (3564 объекта), проверен отказ get_selection в неактивном
  DWG и сохранение цели после неизвестного ID. Возврат в follow-active успешен:
  активен прежний план, его fingerprint, выделение и DBMOD=0 не изменились.
  Чертежи не редактировались и не сохранялись.
- Код остаётся проверенным commit 61916cf: 1531 offline-тест, обе сборки и
  native-проверки установленного RC из предыдущего раздела действуют.
  Полный набор и упаковка для этой записи журнала не повторялись. Ограничения
  native-проверок AutoCAD 2026/Civil 3D и grip/производной записи блока сохраняются.

## Пакет 2.0.1-team.6-rc.1 собран и установлен — 2026-10-22

- По поручению пользователя сделан локальный commit 61916cf848f6ef4ec39229b9d39ce81d4e4dd493
  в andrew: проверенные document routing, Undo/Redo, MLeader, LISP, контракты
  и native-журнал ниже. Затем пользователь также поручил переустановку и тесты.
  Push/merge не выполнялись; результат упаковки/установки фиксируется отдельно
  в журнале без изменения кода и пересборки уже проверенного архива.
- Пакет из чистого commit 61916cf, manifest.dirty=false, AutoCAD 2026/2027.
  Это RC личной ветки, общий принятый выпуск — после принятия в develop.
  Выход: releases/2.0.1-team.6-rc.1/DwgMcp.Setup-v2.0.1-team.6-rc.1-win-x64.zip,
  39 907 742 байта. ZIP SHA256:
  89dc57afd5bde254b72d1c8872802f5efdf35cd6150bec0727c5ed43c891c0a8.
  Готовая распакованная папка установки — stage/ рядом с ZIP; дополнительных
  распаковок/копий не создавалось. Неиспользуемый plugin-build удалён после
  проверки реального пути загруженной DLL, stage и ZIP сохранены.
- Проверены все 40 файлов manifest: размеры/хеши stage, идентичные байты в ZIP,
  точный состав архива, оба года, совпадение runtime-docs с каноническими.
  Autodesk compile-only AcMgd/AcDbMgd/AcCoreMgd в поставку не попали.
  Самодостаточный EXE из пакета проверен реальным SDK: для каждого года
  стандарт 65, query 19, read-only 26, без дублей; новые document schemas
  корректны, удалённые Unicode/translation tools отсутствуют.
- Первый publish отказал из-за sandbox-доступа к NuGet.Config; повтор с доступом
  к зависимостям успешен. Перед повтором удалён только проверенный пустой stage.
  Оба плагина собраны; у 2027 прежние два Roslyn MSB3277. 1531 успешный тест
  того же кода без причины не повторялся.
- При закрытом AutoCAD выполнена штатная установка из stage:
  install.ps1 -Years 2027 -Client none. EXE, DLL и bridge-docs установлены в
  прежние локальные пути, их байты совпадают с пакетом; release.json содержит
  team.6-rc.1. Существующий config сохранён. NETLOAD/MCPSTART вручную не нужны:
  обычный запуск AutoCAD зарегистрировал pipe BimwrightDwg-2027-29148.
  Загруженная DLL проверена в установленном ApplicationPlugins bundle.
  Первый read был раньше регистрации discovery; после готовности автозагрузки
  связь успешна, исход записи неизвестным не был (вызов был только читающим).
- Повторены проверки именно установленного EXE с установленной DLL: чтение
  обоих DWG без изменения вкладки/выделения/камеры/DBMOD, отказы неактивных
  selection/variables/create_line, сохранение цели при неверном ID и изоляция
  двух stdio-процессов — PASS. На копии профиля круг 1285B после чтений всех
  восьми DB-readers и обоих selection tools отменён одним Ctrl+Z; между Undo
  и Ctrl+Y снова прочитаны свойства и пустое выделение. Redo восстановил
  точные координаты/радиус. Стрелка без текста 1285C создана/прочитана.
  Оба контрольных объекта удалены и отсутствие подтверждено. Сохранена только
  очищенная локальная копия профиля; NAS-оригиналы не затрагивались.
- Текущий процесс Codex держит прежний сервер из временной rollback-папки
  current.dwgmcp-rollback-44a6dccfd59447c485d2af7c68482fb6. Не удалять её, пока
  процесс используется; штатный installer удалит после освобождения.
  Новый EXE и 65 tools уже проверены отдельными SDK-клиентами. Для обновления
  каталога tools в обычных чатах утром перезапустить Codex; config менять не надо.
  AutoCAD 2026/Civil 3D и grip/производные записи блока остаются отдельными
  native-проверками, как указано ниже; их успешность не заявляется.

## Выбор DWG и завершённые native-проверки — 2026-10-22

- Продолжена та же задача в andrew; предыдущие изменения сохранены, повторного
  pull и Git commit/push/merge не было. Контракты новых инструментов записаны
  в существующих AGENTS, TOOLS#documents, CAD_WORKFLOW и server instructions.
  Это подготовленные исходники, не новый выпущенный установщик.
- dwg_list_documents/dwg_use_document проверены реальным MCP SDK на открытых
  локальных копиях плана и профиля из D:\Загрузки. Каждый целевой DWG прочитан
  через metadata/query/count/styles/properties. Вкладка, pickfirst, камера и
  DBMOD активного DWG не изменились. Неактивные selection/variables/create_line
  получили ожидаемый отказ; случайный неизвестный ID не заменил прежнюю цель.
  Второй stdio-процесс продолжал следовать активной вкладке, первый затем
  сброшен в follow-active. Выбор не переключает UI и пока разрешает только
  восемь перечисленных DB-readers для неактивного документа; запись — в активный.
- MLeader без текста раньше падала при чтении MText из NoneContent. Исправление
  подтверждено native: создана стрелка 3ADA с Z=12, размером 1.5, прямой линией,
  без landing/dogleg/frame/annotation scale; прочитана и удалена.
- Native Undo выявил два разных дефекта. Обычные DB-readers оставляли пустой
  шаг; после OpenCloseTransaction без Commit и Read-lock восемь чтений больше
  не мешают одному U и последующему Redo (круг 3B1F). Затем отдельно доказано:
  get_selection со старым write-lock сбрасывал Redo даже при пустом выборе.
  Для get_selection/get_selected_texts также введён Read-lock, без разрешения
  этих Editor-операций в неактивном DWG. После замены DLL реальный pickfirst
  с кругом 3B31 прочитан обоими tools, один Ctrl+Z удалил круг; после ещё двух
  чтений, включая ожидаемый no selection, Ctrl+Y восстановил ту же геометрию.
  Итоговые callbacks без реальных изменений не создавали лишнего шага.
- BWUNICODE: исправлены возврат значения через or, текст ошибок и диагностика
  этапа. По разрешению пользователя установлен неизменённый bundled Open Sans
  Condensed Light в Windows для текущего пользователя: Windows Fonts в LocalAppData,
  HKCU registration, AddFontResourceEx вернул 1. Font Viewer отказал, но все
  18 таблиц TTF и общий checksum исправны, байты совпадают с Google Fonts.
  Шрифт не заменялся/не скачивался; LISP сам его не устанавливает.
- После установки обнаружено: SetFont нормализует FontFile в basename, который
  AutoLISP findfile не находил в пользовательском Fonts. Теперь только точный
  ожидаемый basename может разрешаться через заранее проверенный путь входа;
  native face/начертание и остальные свойства стиля по-прежнему проверяются.
  Поиск пустого font_path также учитывает Windows user Fonts.
- В MLeader изменение высоты/метрик пересчитывает внутреннюю координату MText
  CONTEXT_DATA/12. Она отделена от базовой точки и вершин/разрывов линий по
  официальному DXF-контракту; внутреннее размещение не сравнивается как ручная
  контрольная точка. Полная проверка прочих точек сохранена, LEADER/12 break
  points не исключены. Native-тест дополнительно независимо сверяет
  GetLeaderLineVertices и content base. Ограничение пояснено в CAD_WORKFLOW.
- Финальный Unicode native-прогон: **3 PASS, 0 FAIL, 0 SKIP** — DBText/MText/
  MLeader TTF 10→9, сохранение явного цвета и контрольных точек, повтор 9→9;
  SHX 10→6; fields/inline-format отказывают без изменения свойств.
  Предшествующие NETLEN/PLLEN/PLLENLEADERS native PASS остаются действующими.
  Во вспомогательном тесте отдельно исправлено употребление AutoLISP last;
  это была ошибка нового тестового кода, а не production-команды.
- Offline окончательной .NET-версии: **1531 passed, 0 failed, 0 skipped**.
  Два AppData-теста первоначально отказали из-за sandbox; с доступом к их
  собственным тестовым файлам весь набор прошёл. Оба плагина собраны: 2026
  без предупреждений, 2027 с прежними двумя Roslyn MSB3277. Реальный Python
  MCP SDK: восемь профилей для каждого года, стандарт 65/annotation 66/
  read-only 26/query 19, без дублей. Python AST, UTF-8/скобки/размер трёх LISP,
  Markdown fences и diff --check проверены. Скрипт диагностики выводит
  предупреждения сервера без потока служебных Info-сообщений.
- Runtime: DLL/PDB 2027 заменены в существующем установленном bundle и загружены
  контролируемым перезапуском AutoCAD; финальные файлы совпадают со сборкой.
  Установленный server EXE и MCP config НЕ менялись. Новые document tools пока
  доступны через собранный source-server, а текущий клиентский каталог старого
  установленного EXE остаётся прежним. Установщик не собирался.
- Уборка завершена: 36 Unicode fixtures, тестовая стрелка и контрольные круги
  удалены, шесть SHX-стилей, Bimwright_Unicode и восемь тестовых слоёв удалены
  только после проверки отсутствия использования. Проверка MCP_NATIVE_* пуста;
  пользовательская выноска «сущ сеть» не затрагивалась. Сохранялись только
  подтверждённые локальные копии D:\Загрузки\(m) 5.3 нк2.dwg и
  D:\Загрузки\Профили НК2.dwg для перезапусков, NAS-оригиналы не открывались.
  Native-журнал остался в игнорируемом releases/native-tests/2026-10-21/.
- Оставшиеся границы проверки: native выполнялся на AutoCAD 2027, не на 2026;
  grip→Idle→Undo/Redo с реальной производной записью углового атрибута
  динамического блока отдельно не проверен. Много-DWG проверка не заявляет
  фоновой записи, открытия/закрытия файлов или native lifecycle close/reopen.
  Следующий этап поставки: пользователь принимает/коммитит исходники, затем
  обычный выпуск установщика из чистой develop обновит EXE и каталог клиентов.

## Остановлено по просьбе пользователя — 2026-10-22

Пользователь вернулся к работе и попросил прекратить вмешательство в AutoCAD.
Live-тесты, установка шрифта и перезапуск AutoCAD отложены. При продолжении
сначала прочитать этот раздел и текущий diff; не начинать новые CAD-действия
без согласования свободного времени. Незавершённые исходники сохранены в andrew,
без commit/push/merge; повторный pull посреди этой работы не выполнять.

**Готово в исходниках, ещё не установлено:**

- Добавлены dwg_list_documents и dwg_use_document: выбор целевого открытого
  DWG внутри отдельной MCP-сессии без переключения вкладки пользователя.
  Восемь проверенных DB-readers разрешены для неактивного DWG; запись,
  Editor/выделение, переменные, вид и LISP требуют активного документа.
  ID привязан к экземпляру Document; закрытый/неизвестный ID даёт отказ,
  null возвращает следование активной вкладке. Между чатами выбор не общий.
- Исправлено чтение MLeader без текста: прежнее обращение к MText вызывало
  native-ошибку при создании стрелки без подписи. GetSelectedTexts пропускает
  такие выноски. Native-проверка исправленной DLL ещё предстоит.
- Для DB-readers введены OpenCloseTransaction без Commit и Read-lock.
  Старый runtime оставлял пустой шаг Undo после чтения свойств; исправление
  нужно проверить после загрузки новой DLL. Чистый UI Undo/Redo с фоновым
  модулем без реальных изменений прошёл; grip/угловой атрибут ещё не проверен.
- В polyline_lengths.lsp исправлены привязка bulge к вершине и замыкание;
  общий pllen:label используется командой PLLEN и native-тестом.
  В apply_unicode_style.lsp исправлено использование or вместо значения
  (AutoLISP возвращает T), сохранены текст и этап native-ошибки.

**Что проверено:**

- Полный .NET-набор: 1519 PASS, затем добавлены ещё восемь тестов Read-lock,
  пока без нового полного прогона. Обе DLL 2026/2027 собраны; у 2027 прежние
  два Roslyn MSB3277. Реальный Python MCP SDK: восемь профилей на каждый год,
  штатный 65 инструментов, annotation 66, read-only 26, query 19; без дублей.
- На локальной копии плана проверены смешанное/ограниченное/отключённое
  выделение, MLeader с текстом и Z, отказы без оставшихся объектов,
  UpdateTexts и CollapseAndRewrite. Выделение после чтения сохранялось.
- Native LISP: NETLEN с открытой/закрытой прокладкой и футляром, привязка
  PLLENLEADERS по стрелке и повторный запуск, дуга PLLEN и четыре подписи
  замкнутого прямоугольника прошли. Полный интерактивный цикл не заявляется.
- BWUNICODE: отказы для fields/inline formatting и проверенный откат прошли;
  положительные случаи остановились на TextStyle.SetFont. TTF присутствует
  в lisp/, но Open Sans Condensed Light не зарегистрирован в Windows.
  Пользователь разрешил установку шрифта, однако до просьбы остановиться
  установка НЕ выполнялась. Нужны установка и перезапуск AutoCAD.

**Состояние рабочего AutoCAD и продолжение:**

- Установленные EXE/DLL и конфигурация не заменялись, AutoCAD не перезапускался,
  установщик не собирался. Новые инструменты выбора DWG пока только в source.
  Открытые тестовые копии: D:\Загрузки\(m) 5.3 нк2.dwg (план) и
  D:\Загрузки\Профили НК2.dwg. Во время остановки активен был план;
  дальше пользователь может переключать вкладки, перечитать identity обязательно.
- В плане остались тестовые объекты на слоях MCP_NATIVE_20261021_d9e57125,
  MCP_NATIVE_436516437, MCP_NATIVE_486793328, MCP_NATIVE_486898250 и три
  одноимённых стиля с суффиксом _SHX. Это незавершённая уборка, не рабочие
  объекты пользователя. На первом слое также есть MText 3AC4; старые 3ABC/3ABD
  уже удалены слиянием. Не удалять пользовательскую выноску «сущ сеть».
- Точные fixture handles и результаты: игнорируемый файл
  releases/native-tests/2026-10-21/native-lisp-results.txt. Тестовый код —
  tools/native-lisp-regression.lsp; загрузка сама ничего не выполняет.
  DWG не сохранялись. После возвращения к тестам удалять только подтверждённые
  собственные fixtures, без глобального purge и без сохранения оригиналов.
- Следующий шаг в свободное окно: убрать fixtures, установить разрешённый TTF,
  загрузить новую DLL через контролируемый перезапуск, повторить BWUNICODE,
  стрелку без текста и Undo/Redo с чтением через MCP. Затем выполнить готовый
  tools/check-dotnet-mcp.py --live-documents --document-paths с двумя полными
  путями выше: проверяет чтение обоих DWG, изоляцию сессий, отказы неактивной
  записи и сохранение вкладки/выделения/вида/DBMOD. Эти флаги выбирают native-
  проверку нескольких документов и задают их точные пути; сейчас не запускать.
- Завершить полный .NET-прогон и документацию контрактов в существующих
  AGENTS/TOOLS/CAD_WORKFLOW. До этого не объявлять доработку готовым выпуском.

## Простая MLeader и чтение текущего выделения — 2026-10-21

- Доработан существующий dwg_create_leader: ровно две разные WCS-точки одной
  отметки, прямая сплошная линия, closed-filled стрелка, без полки/dogleg/рамки
  и аннотационного масштаба. Явные height/arrow_size/text_style; размеры по
  умолчанию 2.5. Текстовый стиль проверяется и при использовании default.
  Текущие стили/системные переменные не переписываются. Новый слой и MLeader
  создаются одной транзакцией, до Commit читается результат.
- Единственная MCP-регистрация перенесена в существующий TextCreationTools,
  доступный drawing/annotation; прежний AnnotationTools.CreateLeader оставлен
  только forwarding helper без атрибута MCP. Parser и native factory общие.
  Отдельный LISP/сценарий создания выноски не добавлен. PLLENLEADERS сохраняет
  свою задачу: записывает длины в уже существующие выноски.
- Добавлен один dwg_get_selection: SelectImplied текущего pickfirst, без prompt,
  изменения выбора или поиска других объектов. По умолчанию до 100 строк без
  подробной геометрии, предел 500. Identity документа, handles, типы native,
  полный selected_count и ограниченный returned_count, отдельные ошибки строк,
  complete/truncated; type_counts относится только к возвращённым строкам.
  Пустой/отключённый выбор явно отмечен, ошибки чтения не маскируются пустотой.
  Существующий dwg_get_selected_texts отдельно извлекает/группирует текст.
  Правило сначала определять выбранные объекты закреплено в AGENTS и server
  instructions; контракты — в TOOLS, без нового файла инструкций.
- Offline: **1449 passed, 0 failed, 0 skipped**. Добавлены проверки входов,
  частичного подсчёта, реальных SDK-схем и единственности MCP-регистрации.
  При прогоне обнаружена гонка закрытия тестового named pipe; IOException
  перехватывается только при уже отменённом токене, рабочие ошибки не скрыты.
  Плагин 2026 собран без предупреждений, 2027 — с прежними двумя Roslyn
  MSB3277, без ошибок. Реальный Python MCP SDK: по восемь профилей для
  2026/2027, штатный 63, с annotation 64, read-only 24; повторных имён нет.
  Только initialize/tools-list, без CAD-вызовов. Ссылки/fences текущих инструкций
  и git diff --check — PASS.
- Проверки исходников не подтверждают native-геометрию, вид стрелки, сохранение
  pickfirst или Undo/Redo. По договорённости live-тесты — после обновления:
  смешанное/пустое/ограниченное выделение, MLeader с текстом и без, Z не 0,
  необычный текущий стиль, Undo/Redo и отказ без частичной записи.
  Установленные/загруженные EXE/DLL, MCP config и DWG не менялись; установщик
  не собирался. Предыдущие незакоммиченные правки сохранены в andrew.
  Следующий шаг: принять исходники и выпустить обновление; Git остаётся пользователю.

## Полный вынос перевода и фирменного оформления — 2026-10-21

- По прямому запросу пользователя оба кандидата удалены из MCP, а не спрятаны
  в необязательном профиле: dwg_translate_and_rewrite и dwg_apply_unicode_style.
  Удалены handlers/DTO/dispatch/schema/ToolBaker whitelist, C# Unicode service,
  скрытые applyUnicodeStyle/finalScale и старый подбор размеров. Их tests
  удалены вместе с реализацией; актуальные parser/schema/profile tests добавлены.
- Перевод — единый сценарий в CAD_WORKFLOW, оформление — единственный
  lisp/apply_unicode_style.lsp: BWUNICODE и bw:apply-unicode(handles,font_path).
  Общие LISP из предыдущего этапа сохранены. Шрифт перемещён в lisp/ с
  совпавшим SHA256; plugin outputs больше его не содержат. В метаданных самого
  TTF обнаружена SIL OFL 1.1 вместо прежней ошибочной пометки Apache; NOTICE
  исправлен и официальный текст источника сохранён в lisp/OFL.txt.
- LISP не скачивает/не устанавливает шрифт, не сохраняет DWG и не меняет его
  при загрузке. Полный preflight, одноразовое масштабирование 0.60/0.90/0.82,
  сохранение цветов и геометрии, группа Undo, readback всех целей и ограниченный
  отчёт. Несовместимый существующий стиль/слой не перезаписывается. Возврат
  ok=nil отдельно проверяется даже при completed у MCP-job; property restore
  после ошибки не обещает отката всего DWG или истории Undo.
- Обычные тексты определений блоков доступны по явным handles и затрагивают
  все ссылки; анонимные/динамические определения, вложенные block references,
  annotative/fields/inline overrides и прочие неподдержанные случаи отказывают.
  ActiveX Block.IsDynamicBlock сверен с Autodesk; неизвестные DXF-метаданные
  для определения типа не используются.
- GetSelectedTexts теперь возвращает снимок с owner/local/WCS/bounds metadata;
  скрытый ClusterStateStore удалён. UpdateTexts меняет только текст.
  CollapseAndRewrite — нейтральный явный контракт: identity текущего DWG,
  уникальные handles, локальные размеры; весь JSON проверяется до native.
  До записи каждого элемента проверяются типы, владельцы, слои, стили и общая
  плоскость/направление, после — текст/размеры/сохраняемые свойства. Merge
  обычных DBText/MText выполняется в их владельце, с правильным Z; атрибуты
  и MLeader не удаляются ради merge. Транзакции раздельные, общий Undo не заявлен.
- Offline: **1365 passed, 0 failed, 0 skipped**. Последняя server-only правка
  явных readOnly/idempotent hints дополнительно прошла все 8 .NET stdio-профилей.
  Реальный Python MCP SDK для 2026 и 2027: по восемь профилей, стандарт 61,
  annotation 63, read-only 23, modify 32; новые схемы и отсутствие двух
  удалённых имён подтверждены. Только initialize/tools-list, без CAD calls.
- Обе plugin DLL собраны: 2026 без предупреждений, 2027 с прежними двумя
  Roslyn MSB3277, без ошибок. Статически проверены UTF-8/размер/скобки/строки
  LISP и отсутствие исполняемых top-level действий; это не native-проверка
  ActiveX, шрифта, геометрии MLeader, Undo/Redo или отката транзакции.
  Ссылки/fences четырёх актуальных руководств, байты TTF и diff --check — PASS.
- Справочник дополнен текущими возможностями выносок: чтение и замена текста,
  простое создание через annotation, PLLENLEADERS; полного API стрелок/полок/
  стиля/блочного содержимого нет. Новых таких операций в этой задаче не добавляли.
- Исходники/build outputs изменены, предыдущие правки Undo/инструкций сохранены
  в andrew. Установленные и загруженные DLL/EXE, MCP config и DWG не менялись.
  Новый установщик не собирался; Git commit/push/merge остаются пользователю.
  Следующий шаг — принять исходники, выпустить/установить новую версию и провести
  отложенные пользователем native-проверки Undo и текста/LISP на scratch DWG.

## Исправление фоновой проверки углов / Undo — 2026-10-21

- По сообщению коллеги проверена текущая реализация: после каждой команды
  выполнялись обычная транзакция и блокировка документа даже без изменений.
  Autodesk описывает потерю Redo из-за StartTransaction/write-open внутри
  callback Undo/Redo; утверждение, что любая блокировка сама по себе всегда
  сбрасывает Redo, отдельно не доказано. Основание и порядок — в TOOLS,
  разделе угловых атрибутов.
- CommandEnded/Cancelled/Failed теперь только ставят уведомление в очередь.
  Работа выполняется на Idle активного, свободного документа. Чтение —
  OpenCloseTransaction без Commit и write-lock; при совпадении текста запись
  не начинается. План хранит IDs/значения, не открытые DBObject. Только при
  реальной правке берётся lock и заново проверяется весь план до первого
  UpgradeOpen. Сохранены лимиты, повторные наблюдения, подавление собственных
  уведомлений и освобождение состояния после ошибок. Database Undo не отключается.
- Offline: **1385 passed, 0 failed, 0 skipped**, в том числе 13 новых
  регрессионных случаев жизненного цикла: отсутствие lock при чтении,
  отложенные callbacks, запись под lock, ошибки/повторы, смена документа и
  очистка собственных уведомлений. Тестируется настоящий Lifecycle, но native
  обход/запись заменены заглушкой; это не эмуляция истории Undo AutoCAD.
- Плагины 2026/net8 и 2027/net10 собраны; только прежние Unicode/Roslyn warnings.
  Реальный MCP SDK подтвердил восемь профилей для каждого года: штатные 63,
  с annotation 65, read-only 23; схемы и фильтрация PASS. CAD-инструменты не
  вызывались. Прогон тестов потребовал доступ к собственным тестовым файлам
  в AppData, а SDK — чтение существующей конфигурации сервера. Независимое
  ревью не выявило блокирующих замечаний; diff --check и Markdown — PASS.
- Изменены исходники и build outputs. Установленные/загруженные DLL, EXE,
  конфигурация и DWG не менялись; установщик не пересобирался. Commit/push
  остаются пользователю. Сохранены предыдущие незакоммиченные правки andrew;
  pull посреди работы не выполнялся.
- Следующий шаг: после принятия исходников выпустить новый установщик, закрыть
  AutoCAD/Civil 3D, обновить и запустить заново. Пользователь проверяет в пустом
  несохранённом чертеже круг → Ctrl+Z → ожидание Idle → Ctrl+Y, затем несколько
  Undo/Redo подряд. Отдельно проверить grip → Idle → U → Idle → REDO → Idle
  привязанного блока: геометрия и угловой атрибут должны совпадать после
  восстановления, без лишнего шага U. Реальные производные записи всё ещё
  могут затрагивать Undo filing при UpgradeOpen; откат native-сбоя после
  начала записи также не покрыт заглушками. До native-проверки полная
  исправность Undo/Redo не заявляется.

## Аудит состава MCP — 2026-10-21

- Проверены source-регистрации, профили и фактический каталог dwg-2027: 63
  инструмента в query,modify,drawing,view. Инструменты не удалены: сначала
  нужна равноценная замена и проверка существующих вызовов.
- Главные кандидаты на прикладной необязательный профиль/команду:
  dwg_translate_and_rewrite (слияние текста и эвристики размещения) и
  dwg_apply_unicode_style (конкретные Bimwright/OpenSans/слой/масштабы).
  Общий порядок перевода/оформления можно описать сценарием или LISP, но
  существующие низкоуровневые средства пока не покрывают весь их контракт.
  Не писать параллельную реализацию до выбора замены; первым шагом отделить
  эти workflows от общего профиля, сохранив совместимость вызовов.
- Оставить базовые CAD-операции, настройки, чтение блоков и выделенного текста.
  dwg_collapse_and_rewrite обеспечивает единый проверяемый план переписывания
  и транзакцию; отдельные erase/create вызовы его не заменяют. Две rewrite
  обёртки уже используют общий RewriteExecutor. select_by_layer/type — простые
  фасады общего поиска, не отдельная дублирующая реализация для переноса в LISP.
- send_code, batch_execute и ToolBaker в штатном командном профиле скрыты.
  В upstream defaults остаются meta/all и включённый ToolBaker: отдельная
  очередь — согласовать defaults с командной поставкой, не объявлять все
  варианты конфигурации эквивалентными. Фоновая привязка углов — сервис
  плагина, а не лишний MCP-инструмент; исправление Undo не требует её переноса.

## Выбор между MCP, LISP и инструкциями — 2026-10-21

- В CAD_WORKFLOW добавлены критерии выбора, примеры текущих инструментов
  и порядок пересмотра LISP. Общность/сложность/частота не требуют переноса
  всех команд в MCP: базовые проверяемые операции моста отделены от
  самостоятельных прикладных команд и правил применения готовых средств.
- AGENTS, памятка и текст указателя в setup-codex.ps1 ведут к одному разделу.
  Настройка уже установленных клиентов автоматически не менялась.
  Уточнено, что LISP может иметь
  параметры/результат/Undo, а MCP не обещает атомарность автоматически;
  дублирование реализации и обходы ограничений запрещены. Основания сверены
  с официальными описаниями MCP tools и команд AutoLISP Autodesk.
- Продолжение текущих незакоммиченных правок в andrew; повторный pull не
  выполнялся, предыдущий перенос LISP сохранён. Изменены правила и текст,
  который установочный скрипт добавляет в инструкции; логика скрипта прежняя.
  .NET/LISP, установленный runtime и DWG не менялись, запусков CAD не было.
- Проверки: ссылки/якоря/указатели, Markdown fences, diff --check,
  PowerShell-синтаксис setup-codex и 11 примеров памятки — PASS.
  Скрипт установки не выполнялся, сборки и прежние тесты кода не повторялись.

## Общие LISP в репозитории — 2026-10-21

- По разрешению пользователя работа ведётся в andrew; перед правками выполнено
  штатное автообновление личной и локальной develop, все исходники актуальны.
- Три общих файла перенесены из D:\CAD-Automation\lisp в cad-bridge/lisp:
  netlen.lsp, polyline_lengths.lsp и polyline_lengths_to_leaders.lsp.
  Байты сохранены: хеши до/после совпали; старых копий этих файлов нет.
  Локальный sandbox.lsp с данными конкретного чертежа остался на месте.
- В существующей CAD_WORKFLOW добавлены единый порядок создания/обновления
  LISP и каталог команд; AGENTS и короткие указатели согласованы. Общая
  библиотека передаётся через Git и запускается из локального checkout;
  отдельные копии на NAS/в runtime и пересборка сервера для LISP не нужны.
- Перед переносом прочитан статус LISP: approvals/jobs пусты, busy=false.
  Сверка байтов трёх файлов, UTF-8, лимит 64 КиБ, отсутствие старых копий,
  сохранность sandbox, Markdown-ссылки/якоря и diff --check — PASS.
  LISP и изменяющие CAD-инструменты не запускались; новый путь требует
  пользовательского MCPTRUSTLISP. Установленные EXE/DLL и инструкции runtime,
  конфигурация и DWG не менялись; commit/push остаются пользователю.

## Подготовка исходников к следующей упаковке — 2026-10-20

- Пользователь разово поручил агенту commit/push/merge текущих правок инструкций.
  В origin перед публикацией andrew/develop на 55ca5d5; новых чужих правок нет.
  Для PR нет авторизованного GitHub-сеанса; слияние выполняется обычным Git
  по SSH с отдельным merge-коммитом, без force и изменения прав репозитория.
- Входы упаковщика, SDK 10.0.401, runtime .NET 8, AutoCAD 2027 DLL и кеш
  AutoCAD.NET 25.1.0 на месте. Свободный номер следующего пакета: 2.0.1-team.4.
  Команда из cad-bridge: powershell.exe -NoProfile -ExecutionPolicy Bypass
  -File .\tools\package-team-dwg.ps1 -Version 2.0.1-team.4.
- Объединённый код уже прошёл 1372 offline-теста, сборки обоих плагинов
  и MCP-каталоги. Последующие правки только документационные; повторная
  сборка и тесты кода не требуются. Настройки native/Civil 3D остаются
  отдельной непроверенной частью: упаковка не подтверждает их работу.
- Новый пакет пока не собирался, установка и CAD не затрагивались.
  Упаковку запускать после публикации с чистой принятой develop.

## Памятка для подключения сотрудников без переменных — 2026-10-20

- В TEAM_SETUP убраны переменные путей и ветки; общий путь исходников задан
  явно: D:\CAD-Automation\team\Projects. Установка и обновление выполняются
  относительными командами из терминала, открытого в папке установщика через
  Проводник. Отдельный переход на сетевой Z: не требуется.
- Добавлены свой GitHub, приглашение Collaborators, создание Ed25519,
  добавление публичного ключа в свой аккаунт и проверка SSH. Порядок сверен
  с официальными инструкциями GitHub; clone использует SSH.
- Сокращён повтор алгоритма автообновления: его источник остаётся AGENTS.md.
  Предыдущие незакоммиченные правки сохранены, pull посреди работы не выполнялся.
  diff --check и синтаксис 11 PowerShell-примеров — PASS; переменных в памятке нет.
  Это документация; сборка, установка и Git commit/push не выполнялись.

## Автообновление локальной develop — 2026-10-20

- По запросу пользователя правило перед правками теперь обновляет и локальную
  develop без переключения с личной ветки. Только fast-forward; проверяются
  upstream, чистота, незавершённые операции и занятость ветки другим worktree.
  В конце проверяется включение develop в личную ветку; при любом отказе — стоп
  перед правками, без force и отката уже выполненных обновлений.
- На чистой andrew выполнены обе команды pull и fetch локальной develop:
  всё уже актуально, andrew/develop и их origin-ссылки указывают на 55ca5d5.
  Проверка ancestry успешна, текущая ветка и upstream сохранены.
- Изменена только документация: AGENTS, памятка и технический указатель.
  diff --check и синтаксис девяти PowerShell-примеров памятки — PASS.
  Сборка и установка не требуются; commit/push остаются пользователю.

## Навык подсчёта ФБС по профилю — 2026-10-20

- Добавлен `.agents/skills/count-fbs-profile`: SKILL.md, UI-метаданные и
  контрольный пример Ивакино. Существующие typed-инструменты читают рамки,
  MLeader/MText и геометрию; новые методы MCP и изменения DLL не требуются.
- Правила: сопоставление стрелки с конкретным профилем/интервалом таблицы,
  умножение количества на число труб, дедупликация совпадающих выносок,
  явное разрешение конфликтов 2/3. Близость двухтрубного выпуска не даёт
  основания удваивать ФБС основного однотрубного профиля.
- Контрольный пример: 8/52/13/40/8 = 121 шт.; для пяти спорных выпусков
  корпуса 1.2 пользователь выбрал 3 шт. на одну трубу. Числа не являются
  универсальными значениями для других чертежей.
- Offline: quick_validate и YAML/ссылки/контрольная арифметика проверены.
  Изменены только инструкции и документация. Сборки, загруженный runtime,
  MCP-конфигурация и DWG не изменены; новых live-тестов не запускали.
- Навык хранится в репозитории; глобальная установка не выполнялась.
  Commit/push/merge остаются пользователю.

## Объединение инструментов настроек и новой структуры — 2026-10-20

- Сведены файлы andrew c9110a6 и принятой develop 44e8951 с доработками alexandr.
  Сохраняются упрощённая структура, общие инструкции, автообновление исходников
  и инструменты перекрестия/настроек. Единственная реализация остаётся в
  cad-bridge/dwg-mcp. Все 15 новых C#-файлов сохранены в новом пути; девять
  Preferences перенесены вручную, прочие уже разместил Git. Включения server/test
  csproj сохранены. Старого upstream на диске нет; Git index ещё ждёт user add.
- Контракты двух настроек объединены в TOOLS.md; канонические AGENTS, README,
  CAD_WORKFLOW и упаковщик согласованы с новой структурой. Python-проверка
  обновлена на 63/65 инструмента и read-only 23, query 16, modify 34.
- Проверки именно объединённого результата: **1372 passed, 0 failed, 0 skipped**;
  обе DLL (2026/.NET 8, 2027/.NET 10) собраны, только прежние Unicode/Roslyn warnings.
  Реальный MCP SDK прошёл восемь профилей для target 2026 и 2027: 63/65/23,
  корректная уникальность и read-only фильтрация; только initialize/tools-list.
  Прежние итоги ниже относятся к отдельным веткам. Нативная работа новых настроек
  в AutoCAD/Civil 3D ещё не подтверждена.
- Новый ZIP не собирался; установленный runtime, DLL, конфигурация и DWG
  не менялись. Установка, live-проверки, сохранение DWG и Git commit/push
  не выполнялись. Завершение merge и публикация остаются пользователю.

## Автообновление перед правками — 2026-10-20

- Пользователь разрешил автоматический pull перед новой задачей. Правило внесено
  в канонический AGENTS; корневой AGENTS только ссылается на него, памятка и
  CAD_WORKFLOW согласованы. Новых скриптов, hooks или копий инструкций нет.
- При чистой личной ветке с правильным upstream агент выполняет pull --ff-only
  своей ветки, затем pull --ff-only origin develop. Ни переключения ветки,
  ни нового merge-коммита; расхождение/ошибка/существующие правки останавливают
  новые изменения. Обычные merge, commit и push остаются пользователю.
- Сейчас папка содержит незакоммиченную уборку на andrew: pull не выполнялся,
  предыдущие изменения сохранены. Эта правка только документационная;
  сборки, установки и проверки AutoCAD не повторяются.

## Упрощение структуры и документации — 2026-10-19

- Убраны три лишних уровня: upstream/dwg-mcp → dwg-mcp, packaging/team →
  packaging, releases/team/<версия> → releases/<версия>. Внутренняя структура
  импортированного .NET-проекта сохранена; plugin-acad24/App.cs остался на месте.
  Рабочий каталог Projects и отдельные пользовательские копии не переносились.
- Пять справочников STYLE_TOOLS/TEXT_TOOLS/BLOCK_INSPECTION/BLOCK_VARIANTS/EOM_TOOLS
  объединены в docs/TOOLS.md. Параметры, guards, единицы, транзакции, Undo/live
  ограничения и внешние ссылки сохранены. Убраны дубли установки/Git и старые
  количества инструментов. AGENTS сокращён до общих правил; README — навигаторы.
  Корневые AGENTS остаются короткими указателями, отдельные правила там не ведутся.
- Удалены три перевода upstream README, устаревшие CONTRIBUTING, ARCHITECTURE,
  fresh-install-checklist и lib/acad24/README. Актуальные сведения об архитектуре
  и compile references перенесены в CAD_WORKFLOW. LICENSE, THIRD_PARTY_NOTICES,
  font README, upstream CHANGELOG и provenance сохранены. Исторические пути и
  хеши в IMPORT_MANIFEST/patches и прежних записях этого журнала не переписывались.
- В корневом D:\CAD-Automation\.mcp.json старый style-tools DLL заменён на уже
  установленный server/current/dwg-mcp.exe. Codex, VS Code и известные клиентские
  конфигурации сверены; процессы dwg-mcp и загруженная Acad27 DLL уже используют
  штатную локальную установку. Рабочие EXE/DLL не заменялись, клиенты не перезапускались.
- Удалены проверенные generated stage/plugin-build у team.3 и team.4-rc.1,
  старая runtime-копия D:\CAD-Automation\releases и воспроизводимые кеши — около
  301 МиБ. Три ZIP и их контрольные суммы сохранены без изменения; одна распаковка
  текущего RC сохранена в releases/2.0.1-team.4-rc.1. Уникальные lisp/scripts,
  legacy, .venv и совместимые короткие entrypoints оставлены.
- После переноса: **1288 passed, 0 failed, 0 skipped**; плагины 2026/.NET 8 и
  2027/.NET 10 собраны, 0 ошибок, только прежние Unicode/Roslyn warnings.
  Реальный MCP SDK: style-схемы для обоих годов PASS; text/style/block matrix
  восьми профилей PASS (58/60 инструментов, read-only 20), без CAD-вызовов.
  В check-text-mcp.py исправлены устаревшие expected counts до LISP-интеграции.
  Первые проверки ограничила sandbox-доступность NuGet/runtime config; с нужным
  чтением пользовательской конфигурации проверки прошли, продуктовый код не менялся.
- 343 перенесённых исходных файла сверены с Git index без изменений; отдельно
  обновлены README и семь комментариев со ссылками. Python/PowerShell syntax,
  актуальные Markdown-ссылки/anchors/fences и diff --check прошли. Упаковщик
  использует новые пути и единый TOOLS; готовый установщик не пересобирался.
  Установка, live-проверки, сохранение DWG и Git-мутации не выполнялись.
- При объединении EOM-описания сохранено происхождение: запрос «Уточнить задачу
  по ЭОМ» от 2026-10-08; text/style интеграция 2026-10-09, создание сущностей и
  правка вершин — 2026-10-16. Single Undo и stale/live traces остаются отдельными
  нативными проверками, как и до уборки.
- Следующий шаг: пользователь просматривает и фиксирует переносы/документацию
  в своей ветке; дальнейшие сборки используют releases/<версия>. Исторические
  пути ниже относятся к месту файлов на момент записи, текущая карта — в README.

## Общие настройки AutoCAD через MCP — 2026-10-19

- Добавлены `dwg_list_settings` и `dwg_get_settings` (query), `dwg_set_settings`
  (modify). Каталог: 122 свойства в девяти группах, 43 с записью, 79 read-only
  с объяснением ограничения. Наличие в каталоге не подтверждает доступность
  нативного свойства: чтение возвращает available/error отдельно по каждому ID.
- Реализация общая для 2026/2027: фиксированный allowlist штатных
  Application.Preferences, типизированные вложенные MCP DTO, строгие JSON-типы,
  диапазоны/enum, точный профиль и прежнее значение каждого ID, полная
  предпроверка до setters, dry-run/no-op, чтение результата и проверяемое
  восстановление. Пакеты по 1–32 ID. Нет произвольного пути к API/реестру,
  смены профиля, записи путей, запуска печати/сохранения. Настройки вне DWG/Undo.
- OLE-ожидания сохраняют unsigned raw-биты; новые Display-цвета только RGB,
  цвета маркеров/ручек — ACI Integer. Прежний crosshair API использует общий
  нативный адаптер, публичный контракт сохранён. READBACK подтверждает хранимые
  значения, не UI/будущие эффекты. На сбое rollback_verified и перечитывание;
  другой профиль не изменяется, атомарность не обещается.
- Offline: **1372 passed, 0 failed, 0 skipped** (57 новых cases к crosshair
  кандидату), включая реальные вложенные схемы SDK и stdio восьми профилей.
  В рабочем профиле 63 инструмента, read-only 23; setter в read-only отсутствует.
  Оба плагина успешно собраны: Acad26/.NET 8 с AutoCAD.NET 25.1.0 и
  Acad27/.NET 10 с установленными compile refs. Старые предупреждения
  Unicode/WebClient и Roslyn остаются, новых ошибок сборки нет.
- Контракт/ограничения/официальные источники: docs/AUTOCAD_SETTINGS.md;
  инструкция включена в штатную упаковку и canonical AGENTS.md.
- Итоговый тестовый пакет: releases/team/2.0.1-team.4-rc.4-settings,
  41 274 780 байт, dirty=true (не стабильный выпуск), SHA256
  `8b2ff4f07baf94214aca414cbd2e995079c4382dc84a70a7d0151d8629e528ec`.
  Проверены все 48 файлов ZIP по размеру/SHA256, обе DLL, отсутствие Autodesk
  host DLL и точное совпадение runtime AGENTS/инструкции с исходниками.
  Упакованный EXE проверен SDK 1.1.0 через stdio для 2026/2027 и read-only:
  tools/list 63/23, статический каталог 122/43. CAD-вызовов не было.
  Установщик прошёл WhatIf для обоих годов, конфигурация не изменялась.
  Предыдущий rc.3-settings сохранён неизменным; его заменяет rc.4 с запретом
  неизвестных DTO-полей на этапе SDK-десериализации. Полный suite после этой
  правки снова 1372 passed; оба плагина пересобраны упаковщиком.
  Установленный runtime остаётся team.4-rc.1, SHA загружавшейся DLL прежний
  ab785c70fa3edf2d2dab5f84d85a22f63e660b6f4d3d291aeca518adcef1b392.
  Журнал обновлён после упаковки; ради этого ZIP не пересобирать.
- Установка, загруженная DLL и live не изменялись. Следующий шаг после проверки
  отдельного тестового ZIP — пользовательская установка при закрытых CAD/Codex,
  затем явно разрешённые get → dry-run → set → get для выбранных настроек.
  Проверить COM-dispatch/enum setter в настоящем хосте, визуальные эффекты,
  модель/лист, сохранение профиля после перезапуска. DWG не сохранять.

## Настройки перекрестия через MCP — 2026-10-19

- В ветке alexandr подготовлены `dwg_get_crosshair_settings` (query) и
  `dwg_set_crosshair_settings` (modify): размер 1–100 и отдельные RGB модели/листа.
  Исправляется сценарий, в котором выбор цвета в UI ошибочно принимался за
  фактическое изменение. Новые инструменты читают настоящие свойства
  Application.Preferences.Display внутри плагина и проверяют результат записи.
- Изменяются только три явно перечисленных свойства. Точный профиль берётся
  из getter; неизвестные поля/типы/диапазоны отвергаются до записи. Для обоих
  пространств нужно передать оба цвета. Настройки не являются DWG-транзакцией
  или Undo: при сбое выполняется восстановление с `rollback_verified`, без
  автоматического повтора. Чужой профиль при восстановлении не изменяется.
- Offline: **1315 passed, 0 failed, 0 skipped**, включая 27 новых cases:
  раздельные контексты, порядок RGB/OLE, dry-run/no-op, профиль, игнорирование
  setters, изменение перед исключением, отказ восстановления и MCP SDK-схемы.
  Реальные MCP stdio initialize/tools-list восьми профилей: рабочий каталог
  60 инструментов, read-only 21; setter отсутствует в read-only. CAD не вызывался.
- Собраны плагины Acad26/.NET 8 (AutoCAD.NET 25.1.0) и Acad27/.NET 10.
  У этого компьютера нет системного SDK: локально подготовлены SDK 10.0.402 и
  runtime 8.0.32 в D:\CAD-Automation\work\dotnet-build; зависимости также в work.
  Существующая установка AutoCAD, её DLL и MCP-конфигурация не заменялись.
- Контракт/ограничения/официальные API описаны в docs/CROSSHAIR_SETTINGS.md;
  инструкция добавлена в штатную упаковку. Собран отдельный тестовый ZIP
  2.0.1-team.4-rc.2-crosshair с AllowDirty/dirty=true, не стабильный выпуск.
- Пакет в releases/team/2.0.1-team.4-rc.2-crosshair, 41 207 982 байта;
  SHA256 `07e3c5d2a789957b084507156e9d5234232945d4aef0bb333460a83876697b74`.
  Manifest фиксирует commit 975b3a2ea99e2b49ccf2e061d826fe60bcf327c0 и dirty=true.
  Все 47 файлов финального ZIP проверены по размеру/SHA256; обе DLL присутствуют,
  Autodesk host DLL не включены, runtime AGENTS и новая инструкция точны.
  Упакованный EXE проверен официальным MCP SDK 1.1.0 через stdio для обоих годов:
  60 инструментов, read-only 21, типизированные RGB и expected_profile в схеме.
  Выполнялись только initialize/tools-list, без CAD-вызовов. Установщик прошёл
  WhatIf для 2026 и 2027. Прежняя установленная DLL всё ещё имеет SHA
  ab785c70fa3edf2d2dab5f84d85a22f63e660b6f4d3d291aeca518adcef1b392;
  установленный runtime по release.json — team.4-rc.1. Старые пакеты не менялись.
  Запись результатов добавлена после упаковки; ради журнала ZIP не пересобирать.
- Native/live ещё НЕ проверены: доступность COM-свойств в AutoCAD/Civil 3D,
  немедленное обновление курсора, цвет модели/листа после повторного открытия
  Options и сохранение настроек профиля после перезапуска. Следующий шаг —
  пользовательская установка согласованного ZIP при закрытых CAD/Codex и
  явно разрешённый цикл get → dry-run → set → get. DWG не сохранять.

## Единая памятка подключения и обновления — 2026-10-19

- По уточнению пользователя docs/TEAM_SETUP.md сокращён с 401 до примерно
  180 строк и трёх процессов: новая машина; доработка/commit/push/PR из личной
  ветки; сборка у Андрея и обновление всех машин. Все участники разрабатывают.
- Доступ — через собственные GitHub-аккаунты и Collaborators; для новых машин
  выбран HTTPS с Git Credential Manager. Личные постоянные ветки сразу получают
  upstream, перед работой включают origin/develop. Git-мутации остаются человеку.
- ZIP распаковывается один раз в неизменяемую папку версии на NAS. Установка
  запускается прямо оттуда и копирует runtime локально; возможность проверена
  по установщику. Ручная сверка SHA исключена, встроенная manifest-проверка
  сохранена. README, CAD_WORKFLOW и указатель INSTALL-RU согласованы с памяткой.
- Проверены diff, Markdown fences и синтаксис 9 PowerShell-блоков без исполнения;
  независимый review существенных ошибок не выявил. Изменена только документация:
  код, готовые ZIP и установки не менялись; сборки/тесты и Git-мутации не выполнялись.

## Исправление автозагрузки AutoCAD/Civil 3D 2027 — 2026-10-19

- Причина подтверждена отчётом коллеги: System.ArgumentNullException, key,
  Dictionary.ContainsKey -> BlockAngleBindingService.Attach -> OnActivated.
  При активации стартовой вкладки приходит Document=null. В сохранённом
  минидампе этого компьютера найдены те же Attach/OnActivated и тип исключения.
  Перенос старого bundle у коллеги устранил сбой обычного старта Civil 3D 2027;
  runtime коллеги не патчился. Ранее указанные причины по потокам были гипотезами.
- Исправлен lifecycle синхронизации углов: null event/document, отсутствующая
  или disposed база пропускаются; контекст публикуется после полной подписки;
  неполные подписки откатываются. Поздняя готовность базы обрабатывается на Idle.
  Native callbacks и весь preflight защищены от выхода managed-исключения в хост.
  Lifecycle вынесен в partial BlockAngleBindingService.Lifecycle.cs; геометрия,
  форматирование, ограничения проходов и сохранённые bindings не дублировались.
- App.Initialize только назначает отложенный запуск на первом Idle с готовым
  документом. MCPSTART использует ту же последовательность служб; MCPSTOP и
  Terminate отменяют ожидающий старт. При неполном старте службы освобождаются,
  диагностический вывод не бросает исключение. PluginLog.Error сохраняет стек
  в прежнем debug.log. Сохранён строгий main-thread dispatcher без перепривязки.
- Offline итог: **1288 passed, 0 failed, 0 skipped**, 8 секунд. Новые 37 cases
  используют реальные lifecycle/App/DocumentInvoker: null activation, позднюю
  базу без повторного события, cleanup/повторный старт, отмену pending startup,
  ready Idle, native getter/lock/refresh failures и отказ необязательного toast.
  Геометрический проход и периферия WPF/transport заменены явными test doubles;
  реальные CAD-вызовы/подключения/сохранения эти тесты не выполняют.
- Обе DLL собраны: Acad26/.NET 8 (compile-only AutoCAD.NET 25.1.0) и Acad27/.NET 10.
  Только прежние Unicode/Roslyn warnings, 0 ошибок. Независимый review не нашёл
  блокирующих замечаний. Deferred Idle сверялся с официальным паттерном Autodesk.
- Собран один тестовый пакет 2.0.1-team.4-rc.1 в releases/team, с явным
  AllowDirty и честным manifest dirty=true: это проверка до пользовательского
  коммита, не принятый стабильный выпуск. Старый team.3 не перезаписывается.
  На этапе сборки установленные DLL/конфигурация/чертежи не менялись; live-проверка
  была оставлена пользователю. После offline-проверки пакета пользователь устанавливает RC
  при закрытых AutoCAD/Codex и проверяет Start tab -> новый документ -> чтение MCP.
  Только после этого пользователь фиксирует/принимает изменения для штатного выпуска.
- Архив DwgMcp.Setup-v2.0.1-team.4-rc.1-win-x64.zip: 39 955 697 байт,
  SHA256 `c99617035a2f3981a040cd6b92ddd21bac1a693b3f23be55985e7050c434aa76`.
  Проверены все 46 manifest-файлов, обе DLL и runtime AGENTS; manifest фиксирует
  commit 5ac70b5676b9a117d6be61b74fc08dda2f0c38fa, dirty=true. Установщик прошёл
  WhatIf для обоих годов. Упакованный EXE прошёл реальные MCP initialize/tools-list:
  58 инструментов, 20 read-only, для 2026/2027, без CAD-вызовов.
  SHA старого team.3 не изменился. Эти результаты записаны после упаковки;
  ради дополнения журнала архив не пересобирать.
- Пилот AutoCAD 2027 этого компьютера прошёл: пользователь установил RC,
  запустил AutoCAD обычным способом и создал чертёж без NETLOAD. В процессе
  acad PID 14616 загружена DLL из штатного ApplicationPlugins/Bimwright.Dwg.bundle,
  SHA256 `ab785c70fa3edf2d2dab5f84d85a22f63e660b6f4d3d291aeca518adcef1b392`
  совпадает с проверенным RC. Runtime release.json подтверждает team.4-rc.1;
  процессы MCP используют server/current/dwg-mcp.exe.
- Реальный debug.log подтверждает отложенную автозагрузку в 12:07:10 и
  запуск pipe BimwrightDwg-2027-14616 в 12:07:25. Запрос dwg_get_drawing_info
  вернул ok=true: Чертеж1.dwg, несохранённый документ из Метры.dwt, модель,
  метры, DBMOD=0. Проверка только читала состояние, не меняла и не сохраняла DWG.
  Записи test-only-pipe/тестовых исключений ранее в этом debug.log относятся
  к offline doubles, а не к этому запуску AutoCAD.
- Следующий шаг: такой же обычный старт и чтение у коллеги в Civil 3D 2027.
  Эта проверка AutoCAD не заменяет пилот Civil 3D/2026 или проверки write-tools.

## Сбой при запуске после установки team.3 — 2026-10-16

- Пользователь сообщил о пяти авариях AutoCAD 2027 при запуске после установки.
  Установлены team.3 server/release.json и bundle только для R26.0/Acad27.
  Autodesk CER подтверждает e0434352 в KERNELBASE.dll в 15:47 и 15:56;
  доступные записи не содержат типа managed-исключения или его стека.
  В Application/.NET Runtime соответствующей записи не найдено.
- Причина пока не установлена. В коде найдены кандидаты: проверка потока
  MainThreadExecutor.RunPending из Idle и native-preflight/event callbacks
  BlockAngleBindingService без внешнего catch. Без стека не считать их причиной
  и не подменять диагностику исправлениями наугад.
- Пользователь отключил автозагрузку bundle: падение сохранилось. После
  перезагрузки компьютера AutoCAD запустился; пользователь выполнил NETLOAD
  вручную. Причинная связь падения с мостом не установлена; сам по себе этот
  результат не подтверждает исправность автоматической загрузки.
- После этого реальный dwg-2027/dwg_get_drawing_info ответил ok=true:
  активен ИСП-004104-2026_пробный.dxf, model, метры, слой 0, DBMOD=16.
  В текущем чате доступны 58 инструментов, включая LISP и atomic create.
  Это успешная проверка связи и чтения; новые write-операции не проверялись.
  Дополнительная проверка пути загруженной DLL не выполнилась: автоматическая
  проверка разрешения диагностической команды не завершилась вовремя.
- Автозагрузку пока не включали обратно; повторный обычный старт с включённым
  bundle остаётся отдельным шагом пользователя. Код, установленный runtime,
  конфигурация и DXF этой проверкой не менялись и не сохранялись.

## Выпуск 2.0.1-team.3 собран — 2026-10-16

- Пользователь принял подготовку выпуска в develop. Перед сборкой HEAD, andrew,
  origin/andrew и origin/develop совпадали:
  `355c486ea59e851ebcf85845a6606f2907b63b22`; рабочая папка была чистой.
  Штатный tools/package-team-dwg.ps1 выполнен один раз, exit 0.
- Готовый архив: releases/team/2.0.1-team.3/
  DwgMcp.Setup-v2.0.1-team.3-win-x64.zip, 39 950 508 байт; рядом файл .zip.sha256.
  SHA256: `499ed758069e5a3128975b8df599b6f3596d176b735c24ad06e4f81754bdb3de`.
  Manifest фиксирует указанный commit, dirty=false и годы 2026/2027.
- Проверены байты финального ZIP: точное соответствие всех 46 файлов manifest
  по размеру и SHA256, контрольная сумма самого ZIP, отсутствие дубликатов путей,
  выходов за каталог и Autodesk host DLL. Единственный runtime AGENTS побайтово
  совпадает с каноническим. release.json и PackageContents содержат оба года;
  XML указывает AutoCAD|Civil3D и серии R25.1/R26.0.
- Self-contained EXE запускается с --help. Реальный MCP SDK выполнил только
  initialize/tools-list упакованного EXE для 2026 и 2027: по 58 инструментов
  рабочего профиля query,modify,drawing,view и по 20 в read-only. Дубликатов нет;
  presence и обязательные параметры LISP проверены, write-инструменты исключены
  из read-only. Временная конфигурация не создавалась, CAD tools не вызывались.
- Установщик из stage прошёл -WhatIf для 2026 и 2027, exit 0: проверил пакет
  и показал план без установки и изменений конфигурации. Оба native плагина
  собраны штатной упаковкой: Acad26/net8.0-windows и Acad27/net10.0-windows.
  Остались прежние предупреждения Unicode и Roslyn, ошибок сборки нет.
  Код соответствует ранее проверенным 1251 тестам; полный прогон не повторялся
  ради инструкций и упаковки.
- Подготовлены переносимые инструкции установки, источник публикации NAS,
  58 инструментов и допуск LISP. INSTALL-RU сокращён до входной инструкции;
  технические подробности остаются в TEAM_SETUP/CAD_WORKFLOW. Старые ссылки,
  SHA team.2 и vendor-описание запрещённого LISP не выдаются за новый выпуск.
  Корневой README пакета теперь генерируется как указатель на INSTALL/manifest.
- Упаковщик требует явную версию и чистую рабочую папку. AllowDirty допускается
  только явно для тестовой сборки; manifest сохраняет dirty=true. FinalizeOnly
  проверяет исходный commit/версию stage и не перезаписывает выпуск с SHA256.
- Windows PowerShell 5.1 parsing и шесть проверок подготовки прошли: явная версия,
  отказ dirty до создания output, защита опубликованного team.2, несовпадающие
  commit и версия stage. Тестовые fixtures удалены; старый ZIP не изменился.
- Пользовательские файлы из отдельной папки lisp в этот пакет не включаются.
  Следующий шаг: пилотная установка и явно разрешённые живые проверки новых
  функций, затем публикация ZIP и SHA256 в новой папке версии на NAS; путь пока
  не указан. Установка, конфигурации MCP и DWG в этой сборке не менялись.
  Эта запись добавлена после упаковки; ради записи журнала архив не пересобирать.
  Git-операции остаются пользователю.

## Объединение доработок ГИПа и AutoLISP — 2026-10-16

- Пользователь сохранил наш LISP runner коммитом `a60fabf` в `feat/lisp-runner`
  и начал merge `origin/develop` (`fb1381f`, PR #5 с доработками ГИПа).
  Содержимое пяти конфликтов объединено в рабочей папке: AGENTS.md, README.md,
  docs/CAD_WORKFLOW.md, App.cs и Program.cs. Индекс и завершение merge оставлены
  пользователю: до его `git add` Git продолжит показывать эти файлы как UU.
- Сохранены оба набора: trusted LISP jobs/status и инструменты текста, блоков,
  вариантов, угловых атрибутов, создания объектов и редактирования вершин.
  В App сохранены lifecycle обеих подсистем. Обновления BlockAngleBindingService
  откладываются, пока LISP queued/running; уведомления остаются в очереди.
  Этот интеграционный guard собран для обоих годов, native-проверка ещё нужна.
- Общий итог: **1251 passed, 0 failed, 0 skipped**, 7 секунд. Включены реальные
  MCP SDK 1.1.0 stdio initialize/tools-list восьми профилей, без CAD-вызовов.
  Рабочий профиль query,modify,drawing,view: 58 инструментов; с annotation: 60;
  read-only: 20. Проверены presence/required/annotations LISP и typed items новых
  инструментов. Read-only не регистрирует write-инструменты.
- Исправлены обнаруженные проблемы тестов: nullable JSON Schema принимается
  только как основной тип плюс null при сохранении items/properties/required;
  тест точной elevation создаёт новый JArray, чтобы Json.NET не пропускал замену
  соседнего double как приблизительно равную; fake named pipe продолжает accept
  после штатного разрыва соединения. Ошибки responder не глушатся, три запроса
  проверяются без прежних повторов и с ограниченным ожиданием.
- Первый запуск в sandbox не стартовал из-за связи VSTest/datacollector.
  Следующий прогон вне sandbox выявил пять падений проверок и был прерван после
  45 секунд зависания fake named pipe. После перечисленных исправлений финальный
  полный прогон завершился успешно; production PluginClient не менялся.
- Сервер net8.0 и native плагины Acad26/net8.0-windows и Acad27/net10.0-windows
  собраны. Для 2026 использованы compile-only AutoCAD.NET 25.1.0; для 2027 —
  установленный AutoCAD и .NET SDK 10.0.401. Сохранились прежние предупреждения
  Unicode и Roslyn 4.8/4.10, новых ошибок сборки нет.
- Рабочие пути в общих инструкциях привязаны к локальному checkout, а не к пути
  другого компьютера. Результаты сборок в штатных ignored bin/obj; служебные
  результаты проверок — в upstream/dwg-mcp/artifacts/test-results.
- Git-мутации, упаковка ZIP, установка runtime, CAD-вызовы и сохранение DWG в
  этой интеграции не выполнялись. История установок/live ниже принесена из ветки
  ГИПа и относится к соответствующим сессиям; не переносить её на этот компьютер.
  Следующий шаг: пользователь просматривает diff, делает git add и merge commit;
  затем публикует рабочую ветку/PR. Новую установку и живые проверки планировать
  отдельно, сохранив незавершённые проверки угловых атрибутов и LISP.

## Angle-sync установлен, ожидается открытие DWG — 2026-10-14

- После просьбы сохранить чертёж и полностью закрыть AutoCAD пользователь
  сообщил «закрыл». До штатной установки acadprocesscount0. Installer поставил
  localcandidate2.0.1-angle-sync.20261014 для2026 сClientnone в прежние bundle
  иserver/current, exit0. Native bundle matchverified, server --help checkOK;
  существующий toolsets сохранён, конфигурация Codex не менялась.
- Postinstall DLL/apphost SHA256 совпали с проверенным пакетом; plugin
  `485db065255dc838e19d5a287fcfd036dcced4daa105419e6e23de708899650d`.
  Installedrelease.json подтверждаетangle-sync. AutoCAD ещё закрыт: новая DLL
  установлена, но пока не загружена и live grip/Properties не проверены.
  Используемые старые MCProllbackкопии вручную не удалялись.
- Этот ход не открывал, не редактировал и не сохранял DWG. Сохранение текущих
  пользовательских правок будет установлено fresh inspection после открытия;
  сообщение «закрыл» само по себе не доказывает принятие saveprompt.
- Следующий шаг: открыть прежнийDWG, freshidentity20F76 inspection и persistent
  native-display binding. Затем actualУгол2grip с видимой выноской/атрибутом;
  подтвердить текущий, а не предыдущий угол без снятия выделения.

## Исправление отставания углового атрибута — подготовлено 2026-10-14

- Пользователь сообщил: после ручки атрибут показывает предыдущий угол.
  Fresh native inspector20F76: Угол1=242.31304181831666°, Угол2=84.19741996969823°,
  angularDimension20F88=84.19741996969833°, native formatted84,2° и20FB0=84,2.
  DBMOD5, прежние путь/fingerprint. Нативные данные уже совпадают; причина
  наблюдаемой задержки панели/порядка событий ещё не доказана. Readback сохранён
  в own cache20261014-user-one-step-lag.json. Пользователю задана необязательная
  проверка снять/повторить выделение только530; ответ пока не получен.
- Source: retained per-document Database event wrapper. ObjectModified только
  ставит watched bound refs/current evaluated dimensions в очередь, без tx/API
  mutations. OwnWrites отфильтровывает только реально записанные attr/parent IDs;
  lateDimension во время busy/lockrelease не игнорируется. CommandEnded/Cancelled/
  Failed пытается transactional pass до возврата; Idle делает следующие проверки
  после освобождения lock и до двух одинаковых read-only observations.
- Host-free DeferredAngleRefreshState: поколения внешних команд, native revision,
  in-flight lease, two-pass stable native-source signature, six passes/2000ms
  active time. Dirty native events не перезапускают истраченный бюджет. Refresh
  cooperative500ms, snapshot<=256KiB and SHA256/Base64 retained signature;
  definition/reference bounds сохранены. Exhausted/Failed отображаются явно.
- Changed attribute parent только RecordGraphicsModified(true), без selection,
  no-op geometry setters, dimension recomputation или DB-wide associative eval.
  Это документированный graphics signal, не доказанный Properties refresh.
  UpgradeOpen может начать Undo filing до DisableUndoRecording; single-Undo
  поведение не заявляется. Watchsets публикуются перед освобождением lock.
- Offline **1043/1043 tests passed**,16 новых virtual-clock temporal cases:
  late native updates после первого прохода/lock, superseded running generation,
  self-write events, same rounded text/different measurement, bounded retries,
  active-time accounting/monotonic clock. Native2026 compile passed,1 existing
  WebClient warning. Native2027 attempted: NETSDK1045 (.NET10 SDK unavailable).
- Независимый source review: ранее найденный snapshot8192 vs256KiB mismatch
  устранён SHA256 signature; Failed report added; status display exception caught.
  Нет selection/geometry/Save calls в новом refresh. Остаются actual grip,
  Properties immediate-update, Undo/cancel/reopen checks after loading candidate.
  В этом ходе DWG не изменялся и не сохранялся; новый плагин ещё не загружен.
- Готов local test candidate `releases/local/2026-10-14-angle-sync-acad26`,
  version2.0.1-angle-sync.20261014, native2026 DLL SHA256
  `485db065255dc838e19d5a287fcfd036dcced4daa105419e6e23de708899650d`.
  Matching server/profile reused unchanged from previous checked package;
  canonical docs copied,240file manifest generated and standard installer
  WhatIf/integrity checks passed (Clientnone, existing toolsets kept).
  Initial default staging failed AccessDenied before creatingdirectory;
  authorized elevated staging succeeded. No installation or CAD write yet.
  Next: user saves current unsaved drawing and closes AutoCAD; install prepared
  candidate through normal installer, reopen exactDWG, fresh identity/binding
  inspection and actual grip/Properties check without relying on rawsetter tests.


## Native angle-display связь заменена — 2026-10-14

- Пользователь открыл сохранённыйDWG. Freshinfo: прежнийпуть/fingerprint,
  Model, dbmod0. Prototype20F76 сохранён. Initial native inspector теперь читает
  evaluatedBTR20F77/angularDimension20F88: Measurement13.224234445641045°,
  FormatMeasurement `13,2\\U+00B0`, plain `13,2°`, oldattribute13. Saveduserstate
  уже13.2°, не ранее108.9°. Exactlyone angularDimension, complete, decimalunits,
  Dimadec1/Dimdec2, nofields/overrides/tolerances/limits.
- Guardedreplacement dry-run returned13,2, затем one committedreplace oldowned
  Xrecord21000 in place with `{tag:МАССА_ЕД._КГ,mode:dimension_display_degrees}`.
  Post-read attribute **13,2** equals nativeannotation **13,2°**. Reference/angles,
  effective definition geometry and every other attribute unchanged by replacement.
  No additional save (prioruserapproval covered save before reload, already done).
- Controlled setter verification attempted30/45.1/108.9/135.2 and globalorientation.
  Importantlimitation: setters change named properties and polylines but evaluated
  angularDimension points/Measurement/display remain13.2 throughout. Snapshot
  stringmatches therefore DO NOT prove changed-annotation/grip behavior. Native
  preview at108.9-param also shows drawnannotation13.2; no formulaoverride/fake
  recomputation was introduced. Getter is copying actualannotation as requested.
- User pose restored after tests. Raw values differ from initial bymachineepsilon
  (<1e-12rad); first exactfloatassert correctlyflagged this and was inspected,
  not treated as failure/automaticallyrepeateduncertainwrite. Reference/effective
  geometry same, originalAlpha4.229160400252334/Beta0.2308064321320833 preserved
  within nativefloatingtolerance. Finalannotation13,2°/attribute13,2.
- User asked via async input to drag actualУгол2grip on530 and confirm literal
  match with annotation. This UIcheck remains pending. Typedsetter andtwofresh
  reads are NOT declared evidence of actualGRIP/Idle/Undo/Properties behavior.
  Agentreview notes possible post-lock associativeevaluation race if dimension
  changes after Pendingremoved; not confirmed, noextraDLL/DBwideevaluation call.
  Only currentdisplaycopy passednative verification so far; fix is not saved yet.

## Установлен angle-display candidate — 2026-10-14

- После разрешённого сохранения пользователь сообщил «закрыл». Перед установкой
  acadprocesscount0. Штатный installer поставил2.0.1-angle-display.20261014
  для2026 сClientnone в прежниеbundle/server/current; config/profile не изменены.
  Installer verified Acad2026 and server executable checkOK; exitcode0.
- InstalledDLL/apphost SHA256 совпали сcheckedcandidate, DLLc7806bd10bedb6c9
  ab5ef0992a0124e335145a2ec838f068c0e981387cded8fd. Fresh installedstdio tools/list
  53, typedexpected_binding present, inspectorreadonly, CADcalls0.
  AutoCAD ещё закрыт: DLL пока не загружена, live nativeformatter/rebinding не
  выполнены. Используемые старые MCP rollbackкопии не удалены вручную.
- Независимое readonlyreview не обнаружило blocker установки; подтвердило
  currentevaluatedBTR/uniqueDimension/nativeformatting/transientnonDBMText и
  exactoldbindingguard. Остаются nativegrip/Undo/palettevalidation и ограничение
  общего времени Refresh дляbulkuse; массовое применение пока не разрешено.
- Следующий шаг: пользователь открываеттотже сохранённыйDWG, freshidentity
  и20F76inspection включаяevaluated_angular_dimensions; сопоставитьnative
  measurement/formattedtext с выноской, dry-runguardedreplacement, затемreplace
  старуюproperty_degrees связь наdimension_display_degrees безизменениягеометрии.
  Это исправление ещё не сохранено/не выполнено вDWG, ранее разрешённое
  сохранение сохранилопредыдущийпрототип+пользовательскиеизменения.

## Разрешённое сохранение перед исправлением — 2026-10-14

- Пользователь ответил «да» на прямой запрос сохранить текущийDWG перед
  перезапуском, чтобы сохранить530прототип и свои изменения. Это явное разрешение
  данного сохранения; прежний no-save этап завершён этим отдельным поручением.
- Fresh drawinginfo проверил полный путь Downloads/Отводы Для монтажнной схемы.dwg,
  fingerprint1e3a3e15-4e07-c947-a816-9105ed7ec14d, writable/titled, dbmod21.
  Один installed typed dwg_save_drawing(confirm=true, exactexpectedidentity)
  вернул ok=true, тот жеsaved/documentpath+fingerprint и **dbmod0**.
  Filewrite timestamp изменился, размер573490bytes; повторнаяdrawinginfo dbmod0,
  pathнеизменен. Не было uncertain retry/SaveAsдругомуимени.
- Сохранён текущий пользовательский чертёж с прототипом и его ещё прежней связью.
  Исправленный angle-display candidate пока не загружен. Следующий шаг — пользователь
  закрывает AutoCAD, стандартная ужесогласованная installationpreparedcandidate,
  затем fresh native measurement/display inspection и guardedreplacementbinding.
  Геометрия/динамическиеуглы этим сохранением не корректировались.

## Коррекция совпадения с выноской — 2026-10-14

- Пользователь подтвердил работу обеих ручек530/710, но сообщил несовпадение
  углового атрибута с выноской; уточнил **88 в свойствах против108,9 на выноске**.
  Первое объяснение root про округление было преждевременным и исправлено.
  Fresh native reader по20F76 и selected text reader сейчас получают109,
  Angle1=179.94947009635982°, Angle2=108.92204745274593°. Native preview clearly
  shows108,9°. Pickfirst одновременно включает530/710 и source426; native text
  нового20FB0=109, source1D4ED=90. User UI88 remains unconfirmed by native getter;
  palette cache/selection/timing cause is NOT established. User asked to reselect
  only530; ответ «пока не совпадает» не устанавливает источник88.
- Root/agents prepared correction to use the **native annotation itself**:
  BlockAngularDimensionReader scans only current evaluated reference.BTR direct
  angular dimensions, readsMeasurement/DimensionText/FormatMeasurement; transient
  non-DB MText decodes plain text. Exactlyone supporteddimension, decimaldegrees,
  nofield/literaloverride/tolerances/limits required. NumericAngleTextPolicy strips
  onlyterminaldegree unit and preserves separator/precision/zeros without rounding.
  Inspector returns evaluated_angular_dimensions under boundedexistingdeadline.
- New binding mode dimension_display_degrees re-resolves current evaluated BTR
  every refresh; never stores an anonymous dimension handle or guesses raw-angle
  quadrant/formula. Guarded replacement requires exact expected_binding and same
  tag/current text; owned Xrecord editedinplace. Explicitnull secondary legacy bug
  fixed. No parent graphics notification added as a fake Properties-palette fix:
  official API promises screen graphics only, not general palette invalidation.
- Existing dirty edits preserved. **1027/1027 tests pass** (73 additional cases),
  including61 native text-policy cases and12 replacement/schema tests.
  Native2026 build passed (onepreexistingWebClientwarning). Native2027 attempted,
  NETSDK1045: SDK10 missing; no2027successclaimed.
- Ready checked localcandidate `releases/local/2026-10-14-angle-display-acad26`,
  version2.0.1-angle-display.20261014; nativeDLLSHA256
  c7806bd10bedb6c9ab5ef0992a0124e335145a2ec838f068c0e981387cded8fd.
  Self-containedserver verifiedactualtools/list53, expected_binding typedSDKschema,
  CADcalls0. StandardinstallerWhatIf+manifest passed. Candidate NOT installed or
  loaded, live annotation getter/replacement not yet tested.
- Reviewable replacement plan in own CADcache based on fresh native record21000;
  re-read identity/record/angles/text after reload before any dry-run/write.
  LiveDWG hasunsavedprototype+usergriptests (dbmod21). Agent has NOT saved it.
  Next: obtain explicitsave instruction or user saves it, userclosesAutoCAD to
  reload preparedfixedDLL perCAD_WORKFLOW. Preserveprototype+userchanges; do not
  discard them to install. Then inspectnativeformattedannotation, dry-run exact
  bindingreplacement, native+userProperties/grip/Undo comparison. No existing
  block geometry or testedangles changed by this correction stage.

## Live530/710 создан — 2026-10-14

- Пользователь открыл AutoCAD/DWG и разрешённая native работа продолжена.
  Fresh titled document path/fingerprint совпали, Model70, layer0, mm.
  Старый Codex MCP каталог по-прежнему не показывает новые tools: вызовы выполнены
  через fresh stdio client **того же installed canonical typed MCP** (53tools,
  query,modify,drawing,view,target2026). Нет directpipe/C#/LISP/COM обхода.
- Fresh inspector+properties source1D4E3, guarded dry-run successful. Затем
  один committed `dwg_create_block_variant`: reference **20F76**, independent
  definition **20F47**, initial evaluated definition20F77, effective name
  **Отвод ППУ 530-710**, insertion22716.407407301922,4007.0555247419798,0.
  Independent mapped polylines20F56..20F59: axes1200, crossbars710(±355).
  Six defaults20F5A/20F5F..20F63 corrected, MText20F74=500. Six unique reference
  attrs complete, all values match plan:1,20х1,20, sort95, angle90 and new
 530/710 thickness/material designation. Source reference/definition/attributes
  /dynamic props exact readback equal pre-create snapshot; original sample not edited.
- **Native behavior established**, not inferred from initial values: changing
  Угол1 from180→135 rotates **both** arms by−45 (left135, upper45) while Угол2
  remains90, and geometric bend stays90. Угол1 is whole orientation, not a second
  independent arm direction. Changing only Угол2 to45 with first180 keeps left
  horizontal and turns upper arm to45: bend45. Native preview crops prove these
  actions. Therefore binding uses **property_degrees, Угол2**, no secondary
  property; deflection_between_arms would be wrong for this particular graph.
- Guarded binding dry-run then committed creation on own definition:
  Xrecord **21000** under20F48/BIMWRIGHT_ANGLE_ATTRIBUTE_V1 stores exact tag
  `МАССА_ЕД._КГ`, primary_propertyУгол2, modeproperty_degrees, precision0.
  Native fields remain absent; this is the documented plugin calculation.
- Bound tests: Угол2=45→attribute45; whole orientationУгол1=135 with second45
  leaves attribute45; restore first180/second90→attribute90. All native setters
  read back successful, final full attrs equal intended values and binding record
  verified. Re-read source after all tests: exact reference/geometry/angles/attrs
  unchanged. No uncertain write retries, no Save/SaveAs, no purge/audit.
- Grip/cancel/U/REDO/COPY Idle event behavior remains for **user UI testing**.
  Successful typed setters synchronously update text and do not prove callbacks.
  Undo caveat: UpgradeOpen precedes DisableUndoRecording; earlier Undo filing
  may already exist. Cannot claim single-Undo native behavior passed.
- View limitations exposed during visual validation: ZoomToEntity near-vertical
  ViewDirection(4.9e-11,0,221486) makes exact-zero basis check fail, leading to
  nearlyzero x/y axes and blank centered-target view. Recovered broad visible view
  with a window centered at the existing Target. Region/Restore reject each
  current capture as stale; revisions increase after capture/native graphics.
  Actual cause requires separate native investigation, not presumed dimensions.
  Proof uses8192native previews plus local read-only crops; no camera/code bypass.
  Scene is currently broadly zoomed out; user can zoom the530/710cell for testing.
- Nonblocking contract bug found by independent review: explicitnull
  secondary_property allowed for property_degrees, but Calculate tests token
  existence only and would index withnull. Actual calls omit secondary_property;
  DTO also omitsnull on serialization. Source fix/test remains pending, no runtime
  hot edit attempted. Native view and Undo issues likewise recorded, not silently
  solved or declared verified.
- Own intermediate native evidence/request/readback/crops are in
  `D:\CAD-Automation\.cache\block-530-prototype`. Small temporary stdlib client
  typed_mcp_client.py calls only allowed installed typed tool names, validates
  required SDK arguments and terminates only its own stdio child; no CAD source
  script or replacement server. Client read format and metadata key corrections
  caused no repeat CAD mutations. One530prototype is ready for requested user test.

## Установка variant candidate — 2026-10-14

- Пользователь сообщил «Закрыл» после просьбы закрыть AutoCAD для установки
  подготовленной write-версии. Перед установкой acad process count=0.
- Штатный install.ps1 установил `2.0.1-block-variant.20261013` (2026, Client none)
  в прежние bundle/server/current; настройки/профиль MCP не изменены.
  Installer: bundle Verified Acad2026, server executable check OK.
  Обёртка ошибочно трактовала null LASTEXITCODE PowerShell-скрипта как ненулевой
  код; сам installer завершил успешную сводку. Установка **не повторялась**.
  Последующая SHA256 проверка installed DLL и server apphost совпала с candidate.
- Fresh installed stdio initialize/tools-list:53 tools, все три block-tools
  присутствуют; CAD calls=0. AutoCAD по-прежнему закрыт, новая native DLL ещё
  не загружена. Старые MCP workers удерживают два rollback каталога установщика;
  использующиеся копии не удалялись. Для следующего вызова новой схемы использовать
  fresh client того же installed typed server, если текущая MCP-сессия устарела.
- Независимый read-only review не нашёл blocker создания для фактического530
  плана: composed IdMapping, fresh reference и однократный clone шести атрибутов,
  source ForRead, no saves. Grip/action/Undo/COPY требуют native проверки.
- Следующий шаг: пользователь открывает AutoCAD2026 и исходный DWG. Повторить
  fresh drawing identity + source snapshot; guarded dry-run, одна вставка530/710,
  readback независимости/геометрии/атрибутов. Исходные блоки и файл пока не менялись.

## Independent variant prototype — 2026-10-13

- Пользователь попросил продолжить создание одного530/710, остальные ячейки
  пока не заполнять. Ни один CAD write этого этапа пока не выполнен, DWG не сохранён.
- После пользовательского открытия AutoCAD новая installed read-only версия
  работает: fresh stdio client **того же установленного typed MCP** получил
  `dwg_inspect_block` без изменения Codex config. Старый MCP process в чате ещё
  не показывает новую схему; искать настройки пользователю больше не нужно.
  Sandboxed pipe access denied, повторный read-only call с разрешённой escalation
  успешен. Никакого прямого pipe/C#/COM/LISP обхода нет.
- Native snapshot `1D4E3`: effective `Отвод ППУ ду400`, defining BTR `1D4D0`,
  evaluated BTR `1E48D`, current-space owner70, layer0, unit scale/+Z.
  Angular props `Угол1`=π and `Угол2`=π/2, hidden Origin values unsupported.
  Four local polylines form1100mm arms and560mm crossbars. Six invisible locked
  single-line attrs, **exact tag `МАССА_ЕД._КГ`**, no native fields. Defaults differ
  from instance (ВЕС50 vs100, angle blank vs90, designation inconsistent).
  Full snapshot is in `D:\CAD-Automation\.cache\block-530-prototype`.
- Root implemented two guarded typed operations, shared by both host projects:
  BlockVariantInput/Service/CreateBlockVariantHandler and
  BlockAngleInput/BindingService/UpdateBlockAnglesHandler, real DTO facade,
  modify registration/native schemas/dispatch. Existing dirty text/network work
  is preserved; no Git mutations. Parallel helpers failed due workspace credits,
  so this implementation/review was completed by root.
- Whole defining BTR clones into a temporary in-memory DB, gets explicit mapped
  local edits, clones back with independent name, inserts reference and cloned
  attributes. Source definition is never edited. Native clone APIs may use
  internal transactions: compensating cleanup is best effort, not an atomic
  rollback guarantee. Grip/action correctness remains unverified.
- Binding prototype stores named properties/formula on independent definition.
  It is a **plugin calculation**, not a native portable AutoCAD Field. Public
  field Parameter(n) mapping could not be proven; collection-index guessing is
  avoided. Post-command Idle refresh queues no writes in event callbacks,
  waits for active/quiescent document, checks bounds/layers and temporarily
  disables only the derived attribute's Undo recording. Database Undo is never
  disabled. Grip/Undo/Redo/COPY/reopen/portability require explicit native checks.
- Offline: **954/954 tests pass**, native2026 compile passed with the one existing
  WebClient warning. Added23 cases test scope, path/GUID, typed MCP schema,
  raw-radian angular names/formulas/wrap/rounding and read-only exclusion.
  Actual packaged stdio tools/list: standard53, read-only19, CAD calls0.
  Native2027 build attempted again: NETSDK1045 (.NET10 SDK absent); installed2027
  references also absent. No2027 success claimed.
- Ready local package only for2026:
  `releases/local/2026-10-13-block-variant-acad26`, version
  `2.0.1-block-variant.20261013`; plugin SHA256
  `aad2d8256251f5143a75300c0868c14578f74eeb1022d1fa0c7274854e5e7651`.
  Self-contained server and native DLL built, canonical docs copied, all241files
  manifested, standard installer WhatIf/manifest check passed. Not installed or
  loaded yet. Uses existing global constant server path/profile, no extra MCP.
- Prepared guarded dry-run plan `variant-530-plan.json` in own CAD cache:
  local arms1200, crossbars710 (±355), label500, insertion sourceX−2500,
  size1,20х1,20, sort95, initial angle90, six corrected defaults and instance
  values. Source thickness/material based on supplied PDF versions: pipe530х9,
  bend530х10, steel17Г1С chosen from allowed17Г1С/17Г1С-У, PE710х8.9.
- Next: user closes AutoCAD for the **new write plugin** installation (the prior
  switch added only reading). Install already-authorized local test package via
  its standard install.ps1, user opens sameDWG. Read fresh identity/snapshot,
  validate dry-run, create exactly one530/710, inspect source/new geometry,
  test named angles on own prototype before choosing binding formula, restore90,
  bind and test grip/Undo/Redo with user. Never automatically save/retry an
  uncertain mutation. Document contract: docs/BLOCK_VARIANTS.md.

## Уточнение и согласованная установка — 2026-10-13

- Пользователь разрешил установить локальный test candidate 2026 и переключить
  подключение. Затем сообщил, что закрыл все экземпляры AutoCAD; проверка
  процессов перед установкой вернула 0. Закрытие/открытие GUI выполняет пользователь.
- Исправлено назначение атрибута: несмотря на имя `МАССА_ЕД_КГ`, он хранит **угол
  отвода в градусах**, а не массу. Значение 90 означает 90°. Пользователь прямо
  сообщил, что в образце связи нет, и потребовал создать её: текст должен
  обновляться после изменения угла динамической ручкой. Технический тег сохранить.
  `ВЕС` остаётся внутренним номером сортировки, для 530/710 = 95.
- Read-only inspector дополнен native fields атрибутов и их определений:
  raw code/evaluator/cached status/children с теми же пределами metadata/time/bytes.
  Никаких Field.Evaluate или setter в чтении нет; pinned Autodesk API getters
  сверены по reference XML и финальная Acad26 сборка прошла без ошибок.
- Native BlockPlaceholder/AcObjProp field — предпочтительный кандидат привязки,
  но публичные docs не подтверждают Field Parameter(n) для конкретного `Угол2`.
  Нельзя подменять field parameter index порядком коллекции. Требуется проверка
  на собственном тестовом блоке; source property определяется поведением ручек,
  а не совпадением текущих значений 90/180.
- FIELDEVAL не гарантирует отдельный grip-change trigger. Если native field
  потребуется оценивать после завершения команды, это должна быть ограниченная
  операция для связанных полей с native статусом успеха. Сценарий text-on-idle
  без продуманного Undo может создать лишний Undo шаг/цикл; он пока не реализован.
- Штатный install.ps1 выполнен после WhatIf и SHA-256 проверки manifest:
  candidate `2.0.1-block-inspection.20261013` установлен для 2026. Bundle/source
  plugin hashes совпадают (`32b6e11941fff3ff0e536eb48dd2187cb05427d0d92f2baeaca3bb2d3646d63f`),
  server --help check OK. Installed paths — прежние ApplicationPlugins bundle и
  server/current. Старый MCP process ещё держит собственный rollback каталог
  `current.dwgmcp-rollback-d52e9f8c807c4c1ebbd27a3d3a3d9cdc`; установщик сохранил
  его целым до перезапуска клиента. Не удалять используемую копию вручную.
  AutoCAD пока закрыт, новый plugin не загружен/native read не выполнен;
  текущий чат ещё не показывает dwg_inspect_block и требует обновления MCP.
  Конфигурация
  уже указывает на постоянный `server/current/dwg-mcp.exe` и нужный профиль;
  отдельного второго MCP создавать не требуется. Связь угла и сам 530/710 остаются
  незавершёнными до native чтения и реализации записи.

## Реализация чтения блоков — 2026-10-13

- Пользователь явно разрешил начать доработку общего моста после просмотра
  контракта запроса ниже. Объём этого этапа — read-only `dwg_inspect_block`,
  без создания/изменения block definitions, загрузки DLL и сохранения DWG.
- Source: добавлены BlockInspectionInput, BlockInspectionService,
  InspectBlockHandler и BlockInspectionTools. Инструмент зарегистрирован
  в query/read-only профиле, native schema и dispatcher. Общая реализация
  находится в src/shared; подробный контракт — docs/BLOCK_INSPECTION.md.
  Входные типы/лимиты/identity проверяются до native access; все DB opens
  ForRead. Opaque evaluation graph остаётся graph_contents_complete=false.
- Сохранены существующие незакоммиченные CAD/text изменения. Минимальная
  интеграция затронула Program, server csproj, SchemaValidator, dispatcher,
  ToolsListSnapshotTests и текущие docs; CadEntityProperties и существующие
  обработчики текста этим этапом не переписывались. Добавлены 74 offline cases
  к прежним 857. Git branch/staging/commit/push/merge не менялись.
- Сборочный SDK .NET 8.0.425 загружен с builds.dotnet.microsoft.com по официальной
  release metadata с проверкой SHA-512 и распакован локально в
  `D:\CAD-Automation\.cache\dotnet\sdk-8.0.425`; NuGet cache также локальный.
  Системный SDK/PATH и installed runtime моста не переключались.
- Offline: 931/931 .NET tests passed, 0 failed/skipped. Первые два падения
  выявили String-null JValue в facade; optional identity fields теперь
  исключаются при null. Настоящий MCP SDK проверил schema/annotations в тестах;
  stdio initialize/tools-list SDK-сервера без CAD calls подтвердил standard=51,
  standard read-only=19, query read-only=12, unique dwg_inspect_block. Python MCP
  client SDK на этом ПК не установлен: актуализированный check-text-mcp.py
  здесь целиком не запускался, stdio wire check использовал stdlib клиент.
- Native compile: Acad26 net8.0-windows с pinned compile-only AutoCAD.NET 25.1.0
  собран успешно; одно прежнее SYSLIB0014 WebClient warning, ошибок нет.
  При review добавлены normals/thickness для curves и форматированные MText
  default contents для AttributeDefinition; финальная сборка их включает.
  Acad27 build попытка дала NETSDK1045: нет SDK .NET 10, также отсутствуют
  installed AutoCAD 2027 managed refs. Проверка 2027 остаётся открытой.
- Отдельный **локальный test candidate только для 2026**, не командный ZIP и не
  installed runtime: `releases/local/2026-10-13-block-inspection-acad26/`.
  Server опубликован self-contained win-x64; plugin и его зависимости скопированы
  из финальной сборки, canonical AGENTS скопирован без ручного редактирования.
  Candidate основан на текущем dirty tree и включает уже существующие pending
  CAD/text изменения, а не только этот инструмент. Его self-contained executable
  прошёл initialize/tools-list в read-only профиле: 19 tools, CAD calls=0.
- Loaded/live: новый plugin/server не установлен и не загружен. AutoCAD/DWG
  этим этапом не менялись, geometrical/dynamic block тесты не выполнялись.
  530/710 пока не создан; назначение углового атрибута исправлено в разделе выше.
- Следующий шаг: отдельное поручение на тестовое переключение installed 2026
  plugin/MCP; пользователь выполняет смену сессии AutoCAD. Затем typed чтение
  образца, сверка двух углов/геометрии и дальнейшее развитие independent variant.
  Нельзя объявлять offline/native compile доказательством живого чтения или
  создания рабочего блока; 2027 и фактическое содержимое action graph ещё требуют
  отдельной проверки. Выпуск командного ZIP остаётся после принятия кода по
  CAD_WORKFLOW.md.

## Новый запрос — динамические блоки отводов, 2026-10-13

- Запрос пользователя: разобрать один из четырёх динамических блоков в активном
  `Отводы Для монтажнной схемы.dwg`, затем создать только один самостоятельный
  вариант 530/710 для проверки. Остальные ячейки пока не заполнять; DWG не сохранять.
  Это запись ожидаемого результата, не начало реализации инструментов.
- Живое чтение через установленный `dwg-2026` подтверждено. Исходник-образец:
  BlockReference `1D4E3`, effective name по пользовательской панели
  `Отвод ППУ ду400`, текущее anonymous name `*U13`; fingerprint чертежа
  `1e3a3e15-4e07-c947-a816-9105ed7ec14d`. Снимок панели показывает только
  `Угол1 = 180°`, `Угол2 = 90°`; размерных параметров на панели нет.
  Связи параметров с действиями и геометрией ещё не прочитаны.
- `get_selected_texts` читает все шесть AttributeReference выбранного образца.
  Теги по пользовательской панели: `ПОЗ.`, `ОБОЗНАЧЕНИЕ`, `НАИМЕНОВАНИЕ`,
  `ВЕС`, `МАССА_ЕД_КГ`, `ПРИМЕЧАНИЕ`. Пользователь разъяснил: `ВЕС` — внутренняя
  нумерация для сортировки, брать красное число ячейки; для 530/710 это **95**.
  Позднее пользователь уточнил: `МАССА_ЕД_КГ` — угол отвода в градусах,
  который требуется связать с динамикой. Полные теги предстоит подтвердить
  нативным чтением, без восстановления имён по сокращениям интерфейса.
- Источники размеров/материалов для конкретного чертежа — предоставленные
  пользователем PDF из Downloads. Каталог СМИТ, печатная стр. 26: для 530/710
  при 90° плечо 1200 мм, выпуск 210 мм, оболочка 710×8,9 мм. Таблица В.8 МОЭК
  из версии 2021, стр. 3–4: труба 530×9 мм, колено 530×10 мм, марки стали
  17Г1С/17Г1С-У. Каталожная масса 247,8 кг к угловому атрибуту не относится.
  Эти проектные значения не включать как константы в общие инструменты.
- Нужный контракт чтения: typed `dwg_inspect_block` в query-профиле, один
  явно заданный handle и необязательная пара expected document/fingerprint;
  bounded max_entities/response budget. Возвращать identity, reference/effective
  definition handles/name, transform, все атрибуты tag/value/flags, динамические
  свойства name/type/value/read_only/units/allowed_values и прямую геометрию
  defining BTR в локальных единицах блока. Для связей parameter/action выдавать
  только фактически доступные native metadata и явный признак полноты. Чтение
  не меняет геометрию, pickfirst selection, view и DWG; нераспознанные объекты,
  ошибки чтения или лимит не выдавать за полный разбор.
- Контракт записи требует дополнительного native исследования: самостоятельное
  новое именованное определение без изменения исходного/его вставок, сохранение
  evaluation graph и action selection relationships; точные document/fingerprint
  и свежие source expectations, запрет overwrite, dry_run, transaction, явные
  geometry edits в локальных единицах и атрибуты по полным тегам. Возвращать
  created definition/reference handles, applied values и readback completeness.
  После неопределённого timeout не повторять запись; сохранение не включать.
- Autodesk документирует DynamicBlockTableRecord и перечисление/изменение
  DynamicBlockReferencePropertyCollection; создание/редактирование parameter/action
  graph для смены размеров не подтверждено публичным managed API. Двухэтапное
  WblockCloneObjects целого именованного BTR через временную БД в памяти — кандидат
  для прототипа клонирования, а не доказательство корректной динамики этого отвода.
  Не использовать uniform scale как эквивалент смены диаметра и длины плеча.
- Состояние: source checkout найден в `D:\CAD-Automation\Projects\cad-bridge`.
  Предыдущий вывод чата об отсутствии исходников исправлен. Установленный профиль
  query/modify/drawing/view по фактическому списку содержит 42 инструмента, без
  динамического inspection/definition variant. Подготовленные исходники других
  задач уже изменяют Program, dispatcher, schema validator, CadEntityProperties,
  тестовый проект и WORK_STATUS; чужие правки сохранены. Нового кода, сборок,
  загрузки DLL, переключения MCP и записи объектов этим запросом пока нет.
- Следующий шаг: согласовать начало доработки общего моста и пересекающиеся файлы
  по docs/CAD_WORKFLOW.md; затем реализовать/проверить bounded inspection для обоих
  лет, отдельно согласовать загрузку пользователем и читать фактическую структуру.
  Создание варианта 530/710 и проверка обоих углов остаются незавершёнными.

## Текущее состояние — интеграция текстовых операций, 2026-10-09

- По прямому запросу пользователя текстовые доработки перенесены в актуальный
  checkout `D:\CAD-Automation\Projects\cad-bridge`, ветка `artem`, исходный HEAD
  `95d117f`. До начала рабочее дерево и index были чистыми. Старый патч к
  `a54a174` не применялся целиком: сохранены уже принятые CAD-01…06, проверки
  документа, metadata proxy/сети/атрибутов и правила совместной работы.
- Добавлены `dwg_create_text` в обычный drawing-профиль, `dwg_set_text_style`,
  `dwg_match_properties`, `dwg_list_text_styles`; свойства текста показывают
  style/font metadata. Контракты — в единственном `docs/TEXT_TOOLS.md`.
  В упаковку добавлено копирование этого контракта, канонические AGENTS
  продолжают генерироваться из корня без второй редактируемой runtime-копии.
- Проверено в объединённом дереве: 857/857 .NET offline-тестов, 0 failed/skipped;
  server Release и Acad26 собраны. 2026 использует AutoCAD.NET 25.1.0;
  одно прежнее предупреждение SYSLIB0014 у WebClient, ошибок нет.
- Настоящий MCP Python SDK проверил initialize/tools-list и схемы: стандартный
  профиль 50, с annotation 53, read-only 18; query 11, modify 28, drawing 5,
  annotation 4, drawing+annotation 8. CreateText регистрируется один раз.
  Прежние три style-схемы также прошли. Ни одного CAD-вызова не выполнялось.
- Сборка Acad27 этого объединённого дерева пока не подтверждена: на этой машине
  не найдены .NET 10 SDK и managed references AutoCAD 2027. Ранее успешная
  сборка 2027 ниже относится к предыдущему кандидату, не к текстовой интеграции.
- История Git, ветка, staging и commit/push не менялись. Пользователь фиксирует
  изменения в своей ветке. Никакие DLL не установлены/загружены, ZIP и runtime
  не заменены. Source/offline не являются доказательством loaded/live.
- Следующий шаг: проверить Acad27 на машине с SDK10/AutoCAD2027, затем после
  принятия кода собрать новую версию командного ZIP; загрузка DLL и native
  проверки текста/anchors/rollback выполняются пользователем с отдельным
  явным разрешением. Live-проверки сетей/proxy/SaveAs также остаются отдельными.

## Состояние до интеграции текстовых операций — 2026-10-09

- Единственные исходники и технические инструкции: `team/Projects/cad-bridge`
  в репозитории `git@github.com:tovarishnakhimovets-droid/Projects.git`.
  Пользователь выполнил merge PR #2 и pull; исходный импорт
  `a54a174d2459bc063085450e04c15d2c970bd0ab` принят merge `0e81f07`.
  Локальная `develop` сейчас на `95d117f` (merge PR #4); PR #3 с коммитом
  `3b90a85` тоже входит в историю. Перед доработкой запуска LISP рабочая папка
  была чистой. Новые исходники LISP и актуализация этих документов не закоммичены
  на `develop`; ветку и состав коммита выбирает пользователь.
  Git-команды, публикацию и merge агент не выполнял.
- Снимок upstream v2.0.1 (`4d0f75646631186612af926944d7e33affc4757d`)
  содержит все локальные исправления. Пять патчей уже применены; повторно
  применять их не надо. Нет вложенного .git, Autodesk DLL и рабочих чертежей.
- Для выпущенного кода до уборки прошли 708/708 .NET offline-тестов, SDK
  initialize/tools-list подтвердил 42 инструмента и style-схемы без CAD-вызовов.
  Сверены 286 source hashes и 40 файлов ZIP; оба плагина скомпилированы.
  Compile-only AutoCAD.NET 25.1.0 используется для 2026 / .NET 8;
  версия 25.1.1 требует .NET 10 и не заменяет эти ссылки.
- Текущий кандидат: 852/852 .NET offline-теста, 0 failed/skipped; реальные
  SDK-схемы новых инструментов входят в эти проверки. MCP stdio initialize /
  tools-list подтвердил 48 инструментов без CAD-вызовов; в read-only профиле
  18, запуск LISP отсутствует, статус доступен. Обе итоговые native
  сборки прошли: Acad26 net8.0-windows и Acad27 net10.0-windows; сервер net8.0
  собран без предупреждений. Плагины сохраняют старые предупреждения Unicode
  (WebClient/ServicePointManager) и Roslyn 4.8/4.10 у 2027, без ошибок.
  Результаты сборки лежат в штатных ignored bin/obj. Новые DLL не загружены,
  live-операции и сохранение не выполнялись, ZIP и runtime не обновлялись.
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
  tools пока отсутствуют. В проверенном runtime фильтр Platform=AutoCAD;
  кандидат исходников расширяет только 2026/2027 до AutoCAD|Civil3D.
  Автозагрузка нового фильтра live не проверена.

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

Очередь CAD-01…06 реализована в исходниках этим чатом по разрешению пользователя.
Исходные сценарии НВ сохранены как прототипы; они не скопированы в мост.
Этот код уже входит в текущую `develop`; новая доработка LISP ещё не закоммичена.
Git остаётся у пользователя. Offline-проверки и обе сборки прошли, загрузка/live ещё впереди.
Контракты и пределы описаны в docs/CAD_WORKFLOW.md. История НВ ниже сохранена.

| ID | Готово в исходниках | Что остаётся |
| --- | --- | --- |
| CAD-01 | Диагностика путей/readonly/DBMOD, preflight текущего и другого открытого файла, SaveAs с bBakAndRename=true и проверкой пути после записи. | eFilerError и переименование вкладки проверить live отдельно по запросу; исправление ещё не подтверждено в AutoCAD. |
| CAD-02 | Титулованный полный DWG path вместо имени DWT, fingerprint; обязательные guards новых write-tools и необязательная пара для save. | Проверить на сохранённом/несохранённом DWG после загрузки новой DLL. Остальные старые write-tools не получили обязательные guards. |
| CAD-03 | dwg_inventory_proxies: bounded full-DB scan, metadata/owners/permission, pagination и признаки полноты. | Проверить на реальной Civil/Map БД; классификация только подсказка, страницы не единый снимок. |
| CAD-04 | dwg_erase_proxy_objects: explicit expectations, dry_run, одна транзакция и прямой IsErased readback. | Live только на явно разрешённых собственных объектах; связанное содержимое/ссылки исключены, многие Civil service proxies будут отклонены. |
| CAD-05 | dwg_node_network: tested pure split/snap, сохранение bends/style/owner, исходный handle первого фрагмента и exact endpoints. | Проверить live на своей тестовой сети; merge, дуги, ширины и связанные metadata не реализованы. |
| CAD-06 | dwg_move_network_endpoint: полный snapshot, endpoint/block/attributes в одной транзакции; сохранение других vertices/text. | Проверить native displacement/attribute readback live; dynamic/annotative/xref и multiline attributes исключены. |

Существующее чтение объектов дополнено owner_handle, DBPoint.position,
Polyline normal/elevation/ширинами и полным списком координат атрибутов блока.
Новые сетевые запросы можно подготовить через get_entity_properties с
includeGeometry=true без вспомогательного C#-сценария. Отдельное read-only ревью
подтвердило цепочку чтения. Общие catch-сообщения не гарантируют rollback при
ошибке Commit; неопределённую мутацию автоматически не повторять.

2026-10-09 пользователь сообщил, что Codex ГИПа тоже готовит доработки к коммиту.
Состав/ветка/локальный diff ГИПа из этого чата недоступны и не проверены.
Перед объединением сверить задачи и пересечение файлов; поддержка 2026 и 2027
обязательна для обоих наборов. Каждый работает в своей копии Projects. Первое
слияние затем включается во вторую ветку, после чего проверяется общий результат.
Сообщения в чат ГИПа и действия с его Git не выполнялись.

Другие ограничения и проверки:

- Установленный runtime отказывает в AutoLISP. В новом кандидате предусмотрен
  самостоятельный допуск файла/функции; не обходить отказ старого runtime через C#.
- SOLID hatch поддерживает одну Circle/closed planar lightweight Polyline,
  без островов и произвольных узоров. Наклонные границы и видимый draw order
  отдельно live не подтверждены.
- Civil 3D 2027: ручная загрузка и чтение метаданных проверены; автоматическая
  загрузка кандидата AutoCAD|Civil3D пока не проверена. Контракты специальных
  Civil-инструментов не заданы.

## Запуск файлов LISP через MCP — 2026-10-09

Запрос пользователя — общая возможность запуска `.lsp`, включая интерактивные
команды; конкретный `polyline_lengths_to_leaders.lsp` в мост не вшит.

- `dwg_run_lisp` теперь доступен в write-профиле `modify` (также meta/code),
  `dwg_get_lisp_status` — в query. Прежняя трёхпараметрическая C# обёртка остаётся
  отказом для совместимости; фактическая MCP-схема требует файл, функцию, SHA256
  и точные DWG path/fingerprint. Read-only режим запуск не регистрирует и
  независимо отказывает при прямом вызове server-метода.
- Пользовательская команда `MCPTRUSTLISP` связывает допуск с локальным путём,
  байтами файла и функцией на одну сессию. Scanner/параметры запроса доверие не
  выдают. `MCPDISABLELISP`/`MCPSTOP` очищают его. Запуск выполняет захваченный
  исходник, а не повторно читает файл из AutoLISP; UTF-8/UTF-16 BOM, до 64 КиБ.
- Нативная очередь получает защищённый BEGIN и RESULT callbacks. До queue
  проверяется свободная командная строка; до eval повторяются identity/readonly/
  Unicode/trust guards. Статус не ждёт main thread. Результат callback сам по
  себе не завершает задачу: нужны native LispEnded/Cancelled и нулевая вложенность.
  Пока queued/running, другие CAD-вызовы отказывают. Отмена/ошибка после старта
  может оставить изменения; нет автоматического повторения и обещания rollback.
- Offline: 852 passed, 0 failed/skipped. Проверены реальные SDK-схемы, одобрение
  точной версии, экранирование/JSON-аргументы, ограничения размера/кодировок,
  очередь/отмена/отзыв/задержка, native event ordering в managed модели и отказ
  занятого Command prompt. Один промежуточный testhost завис после 811 тестов
  и был остановлен; причина не установлена. Окончательный прогон с ограничением
  зависания 30 секунд прошёл целиком за 5.3 секунды, без диагностического dump.
- Реальный stdio initialize/tools-list: write-профиль 48, read-only 18.
  Никаких вызовов к AutoCAD при этой проверке не было. Финальные сборки 2026
  (.NET 8/AutoCAD.NET 25.1.0) и 2027 (.NET 10/installed refs) прошли; прежние
  предупреждения Unicode/Roslyn сохранились. Результаты в штатных ignored bin/obj.
- Старый runtime/DLL, ZIP, Git и DWG не менялись. Новый код ещё не загружен.
  Остаются live-проверки native BEGIN/event order, Unicode, интерактивного выбора,
  отмены и доставки большого экранированного исходника. Начать с отдельного
  разрешённого пользователем теста на своей тестовой DWG после новой загрузки;
  успешная сборка не разрешает агенту запустить LISP в рабочем чертеже.

Порядок допуска/запуска и ограничения находятся в docs/CAD_WORKFLOW.md.

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

1. Пользователь создаёт рабочую ветку с текущими незакоммиченными изменениями LISP,
   просматривает diff, затем делает commit/push/PR в develop. База уже содержит
   merge 95d117f; история ранее опубликованного коммита 3b90a85 сохранена.
   Сверить изменения ГИПа до merge, затем проверить объединённый код для обоих годов.
2. Пользователь включает загрузку кандидата в новой сессии AutoCAD и отдельные
   live-проверки на своих тестовых объектах с очисткой. Проверку save запросить
   отдельно на тестовом DWG; не считать eFilerError устранённым по одной сборке.
3. После принятия и проверок собрать новый пакет с новым номером (например,
   team.3), затем установить на обоих компьютерах. Выпущенный team.2 не заменён.
4. Подтвердить read-only связь dwg-2026 у ГИПа, если ещё не выполнено. Установка
   описана в docs/TEAM_SETUP.md. Мост к каждому проекту копировать не требуется.

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
