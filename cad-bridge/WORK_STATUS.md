# CAD bridge — состояние работы

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
  Пользователь создал и опубликовал `docs/cad-bridge-workflow`, коммит `3b90a85`
  от `develop` / `7ba4b30`; открытие/merge документационного PR не подтверждены.
  По просьбе пользователя исходники очереди доработаны без ожидания новой ветки.
  Сейчас код и актуализация этих документов не закоммичены в той же локальной
  ветке; перед кодовым коммитом пользователь перенесёт их в рабочую feature-ветку.
  Git-команды, публикацию и merge агент не выполнял.
- Снимок upstream v2.0.1 (`4d0f75646631186612af926944d7e33affc4757d`)
  содержит все локальные исправления. Пять патчей уже применены; повторно
  применять их не надо. Нет вложенного .git, Autodesk DLL и рабочих чертежей.
- Для выпущенного кода до уборки прошли 708/708 .NET offline-тестов, SDK
  initialize/tools-list подтвердил 42 инструмента и style-схемы без CAD-вызовов.
  Сверены 286 source hashes и 40 файлов ZIP; оба плагина скомпилированы.
  Compile-only AutoCAD.NET 25.1.0 используется для 2026 / .NET 8;
  версия 25.1.1 требует .NET 10 и не заменяет эти ссылки.
- Текущий кандидат: 785/785 .NET offline-тестов, 0 failed/skipped; реальные
  SDK-схемы новых инструментов входят в эти проверки. MCP stdio initialize /
  tools-list подтвердил 46 инструментов без CAD-вызовов. Обе итоговые native
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
Код пока находится незакоммиченным на docs/cad-bridge-workflow; Git остаётся у
пользователя. Offline-проверки и обе сборки прошли, загрузка/live ещё впереди.
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

- AutoLISP блокируется upstream; не обходить отказ через C#.
- SOLID hatch поддерживает одну Circle/closed planar lightweight Polyline,
  без островов и произвольных узоров. Наклонные границы и видимый draw order
  отдельно live не подтверждены.
- Civil 3D 2027: ручная загрузка и чтение метаданных проверены; автоматическая
  загрузка кандидата AutoCAD|Civil3D пока не проверена. Контракты специальных
  Civil-инструментов не заданы.

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

1. Пользователь создаёт feature-ветку с текущими незакоммиченными изменениями,
   просматривает diff, затем делает commit/push/PR в develop. Документационный
   коммит 3b90a85 уже опубликован; его включение учитывается при выборе базы PR.
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
