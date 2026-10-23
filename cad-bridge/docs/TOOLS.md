# Контракты CAD-инструментов

Технический справочник общих операций моста AutoCAD 2026/2027. Установка и
совместная разработка описаны в [TEAM_SETUP.md](TEAM_SETUP.md), запуск AutoLISP
и сетевые/proxy-операции — в [CAD_WORKFLOW.md](CAD_WORKFLOW.md). Правила работы —
в каноническом AGENTS.md; фактические source/offline/installed/loaded/live
результаты — только в WORK_STATUS.md исходников.

- [Выбор открытого чертежа](#documents)
- [Настройки приложения AutoCAD](#settings)
- [Настройки перекрестия](#crosshair-settings)
- [Вес линий и SOLID-заливка](#styles)
- [Текущее выделение объектов](#selection)
- [Текст, стили и выбранные свойства](#text)
- [Чтение и переписывание текста](#text-rewrite)
- [Выноски](#leaders)
- [Чтение динамического блока](#block-inspection)
- [Независимый вариант блока и угловые атрибуты](#block-variants)
- [Атомарное создание объектов](#create-entities)
- [Нативные таблицы TABLE](#tables)
- [Правка существующих вершин полилинии](#polyline-vertices)
- [Вид и отображение весов](#view)
- [Проверка контрактов](#checks)

Перед изменениями объектов DWG прочитать `dwg_get_drawing_info` и свежие
свойства нужных объектов. Слои, стили и handles выбирать по данным текущего чертежа, не по
примеру. Проверять top-level `ok` и результаты элементов. Таймаут записи
не доказывает откат: сначала перечитать состояние, не повторять автоматически.
Отдельные MCP-вызовы не образуют общую транзакцию или Undo. Сохранение DWG
требует запроса пользователя; эти инструменты не сохраняют его автоматически.
Наличие исходников и успешные offline-тесты не обновляют загруженный runtime
и не подтверждают нативное поведение AutoCAD.

<a id="documents"></a>
## Выбор открытого чертежа

Оба инструмента входят в `query`, доступны в read-only и не открывают,
не активируют, не закрывают и не сохраняют DWG.

| Вызов | Результат |
| --- | --- |
| `dwg_list_documents()` | `documents` и `active_document_id`. Для каждого открытого DWG: `document_id`, `document_name`, `document_path`, `fingerprint`, `is_active`, `is_named`, `is_read_only`, `inactive_read_commands`. |
| `dwg_use_document(document_id)` | Проверяет ID и выбирает DWG для последующих CAD-вызовов этого процесса stdio-сервера; возвращает его описание. Неудачный выбор сохраняет прежнюю цель. |
| `dwg_use_document()` или `dwg_use_document(document_id=null)` | Сбрасывает выбор: последующие вызовы следуют активной вкладке. Сброс работает и без открытых DWG; тогда описание равно null. |

Выбор не меняет вкладку, выделение или вид пользователя и не передаётся другим
процессам сервера. Пока выбранный DWG открыт, переключение пользователем вкладки
не меняет эту цель. ID брать из свежего списка: это непрозрачный идентификатор
экземпляра открытого документа, а не путь или fingerprint. После закрытия/повторного
открытия DWG или перезапуска слушателя нужен новый ID. Неизвестный/устаревший ID
даёт отказ без подстановки активного чертежа. `dwg_list_documents` доступен и при
устаревшем выборе. Handles и результаты чтения относить только к своему DWG;
после смены цели перечитать `dwg_get_drawing_info` и проверить путь/fingerprint.

Для **неактивного** выбранного DWG разрешены только восемь DB-readers:

- `dwg_get_drawing_info`, `dwg_get_entity_properties`;
- `dwg_query_entities`, `dwg_count_entities`;
- `dwg_list_layers`, `dwg_list_text_styles`;
- `dwg_select_by_layer`, `dwg_select_by_type` — возвращают handles поиска,
  фактическое выделение AutoCAD не меняют.

Их обычные фильтры и лимиты сохраняются; например, поиск entities остаётся в
model space. Поле `inactive_read_commands` содержит внутренние имена этих команд
без префикса `dwg_`. У неактивного DWG `dbmod=null`: значение активной вкладки
не выдаётся за его состояние.

Запись, Editor-выделение (`dwg_get_selection`, `dwg_get_selected_texts`), переменные,
нативные настройки, вид, запуск LISP, batch и C# требуют, чтобы выбранный DWG был активной
вкладкой; иначе запрос отклоняется до выполнения операции. Для чтения текущего
выделения пользователя сначала сбросить неактивную цель. Список документов и
статус уже поставленного LISP-job не зависят от выбранного DWG. Локальные каталоги
вроде `dwg_list_settings` не обращаются к AutoCAD. Выбор цели не
открывает фоновое редактирование и не даёт разрешения менять вкладки пользователя.

<a id="selection"></a>
## Текущее выделение объектов

`dwg_get_selection(max_entities=100, include_geometry=false)` доступен в `query`,
включая read-only режим. Читает только текущее предварительное выделение AutoCAD
через SelectImplied; не вызывает запрос выбора, не меняет выделение и PICKFIRST.
Если пользователь говорит «выбранные объекты», начинать с этого инструмента.

`max_entities` — целое 1..500. По умолчанию возвращаются основные свойства,
native `dxf_name`/`class_name`, handles и типы без подробной геометрии или раскрытия
блоков. `include_geometry=true` добавляет обычное подробное описание существующего
reader, в том числе доступные атрибуты блоков; это не полный обход вложенных объектов.
Для дальнейших операций использовать свежие handles и читать нужные свойства.

Ответ содержит identity текущего документа (`document_name`, `document_path`,
`fingerprint`), `status` (`ok`, `empty`, `disabled`), `native_prompt_status`,
`pickfirst`, `selected_count`, `returned_count`, `handles`, `entities`, `complete`,
`truncated`, `errors`. Элемент entities: `handle`, `ok`, `entity`, `error`; ошибки
остаются отдельными строками, а не исчезают из подсчёта. Handles соответствуют
возвращённым строкам; null означает, что даже handle прочитать не удалось.
`type_counts.scope="returned"`: counts учитывает только распознанные типы
возвращённых строк, включая строки с ошибкой последующего чтения свойств.
Это не статистика пропущенных из-за лимита объектов.

`selected_count` — размер нативного SelectionSet до ограничения. `complete=true`
означает, что прочитаны все выбранные объекты без ошибок. При `PICKFIRST=0`
возвращается `disabled`, `complete=false`; размер недоступного выбора не известен.
`empty` означает отсутствие доступного pickfirst-выбора. Ошибка самого чтения —
отказ, не пустой список. При пустом, отключённом или недоступном выборе сообщить
об этом; поиск по всему чертежу и старые handles не подменяют пользовательский выбор.
`dwg_get_selected_texts` отдельно извлекает и группирует текст, а не описывает
все выбранные объекты. Чтение работает и с несохранённым DWG.

Основание — [официальный пример pickfirst/SelectImplied](https://help.autodesk.com/cloudhelp/2027/ENU/OARX-DevGuide-Managed/files/GUID-D4987D00-1164-4217-A82E-B8B49FFB7A29.htm).

<a id="styles"></a>
## Вес линий и SOLID-заливка

| Вызов | Основные параметры | Результат |
| --- | --- | --- |
| `dwg_set_lineweight` | `handles`: массив строк; `lineweight_mm`: число; `mode`: `explicit` / `by_layer` / `by_block` | Изменённые handles, count, вес и режим |
| `dwg_set_layer_lineweight` | `layers`: массив имён существующих слоёв; `lineweight_mm`: число | Изменённые слои, count, вес |
| `dwg_create_hatch` | `boundary_handle`: одна окружность или замкнутая плоская lightweight Polyline; опциональные layer, color_index либо color_rgb, associative, draw_order | Handle новой Hatch и её параметры |

Примеры параметров обычных MCP-вызовов:

```json
{"handles":["24C"],"lineweight_mm":0.30}
{"handles":["24C"],"mode":"by_layer"}
{"layers":["НВ","НК1","НК2"],"lineweight_mm":0.30}
{"boundary_handle":"24D","color_rgb":[255,255,255]}
```

Handles в примерах условные. Агент получает актуальные handles из создания
или чтения объектов нужного активного чертежа.

Вес всегда задаётся в миллиметрах, независимо от единиц координат чертежа.
Допустимы точные значения AutoCAD: 0, 0.05, 0.09, 0.13, 0.15, 0.18, 0.20, 0.25,
0.30, 0.35, 0.40, 0.50, 0.53, 0.60, 0.70, 0.80, 0.90, 1.00, 1.06, 1.20, 1.40,
1.58, 2.00, 2.11. Для by_layer/by_block поле lineweight_mm надо опустить.
LWDISPLAY не меняется: отображение толщины включается пользователем отдельно.

Заливка использует только паттерн SOLID, без островов. Граница должна находиться
в текущем пространстве. По умолчанию используется её слой, цвет ByLayer,
associative=true, draw_order=above_entities. Белая заливка задаётся настоящим RGB
[255,255,255]. above_entities поднимает заливку над другими объектами и контур
над заливкой; below_entities опускает заливку вниз. Слои должны существовать.

Все цели проверяются до записи. Заблокированные, зависимые от Xref, отсутствующие
или недоступные цели отклоняют весь запрос. Вес меняется одной транзакцией;
создание Hatch также выполняется одной транзакцией. Общий Undo для нескольких
MCP-вызовов не добавлялся. Чертёж автоматически не сохраняется.

Подход опирается на[Autodesk LineWeight enum](https://help.autodesk.com/cloudhelp/2026/ENU/OARX-ManagedRefGuide/files/OARX-ManagedRefGuide-Autodesk_AutoCAD_DatabaseServices_LineWeight.html)
и [пример создания Hatch](https://help.autodesk.com/cloudhelp/2022/ENU/OARX-DevGuide-Managed/files/GUID-89CAED67-B3A0-4045-9D8B-9034D67E8D91.htm).

<a id="text"></a>
## Текст, стили и выбранные свойства

| Инструмент | Параметры и поведение |
|---|---|
| `dwg_create_text` | Required `text:string`, `position:string` с JSON point `{x,y,z?}`. Optional `height:number` (default 2.5), `rotation:number` (degrees, default 0), `layer:string`, `color_index:integer` (1..256), `text_style:string`, `width_factor:number`. Создаёт один DBText в текущем space; point — WCS, размеры — drawing units. Height/width должны быть finite и >0. Цвет по умолчанию — явный ByLayer 256. |
| `dwg_create_mtext` | Required `text:string`, `location:string` с JSON WCS point. Optional `width:number` (default 0, без переноса), `height:number` (default 2.5), `rotation:number` (degrees, default 0), `layer:string`, `color_index:integer` (1..256), `text_style:string`. Создаёт MText с нативным форматированием; width конечная и >=0, height конечная и >0. |
| `dwg_set_text_style` | Required `handles:string[]`, `text_style:string`. Назначает существующий неаннотативный стиль обычным DBText/MText, не изменяя запись стиля, текущий TEXTSTYLE и текст. Возвращает changed `handles`, `count`, `examined`, `text_style`, `atomic:true`. |
| `dwg_match_properties` | Required `source_handle:string`, `target_handles:string[]`, `properties:string[]`. Копирует только указанные свойства существующего unlocked образца. Source нельзя включать в targets. Возвращает source handle, target `handles`, `count`, `properties`, `atomic:true`. |
| `dwg_list_text_styles` | Optional `limit:integer` (default 100, range 1..1000). Read-only список: `styles`, `count` возвращённых записей, `limit`, `truncated`. Записи содержат `name`, `handle`, `is_current`, font metadata и style defaults. |

CreateText/CreateMText используют plugin handlers; их facade доступен через
`drawing` и `annotation`. При включении обоих toolsets имя регистрируется один раз.
Старые C# `AnnotationTools.CreateText/CreateMText` остаются forwarding helpers.
Read-only профиль скрывает все четыре text/style write-инструмента и сохраняет list_text_styles.

`text_style` при создании должен существовать, быть local, horizontal,
неаннотативным и не shape record. При отсутствии параметра используется текущий
default style. Новый стиль и font не создаются и не загружаются автоматически.
Height остаётся заданной высотой entity; style defaults не подменяют явно
заданные размеры. Missing style, invalid color/width/point и locked target layer
отклоняют создание; добавление объекта и возможного нового слоя откатывается
в одной транзакции.

Для match_properties допустимы ровно:
`layer`, `color`, `linetype`, `linetype_scale`, `lineweight`, `text_style`,
`height`, `width_factor`, `oblique`. Без `all` или implicit default list.
Text properties требуют обычные неаннотативные DBText/MText; width_factor и
oblique требуют DBText у source и всех targets. Common properties допускают
другие supported Entity. Color/lineweight/linetype копируются как stored values:
ByLayer/ByBlock сохраняются, true RGB не превращается в ACI или видимый цвет слоя.
При color-only copy слой target сохраняется; ByLayer использует именно этот слой.

Оба инструмента изменения проверяют все targets и возможность записи до setters
и используют одну transaction. Отказ до Commit не фиксирует изменения. Ошибка
во время Commit или потеря соединения не доказывают rollback: перед повтором
нужно выяснить фактическое состояние. Нативные проверки остаются отдельным шагом. Запросы
содержат 1..1000 уникальных ненулевых hex handles; aliases `1`/`01`, duplicates,
unknown properties, missing handles и xref/locked/unwritable targets
отклоняются. AttributeReference/AttributeDefinition исключены из операций
с текстовыми свойствами; копирование только общих Entity properties допускает
их при успешном preflight. Aligned/Fit DBText и annotative text требуют отдельной операции и
здесь не поддержаны. Отдельные последовательные tool calls общей атомарности
не имеют. Проверять top-level `ok`; при неопределённом timeout сначала перечитать
состояние, не повторять mutation автоматически.

При смене стиля сохраняется reference anchor: DBText с default justification —
Position, прочий supported DBText — AlignmentPoint, MText — Location.
AutoCAD может пересчитать derived insertion point и extents при закрытии объекта;
пиксельный размер текста после смены font не гарантируется. Текст, rotation,
justification и невыбранные параметры entity не копируются. Inline formatting
MText сохраняется и может переопределять font выбранного style.

### Чтение оформления

`dwg_get_entity_properties` с `includeGeometry:true` и `dwg_query_entities` с
`include_geometry:true` возвращают для DBText дополнительно `width_factor`,
`oblique`, `normal`, `alignment_point`, `justification`, `is_default_alignment`.
Для MText добавлены normal/justification. Оба text types получают:

- `style_name`, `style_handle`;
- `font_file`, `big_font_file`, `typeface`, `bold`, `italic`, `character_set`,
  `pitch_and_family`, `font_metadata_error`;
- `style_width_factor`, `style_oblique`, `style_text_size`, `style_is_vertical`,
  `style_metadata_error`.

Style defaults отличаются от собственных width/oblique/height entity.
`rotation` и `oblique` в query, а также `oblique` в style metadata — radians;
`rotation` при создании — degrees. Font metadata отражает запись DWG и не
подтверждает установленный font или отсутствие font substitution. Недоступные
metadata отмечаются null/diagnostic. Имеющиеся color_index, color_rgb,
lineweight_mode и lineweight_mm позволяют различать RGB, ACI и наследование.

### Примеры параметров

Сначала list_text_styles и query подтверждают, что `GOST TYPE A` существует,
а его font metadata содержит требуемый ISOCPEUR. Имя стиля и имя font — разные
значения; не выбирать стиль по догадке. Числа и handles ниже — пример, их нужно
заменить проверенными данными проекта.

Создание DBText через `dwg_create_text`:

```json
{
  "text": "Т1",
  "position": "{\"x\":100,\"y\":200,\"z\":0}",
  "height": 3.5,
  "rotation": 0,
  "text_style": "GOST TYPE A",
  "width_factor": 0.8,
  "color_index": 256
}
```

Назначение существующего стиля через `dwg_set_text_style`:

```json
{"handles":["1A2B"],"text_style":"GOST TYPE A"}
```

Копирование только стиля образца через `dwg_match_properties`:

```json
{"source_handle":"1A2B","target_handles":["1A2C","1A2D"],"properties":["text_style"]}
```

После изменения перечитать те же handles с geometry, сверить выбранный style,
reference anchor и остальные нужные entity values. Примеры не являются
live-проверкой подготовленного патча.

Основание политики стиля и placement: официальные Autodesk
[`DBText.TextStyleId`](https://help.autodesk.com/cloudhelp/2026/ENU/OARX-ManagedRefGuide/files/OARX-ManagedRefGuide-Autodesk_AutoCAD_DatabaseServices_DBText_TextStyleId.html)
и [`DBText.AdjustAlignment`](https://help.autodesk.com/cloudhelp/2024/ENU/OARX-ManagedRefGuide/files/OARX-ManagedRefGuide-Autodesk_AutoCAD_DatabaseServices_DBText_AdjustAlignment_Database.html).
Для MText: [свойства MText](https://help.autodesk.com/cloudhelp/2026/ENU/OARX-ManagedRefGuide/files/OARX-ManagedRefGuide-__MEMBERTYPE_Properties_Autodesk_AutoCAD_DatabaseServices_MText.html).

<a id="text-rewrite"></a>
## Чтение и переписывание текста

`dwg_get_selected_texts(groupingStrength="normal", includeEntities=false)`
читает пользовательское pickfirst-выделение. `weak`/`normal`/`strong` управляют
пространственной группировкой. Пустое выделение — отказ. При `includeEntities=true`
возвращаются handles, типы/тексты, слой и owner, координаты/высота WCS,
`local_position`/`local_height`, `block_handle`/`block_scale`, bounds с признаком
`bounds_available`. Вложенные определения читаются на один уровень. Группы
не сохраняются для будущей записи; `rewrite_mode` больше не возвращается.
Снимок не доказывает, что произвольные фрагменты безопасно объединять.

`dwg_update_texts(items)` принимает строку JSON-массива
`[{"handle":"1A2B","new_text":"Текст"}]` и меняет только текст DBText/MText,
AttributeReference и текстового MLeader. Геометрия, высота, стиль и слой
не назначаются автоматически. Ответ — результаты элементов `handle/ok/error`;
отказ элемента не отменяет удачные элементы запроса.

`dwg_collapse_and_rewrite(clusters)` принимает **одну строку JSON-объекта**:

- Обязательные поля корня: `expected_document` — полный путь текущего сохранённого
  DWG, `expected_fingerprint` из свежего чтения и массив `clusters`.
- Элемент: `anchor_handle`, `new_text`; необязательные `delete_handles`,
  `convert_to_mtext`, `mtext_width`, `text_height`.
- Ширина/высота задаются в **локальных единицах владельца**, не WCS блока.
  Это конечные числа: ширина >=0, высота >0. Неуказанные свойства наследуются
  от исходного текста; скрытого множителя высоты и фирменного стиля нет.
- Объединение поддерживает обычные DBText/MText одного владельца. Атрибуты и
  MLeader допускают замену текста, но не удаление/преобразование при merge.
  Fields, annotative, xref и заблокированные объекты отклоняются.
- До записи проверяется весь план элемента. Транзакции отдельные для каждого
  элемента: учитывать top-level `ok` и все результаты; общий Undo не обещается.
  Ответ содержит `anchor_handle`, `ok`, `new_handle`, `deleted_count`, `action`,
  фактические размеры и ошибку. `deleted_count` включает стёртый старый anchor
  при создании MText. Анонимные/динамические определения отклоняются; обычное
  определение изменяется для всех ссылок. После замены старый anchor может быть стёрт.

`dwg_translate_and_rewrite`/`dwg_apply_unicode_style` удалены; старые параметры
автоматического оформления отклоняются. Перевод и фирменное оформление выполняются
по [сценарию](CAD_WORKFLOW.md#перевод-и-оформление-текста), оформление — одним LISP.

<a id="leaders"></a>
## Выноски

| Возможность | Текущая реализация и границы |
| --- | --- |
| Чтение | `dwg_get_entity_properties` с `includeGeometry:true` для MLeader: текст, высота, количество leaders/lines, first/last vertex линии с индексом 0. Это не полная геометрия всех стрелок; при отсутствии этого индекса точки могут быть null. |
| Текст | `dwg_update_texts` меняет MText-содержимое MLeader. Для старого LEADER нужно отдельно прочитать связанную аннотацию; его handle не является handle текста. Блочное содержимое MLeader этим setter не редактируется. |
| Создание | `dwg_create_leader(points,text?,layer?,color_index?,height?,arrow_size?,text_style?)` создаёт обычную MLeader в текущем пространстве. Доступен в штатном `drawing` и в `annotation`; при совместном включении регистрируется один раз. |
| Длины участков | `PLLENLEADERS` из общего LISP сопоставляет стрелки существующих MLeader/Leader с сегментами выбранных полилиний и меняет число после `L=`. Несколько стрелок должны указывать на один сегмент; неоднозначности отклоняются. |

`points` — строка JSON-массива ровно двух различных WCS-точек `{x,y,z?}` на одной
отметке: сначала конец стрелки, затем точка у текста. Координаты конечные, z по
умолчанию 0. Линия прямая сплошная, стрелка закрытая заполненная; полка, dogleg,
рамка, удлинение к тексту и аннотационный масштаб отключены, Scale=1. Это явные
свойства создаваемого объекта; текущий MLeaderStyle и системные переменные не меняются.
Без текста создаётся стрелка без содержимого. Текст ограничен 16384 символами,
без fields и NUL. `height` и `arrow_size` — конечные положительные размеры в
единицах чертежа, по умолчанию 2.5. `text_style` — существующий local horizontal
неаннотативный стиль; без параметра проверяется и используется текущий default
стиль текста. Цвет ACI 1..256,
по умолчанию ByLayer; вес линии ByLayer. Возможное создание слоя и самой выноски
выполняется одной транзакцией. Ответ: handle, обычные свойства `entity` и фактические
параметры `leader`. Визуальная проверка стрелки и Undo выполняется после обновления.

Контракт использует [свойства MLeader](https://help.autodesk.com/cloudhelp/2026/ENU/OARX-ManagedRefGuide/files/OARX-ManagedRefGuide-__MEMBERTYPE_Properties_Autodesk_AutoCAD_DatabaseServices_MLeader.html)
и [per-line ArrowSymbolId](https://help.autodesk.com/cloudhelp/2026/ENU/OARX-ManagedRefGuide/files/OARX-ManagedRefGuide-Autodesk_AutoCAD_DatabaseServices_MLeader_SetArrowSymbolId_int_ObjectId.html).
Для closed filled применяется ObjectId.Null по [проверке автора на Autodesk Community](https://forums.autodesk.com/t5/net-forum/arrowhead-api-for-mleader/m-p/12747274);
это не заменяет проверку в наших 2026/2027.

Общих MCP-setters всех стрелок/полок, стиля MLeader и блочного содержимого пока
нет. Для работы с несколькими стрелками не подменять полную геометрию первой
линией и не вычислять связь по одному положению текста.

<a id="block-inspection"></a>
## Чтение динамического блока

`dwg_inspect_block` — read-only инструмент query-профиля. Он читает одну явно
указанную вставку и её effective definition. Не меняет чертёж, текущий вид,
выделение, динамические значения и не сохраняет DWG. Реализация общая для
плагинов 2026/2027; состояние сборки и живой проверки — в WORK_STATUS.md.

### Параметры

| Поле | Контракт |
| --- | --- |
| `handle` | Обязательная строка из 1–16 hex-символов; ненулевой положительный signed 64-bit handle. Пробелы, знаки и префикс `0x` не принимаются. |
| `expected_document`, `expected_fingerprint` | Необязательная пара: полный путь именованного DWG и ненулевой GUID. Оба должны быть заданы вместе; DWT и относительные пути не подходят. Фактическое совпадение проверяется в активном документе перед чтением. |
| `include_definition` | По умолчанию `true`: прямая геометрия определения и его owned metadata. При `false` остаются свойства/атрибуты вставки и её owned metadata. |
| `max_entities` | Целое 1–5000, по умолчанию 500. Ограничивает прямые entities определения и дополнительные пределы атрибутов/свойств. |
| `max_metadata_objects` | Целое 0–1000, по умолчанию 200. Ноль запрещает обход содержимого metadata; пропуск отражается в полноте ответа. |

Неправильные типы JSON, неизвестные поля, переполнение и неверные пределы
отклоняются одинаковым host-free preflight в сервере и обработчике плагина.
Инструмент не ищет другой блок, если handle неверен, и не подменяет активный DWG.
Xref, dependent и layout определения явно отклоняются.

### Ответ и единицы

При `include_definition=true` также возвращается `evaluated_angular_dimensions`:
ограниченное чтение прямых LineAngularDimension2/Point3AngularDimension из текущего
`BlockReference.BlockTableRecord`. Это evaluated BTR конкретной вставки, а не
исходное effective definition. Ответ содержит native Measurement (радианы и
градусы), DimensionText, штатный FormatMeasurement, plain text, формат/геометрию,
полноту и причину отказа от числовой трактовки. `unique_numeric_text` доступен
только для одного поддержанного decimal-degree размера. Текст сохраняет штатные
запятую/точку/нули и точность; убирается только конечный знак градусов.
Размеры с fields, literal overrides, допусками/пределами и неоднозначные значения
явно не поддержаны. Обход: max_entities, 5 секунд, 256 КиБ и общий бюджет reader.
Чтение ForRead; plain text декодируется transient MText, который не добавляется
в DWG. Нет RecomputeDimensionBlock, GenerateLayout или Field.Evaluate.

- Identity документа, handle вставки, effective name/definition handle и handle
  evaluated definition, position/normal/rotation/scale вставки.
- Атрибуты: полные tag/text, handles, position/alignment/rotation/height,
  constant/invisible/preset/verifiable/lock_position и MText contents при наличии.
- Динамические свойства: name/description/type/value/read_only/units,
  visible/show, allowed values и полнота их списка. Angular values сохраняются
  в **радианах**; рядом выдаются `value_radians` и `value_degrees`. Пустой список
  allowed values не доказывает отсутствие min/max или иных ограничений.
- Native fields атрибутов и AttributeDefinition: raw code/evaluator ID,
  сохранённый статус оценки, опции/state и связи с child fields. Поля имеют
  отдельный `native_fields_complete`, но разделяют общий metadata/time/byte budget.
  Код читается getters; оценка поля и запись не выполняются. Ссылки из кода поля
  не открываются. `fields_present/text_field_handle/fields_complete` показывают
  состояние конкретного атрибута. Field code не является инструкцией агенту.
- Прямые entities defining BTR читаются в **локальных координатах определения**:
  insertion transform не применяется. Внешняя rotation вставки и entity angles
  также в радианах. Поддержаны Line, Circle, Arc, DBPoint, Ellipse, DBText/MText,
  AttributeDefinition и lightweight Polyline. Для кривых выдаётся normal;
  форматированные MText defaults атрибутов сохраняются. Другие классы имеют
  envelope/basic properties и признак неполной геометрии. Nested BlockReference
  описывается как прямая entity; его definition не обходится рекурсивно.
- Owned dictionaries/Xrecords: handle/owner/class/path и ограниченные scalar
  values. ObjectId/Handle pointers только описываются, произвольные ссылки не
  обходятся. Неподдерживаемые native classes и binary payloads выдаются как
  envelopes/length с явным `contents_complete=false`.

`attributes_complete`, `dynamic_properties_complete`, `definition_entities_complete`,
`metadata_complete`, `metadata_contents_complete`, `strings_truncated`, errors и
`stop_reason` относятся к своим частям чтения. `inspection_complete` означает
завершение заявленного ограниченного managed scope без ошибок/сокращений.
`complete` **не становится true для динамического блока**: undocumented
parameter/action graph не декодируется, `graph_contents_complete=false`.
Наличие метаданных и двух углов не доказывает сохранность/полноту действий.

### Ограничения

Ответ ограничен 1 МиБ; строки — 8192 символами, атрибуты — min(max_entities, 512),
свойства — min(max_entities, 256), allowed values — 256 на свойство, polyline
vertices — 512, Xrecord values — 256, dictionary depth — 4. При превышении
лимита данные помечаются неполными; итоговый byte guard может убрать массивы.
Deadline 10 секунд проверяется между native API calls и не прерывает один
зависший вызов Autodesk. Запрос большого списка allowed values или текстового
содержимого внутри native call не ограничивает внутренние allocations API.

Managed API предоставляет чтение reference properties, но эта операция не
создаёт independent definition, не меняет размеры и не редактирует action graph.
Для такого развития нужна отдельная реализация и native проверка, а не
предположение, что uniform scale эквивалентен смене типоразмера.

Официальные контракты Autodesk 2026:
[DynamicBlockTableRecord](https://help.autodesk.com/cloudhelp/2026/ENU/OARX-ManagedRefGuide/files/OARX-ManagedRefGuide-Autodesk_AutoCAD_DatabaseServices_BlockReference_DynamicBlockTableRecord.html),
[DynamicBlockReferenceProperty](https://help.autodesk.com/cloudhelp/2026/ENU/OARX-ManagedRefGuide/files/OARX-ManagedRefGuide-__MEMBERTYPE_Properties_Autodesk_AutoCAD_DatabaseServices_DynamicBlockReferenceProperty.html).

<a id="block-variants"></a>
## Независимый вариант блока и угловые атрибуты

`dwg_create_block_variant` and `dwg_update_block_angles` are typed operations in
the `modify` profile. They do not accept C#, LISP, commands or arbitrary property
setters, and never save a drawing. Source and loaded runtime are distinct.

Creation requires the active titled DWG's full path/fingerprint, a complete
source reference/attribute/writable-angle snapshot from `dwg_inspect_block`,
and explicit snapshots of each edited direct definition entity. The reader's
definition geometry is local, while insertion/reference attributes use WCS.
The reference layer is obtained with `dwg_get_entity_properties`.

Supported variant scope: one independent named dynamic definition with up to 500
direct entities, 1..32 open planar +Z straight zero-width two-vertex polylines,
0..32 single-line attribute defaults,0..8 field-free MTexts and 32 attached
single-line attributes. Source reference must have +Z normal and unit scale.
Edited handles must belong to its effective defining BTR. Name collisions,
external/layout/anonymous source definitions, source attribute fields and
changed snapshots are rejected. `dry_run=true` preflights without setters.
There is a cooperative 15-second deadline, not interruption of blocked native calls.

The entire defining BTR is cloned by `WblockCloneObjects` to an in-memory
database, renamed/edited there, then cloned back; `Wblock(blockId)` is not used.
Native IdMapping relationships preserve owned graph links. Inserted reference
must still be dynamic, have the same named writable angular properties, retain
requested raw values and all attributes. Correct grip/action behavior is not
proven by these checks and must be verified on the new prototype.

Native clone APIs may commit internal state independently of the outer
transaction. Failure handling attempts compensating cleanup limited to the
new reserved name and mapped newly cloned IDs. Cleanup failures are reported.
Do not claim guaranteed atomic rollback or retry uncertain timed-out mutations.
Do not erase unrelated definitions or purge a drawing to clean up a test.

Angle updates require exact expected effective name and complete named writable
angular values; values are **radians**, with native allowed-value and readback
checks. Optional binding additionally requires exact current attribute text.
Binding persists on the independent definition and applies to all its references.
Replacing it requires `expected_binding` equal to the entire current persisted
specification, with the same attribute tag. The owned Xrecord is edited in place.

The binding is a **plugin calculation**, not a native portable AutoCAD Field.
There was no field in the source, and public managed API does not provide a
verified mapping from a named property to the undocumented Field Parameter(n).
Do not infer that index from the collection position. The plugin must be loaded
for subsequent grip/command/Undo/Redo updates.

`dimension_display_degrees` reads exactly one supported direct angular Dimension
from **reference.BlockTableRecord**, the CURRENT evaluated definition. The
selector is semantic and re-resolved on every refresh; no anonymous dimension
handle is cached. `Dimension.Measurement` and native `FormatMeasurement` supply
the value, rounding, units and separators used by the annotation. A transient,
disposed non-DB MText removes formatting. The numeric legacy attribute strips
only the terminal degree symbol; decimal comma/dot/zeros are preserved exactly.
No independent Math.Round or angular-parameter formula is applied.

Supported native numeric display requires decimal degrees, no fields, literal
text override, tolerances/limits or multiple numeric values. Missing/multiple
angular dimensions, limits, truncated text and unsupported formatting refuse the
operation instead of selecting a guessed angle. Native mode accepts no custom
property names or precision. Inspection exposes `evaluated_angular_dimensions`
with actual handles/geometry/measurement/rawoverride/nativeformattedtext and
completeness under the existing bounds; no RecomputeDimensionBlock, layout
generation or Field.Evaluate is called.

Legacy `property_degrees` formats one named property; `deflection_between_arms` computes
180° minus the smaller angle between two named angular arm directions. The
choice requires a live behavior check; equal initial 90° values prove nothing.
Text uses invariant numeric formatting and 0..6 decimal places without a degree
symbol only in these legacy modes. They do NOT guarantee equality with the
dimension's selected angular quadrant or display precision. The technical
attribute tag is unchanged.

Command-ended/cancelled/failed callbacks only queue a bounded refresh episode;
they do not lock, open or write objects inside the native command callback.
Idle performs all passes only for the active, quiescent, writable document.
Database ObjectModified
callbacks only queue watched bound references/current evaluated angular dimensions;
they never open, evaluate or write objects. The retained database wrapper is
unsubscribed when its document closes. Only explicitly owned attribute/parent
writes are suppressed; a dimension event during lock release remains pending.

Each pass first scans with a read-only OpenCloseTransaction, without Commit or a
document write lock. A current attribute therefore causes no write phase. Only
pending changes acquire a document lock; IDs and managed values cross the
transaction boundary. All affected objects, binding, old text, angles, layers
and calculated output are reopened and revalidated before any UpgradeOpen.
The apply phase also uses OpenCloseTransaction and commits actual changes.
Completion occurs after releasing transactions/the lock and requires two identical
read-only observations on separate callbacks. Raw angles, current evaluated
definition/dimension identities and native measurement/display are included in a
bounded SHA256 signature. A write or late dimension event resets stability.
Six passes and 2000ms of active processing bound an episode; idle intervals are
not charged. One pass has a cooperative 500 ms deadline, 10000 definitions,
1000 references and 256 KiB snapshot bound. These do not interrupt blocked native
calls. Native dirty notifications cannot continually renew an exhausted budget;
a new external command starts a fresh episode. Exhaustion is reported.

After changing an attribute, only its actual parent BlockReference is write-opened
and RecordGraphicsModified(true) is called. This supplies AutoCAD's documented
graphics update signal, not a guaranteed Properties-palette invalidation.
Selection and dynamic angles/geometry are not assigned for this notification.
Only affected attribute/parent Undo recording is temporarily disabled; database
Undo is never disabled, and UpgradeOpen can already begin native Undo filing.
Live Undo/Redo, immediate Properties refresh, COPY and reopen must be checked
separately; compilation/offline temporal tests do not establish these behaviors.
The read-only/event policy follows Autodesk's explanation of
[normal transactions and write opens destroying Redo in callbacks](https://blog.autodesk.io/nothing-to-redo-message-appears-straight-after-undoing-in-autocad-for-net-and-objectarx/).
This does not establish that every document lock alone destroys Redo.

<a id="create-entities"></a>
## Атомарное создание объектов: dwg_create_entities

Одна операция создания новых объектов в текущем пространстве. Не вызывает
legacy batch/команды/сценарии и не изменяет существующие объекты. Профиль `drawing`.

| Поле | Контракт |
| --- | --- |
| `expected_document`, `expected_fingerprint` | Обязательные полный путь титулованного DWG и GUID из свежего `dwg_get_drawing_info`. DWT/несохранённый документ не подходят. |
| `entities` | 1–2000 typed объектов. У каждого обязательны уникальный локальный `id`, `type`, существующий `layer`. ID: 1–64 ASCII букв/цифр/`_`/`-`, регистр различается. Это не DWG handle. |
| `dry_run` | По умолчанию false. True проверяет весь план и native зависимости без constructors, setters и записи. |

Общие appearance поля: один из `color_index` (1–256, default ByLayer 256) или
`color_rgb` ([r,g,b], целые 0–255); `lineweight_mm` — точное поддержанное AutoCAD
значение, иначе ByLayer. Новые слои и стили не создаются. Слои должны быть local,
unlocked; текстовый стиль — local/horizontal/non-annotative/non-shape.

Координаты — typed point `{x,y,z?}` в WCS, единицы чертежа, Z по умолчанию 0.
Числа конечны и ограничены модулем 1e12. Углы создания — градусы.

| `type` | Геометрия и дополнительные поля |
| --- | --- |
| `line` | `start`, `end`; различные точки. |
| `circle` | `center`, положительный `radius`, normal +Z. |
| `point` | `position`. |
| `polyline` | `points` из 2–512 точек одной точной высоты; optional `closed:false`. Прямые сегменты без ширины, повторную замыкающую вершину не передавать. |
| `rectangle` | `position` нижнего левого угла, положительные `width`, `height`, optional `rotation:0`. Создаёт замкнутую lightweight Polyline. |
| `arc` | `center`, `radius`, `start_angle`, `end_angle`; `0 <= start < end <= 360`, sweep <360. Полный круг передавать как circle. |
| `ellipse` | `center`, `major_radius`, `minor_radius`, optional `rotation:0`. Полный эллипс, отношение minor/major от 1e-6 до 1. |
| `text` | `position`, `text`, `text_style`; optional `height:2.5`, `rotation:0`, `width_factor:1`. Одна строка. |
| `mtext` | `position`, `text`, `text_style`; optional `height:2.5`, `rotation:0`, положительная `width:100`. |
| `hatch` | `boundary_id` ранее объявленной circle/rectangle/простой замкнутой polyline; optional `associative:true`. Только SOLID; плоскость/высота берутся из границы, её контур располагается поверх заливки. |

TEXT/MTEXT: до 16384 символов, без fields и null characters. Неизвестные поля и
поля другого типа отклоняются. Самопересекающиеся/нулевые hatch boundaries не
поддержаны. Геометрия чисто вычисляется без округления координат относительно
мирового начала; проверка площади использует локальное начало для survey offsets.

Лимиты: 2 МиБ UTF-8 запроса, 20000 суммарных polyline/rectangle vertices,
ограниченное число сравнений контуров и cooperative deadline 15 секунд.
Deadline проверяется между native calls и не прерывает зависший вызов Autodesk.

Сначала проверяются identity, весь план, существующие слои/стили и Undo state.
Затем подготавливаются transient entities; запись всех объектов выполняется
одной transaction. Ответ содержит `entities:[{id,type,handle,layer}]`, `count`,
`atomic:true`, `committed:true`, `saved:false`. Dry-run возвращает `plan` без handles.
Ошибка до Commit откатывает transaction; после попытки Commit ошибка содержит
прямое требование проверить фактическое состояние перед повтором. Потеря связи
не доказывает rollback, автоматически повторять запрос нельзя.

Undo должен быть включён; Control One и уже открытая группа Undo отклоняются.
Один handler использует один существующий document lock и одну transaction.
Штатный command Undo bracket при lock/unlock описан Autodesk. Нет выключения
database Undo, C#/LISP или вызовов несуществующего managed `StartUndoRecord`.
`single_undo_verified:false` честно оставляет **один U/Redo** отдельной native
проверкой; offline тесты не доказывают поведение пользовательской истории Undo.

Пример (слой `SCHEME` должен уже существовать; identity заменить свежим):

```json
{
  "expected_document": "C:\\Drawings\\scheme.dwg",
  "expected_fingerprint": "11111111-1111-1111-1111-111111111111",
  "dry_run": true,
  "entities": [
    {"id":"node","type":"circle","layer":"SCHEME","center":{"x":10,"y":20},"radius":2},
    {"id":"fill","type":"hatch","layer":"SCHEME","boundary_id":"node","color_index":1}
  ]
}
```

<a id="tables"></a>
## Нативные таблицы: dwg_create_table / dwg_read_table

Одна редактируемая AutoCAD TABLE в текущем пространстве, без Excel/OLE-связи
и разложения на линии/тексты. `dwg_create_table` доступен в `drawing` и
`annotation`; `dwg_read_table` — в `query`, `drawing`, `annotation`, включая
read-only режим. При совместном включении профилей имена не дублируются.
В read-only создание не регистрируется. Оба вызова требуют **активного** DWG:
неактивная цель сессии отклоняется без переключения вкладки.

Наличие инструмента проверять через фактический `tools/list`: исходники и
offline-тесты не обновляют установленный runtime и не подтверждают native/live
поведение. Состояние установки и проверок — в
[WORK_STATUS.md](../WORK_STATUS.md).

| Поля создания | Контракт |
| --- | --- |
| `expected_document`, `expected_fingerprint` | Required: полный путь сохранённого титулованного DWG и непустой GUID из свежего `dwg_get_drawing_info`. Несохранённый DWG и DWT не подходят. |
| `insertion_point` | Required typed point `{x,y,z?}` в WCS; Z по умолчанию 0. Координаты конечны, модуль не больше 1e12. |
| `cells` | Required прямоугольная `string[][]`. 1–256 строк, 1–128 столбцов, не более 10000 ячеек. Число строк/столбцов выводится из матрицы, отдельных `rows`/`columns` нет. |
| `column_widths`, `row_heights` | Required массивы чисел точной длины: по одному на столбец/строку. Значения конечны, строго положительны и не больше 1e6. |
| `layer`, `text_style` | Required непустые точные имена до 255 символов. Слой уже существует, local/unlocked; текстовый стиль уже существует, local/horizontal/non-annotative/non-shape. Новые слои и стили не создаются. |
| `text_height` | Default 2.5; конечное число от 1 до 1000. |
| `horizontal_margin`, `vertical_margin` | Default 0.4 и 0.2; от 0 до 1000. Два отступа должны оставлять положительную внутреннюю ширину каждого столбца и высоту каждой строки. |
| `rotation` | Default 0; конечный поворот всей TABLE в градусах относительно WCS +X. Normal +Z, строки идут сверху вниз. |
| `merges` | Optional массив прямоугольных объединений `{top,left,bottom,right}`; границы включительные, индексы целые с нуля. |
| `cell_formats` | Optional массив `{row,column,text_height?,rotation?,alignment?}`; индексы с нуля. |
| `dry_run` | Default false. True проверяет входы, identity, Undo state и native зависимости без создания TABLE и записи. Native layout и вмещение текста при этом не проверяются. |

Все геометрические размеры, высота текста и отступы — **единицы чертежа**;
автоматического перевода миллиметров или масштабирования под лист нет.
Таблица использует текущий TableStyle и cell style native класса Data:
существующее имя `Data`, а при его отсутствии — ровно один найденный стиль
этого класса. Неизвестное/неоднозначное имя не подставляется. Явно задаются
запрошенные размеры, текстовый стиль, высота, выравнивание и отступы.
Цвет/linetype/вес самого объекта — ByLayer, фон ячеек отсутствует. Отдельных
параметров управления весами/видами линий сетки нет: они наследуются от TableStyle;
существующие стили и системные переменные инструмент не переписывает.

Ячейки содержат только plain strings: число, пустая ячейка и отсутствие изделия
заранее представляются соответственно, например, `"0"`, `""`, `"-"`.
Автоматического преобразования чисел/null в текст нет. Native тип ячеек и
содержимого явно задаётся String/Unitless с пустым DataFormat, чтобы строки
`"001"` и `"7,78"` не превратились в числа или форматированные значения.
До 4096 UTF-16 символов
на ячейку и 200000 суммарно; весь запрос до 1 МиБ UTF-8. Запрещены fields `%<`,
формулы с начальным `=` (в том числе после пробельных символов), обратная косая
черта, фигурные скобки, legacy controls `%%` и управляющие символы, кроме
переводов строк.
CRLF/CR нормализуются в LF; native paragraphs при чтении возвращаются как LF.
Ссылки на Excel, native formulas, блоки в ячейках и произвольные native setters
не входят в контракт. Неизвестные поля отклоняются.

Объединения должны находиться внутри матрицы, содержать минимум две ячейки
и не пересекаться. Текст сохраняется только в верхней левой **опорной** ячейке;
в остальных исходный текст обязан быть строго `""`, даже пробел не допускается.
Иначе запрос отклоняется до native записи, чтобы MergeCells не потерял данные.
Форматы разрешены для обычной или опорной ячейки; неякорные ячейки объединения
и повторяющиеся пары `row,column` отклоняются.

По умолчанию текст центрирован по вертикали и горизонтали, поворот содержимого 0.
В `cell_formats` можно переопределить `text_height` (1–1000), `rotation`
(только 0/90/180/270 **градусов**) и `alignment`
(`middle_center`, `middle_left`, `middle_right`).
Это поворот текста внутри ячейки, отдельный от общего `rotation` таблицы.
Неуказанные/null свойства формата означают отсутствие переопределения.

Создание выполняется на основном потоке под одним document lock и в одной
transaction. До записи проверяются документ, текущий local layout, слой,
текстовый стиль, TableStyle/Data и Undo state. Undo должен быть включён;
Control One и уже открытая группа Undo отклоняются. После native layout до
Commit сверяются матрица текстов, объединения, размеры строк/столбцов,
положение/направление, эффективное оформление и bounds. Если AutoCAD увеличил
размеры из-за native минимума либо результат отличается, transaction не
фиксируется. Автоматического уменьшения текста для вмещения нет.

Успех содержит `handle`, `rows`, `columns`, `cells`, `column_widths`,
`row_heights`, `merges`, эффективные `cell_formats`, `bounds`, identity,
`atomic:true`, `committed:true`, `saved:false`. `single_undo_verified:false`
и `text_fit_verified:false` оставляют один U/Redo, печать и вмещение текста
предметом отдельной native/визуальной проверки. Dry-run возвращает размеры,
`committed:false`, `saved:false`, `native_layout_checked:false`, без handle.
Cooperative deadline — 15 секунд; проверяется между native вызовами и не прерывает
зависший Autodesk вызов. Ответ до 4 МиБ UTF-8 проверяется до Commit.
После таймаута/ошибки попытки Commit сначала перечитать фактическое состояние;
повторять создание автоматически нельзя. Сохранения DWG нет.

`dwg_read_table(expected_document, expected_fingerprint, handle)` принимает
один свежий положительный hex handle той же TABLE и свежую identity активного
DWG. Читает только TABLE непосредственно в local model/paper layout;
определения блоков и xref/dependent таблицы отклоняются. Первая версия читателя
поддерживает только String/Unitless с пустым DataFormat: numeric значения,
data links, block/field/formula и multiple-content ячейки отклоняются.
Возвращает полную матрицу plain text, размеры, объединения, эффективные форматы
всех ячеек и bounds, `complete:true`, `read_only:true`.
Лимиты чтения: 10000 ячеек, 200000 символов, 4 МиБ ответа, 15 секунд.
Чтение использует Read-lock и OpenCloseTransaction без Commit, setters,
GenerateLayout, RecomputeTableBlock или вычисления fields. Оно не сохраняет DWG.
Ответ описывает native состояние; полный `cell_formats` readback не является
готовым запросом создания с форматами только опорных ячеек.

<a id="polyline-vertices"></a>
## Правка существующих вершин: dwg_edit_polyline_vertices

Профиль `modify`. Меняет XY существующих индексов current-space lightweight
Polyline через `SetPointAt`, сохраняя объект. Не добавляет/не удаляет/не переставляет
вершины и не редактирует определения блоков.

Required: `expected_document`, `expected_fingerprint`, `changes` (1–100 объектов).
Optional: `min_segment_length` (default 1e-9, >0), `dry_run:false`.
Каждый change содержит:

- `handle`, `expected_layer`, `expected_owner_handle`;
- `expected_normal:[0,0,1]`, `expected_elevation`, `expected_thickness`,
  `expected_constant_width`, `expected_is_closed`;
- полный `expected_vertices` из свежего `dwg_get_entity_properties(includeGeometry:true)`:
  `{index,x,y,z,bulge,start_width,end_width}` для каждой вершины по порядку;
- `edits:[{index,position:[x,y,z]}]`: существующие уникальные индексы, та же высота.

Snapshots должны совпадать точно, без округления; handles уникальны, включая
hex aliases. До 20000 исходных вершин суммарно, конечные числа модулем <=1e12,
native budget 15 секунд. Слишком короткие сегменты, включая замыкающий, отклоняются.
Bulges и переменная ширина поддерживаются и сохраняются; исходная normal только +Z.

Сначала preflight всех объектов, затем их write-open и повторная сверка snapshots,
только затем setters. После всех setters проверяются геометрия, handle, native
properties, owner, XData и reactor IDs. Дуга сохраняет bulge, поэтому её фактический
радиус при переносе конца меняется естественным образом. Native associations
могут реагировать; это не отключение сторонних reactors.

Одна transaction; dry-run без write-open/setters. Ответ содержит `results` с теми
же handles, `edited_indices`, полным планом vertices, `atomic:true`, `saved:false`.
Grouped Undo отдельно не обещается. При исключении Commit текст ошибки явно
говорит `commit outcome is uncertain; inspect before retry`; отдельного
machine-readable флага не заявляется. Любой timeout требует чтения состояния.

<a id="view"></a>
## Вид и отображение весов

`dwg_set_system_variable(name="LWDISPLAY",value=true)` включает видимые веса;
допустимы также false и ровно 0/1. Дробные/произвольные ненулевые числа не
округляются. `dwg_get_variables` читает LWDISPLAY. Это не меняет веса объектов
и не сохраняет DWG. [Autodesk LWDISPLAY](https://help.autodesk.com/cloudhelp/2026/ENU/AutoCAD-Core/files/GUID-18CA6E8F-0008-402E-9F6B-2F9ED8A0A7DD.htm).

ZoomToEntity нормирует направление и строит устойчивый WCS→DCS basis для
почти вертикального вида, учитывая twist/aspect. Perspective явно не поддержан.
Capture/region/restore сохраняют строгие identity/camera/data-revision guards;
только доказанные graphics-only уведомления внутри bounded rendering scopes
не делают историю устаревшей. Настоящее native изменение данных требует нового
capture. Обработка stale на динамическом DWG требует отдельной нативной проверки и live traces.

Официальные основания: [Autodesk arbitrary axis algorithm](https://help.autodesk.com/cloudhelp/2020/ENU/AutoCAD-DXF/files/GUID-E19E5B42-0CC7-4EBA-B29F-5E1D595149EE.htm),
[view transforms](https://help.autodesk.com/cloudhelp/2027/DEU/OARX-DevGuide-Managed/files/GUID-FAC1A5EB-2D9E-497B-8FD9-E11D2FF87B93.htm),
[document lock/Undo brackets](https://help.autodesk.com/cloudhelp/2019/ENU/OARXMAC-DevGuide/files/GUID-C2C3D641-AA30-49E6-B800-874DAFA49716.htm)
и [Database.UndoRecording](https://help.autodesk.com/cloudhelp/2026/ENU/OARX-ManagedRefGuide/files/OARX-ManagedRefGuide-Autodesk_AutoCAD_DatabaseServices_Database_UndoRecording.html).

<a id="settings"></a>
## Настройки приложения AutoCAD

Общий каталог настроек AutoCAD 2026/2027 предоставляет три инструмента:

- `dwg_list_settings` — статический каталог с типами, диапазонами, официальными
  источниками, сроком действия и причиной запрета записи. Работает без AutoCAD.
- `dwg_get_settings` — настоящее чтение 1–32 явно выбранных ID в текущем хосте.
  Возвращает профиль, типизированные значения и доступность каждого свойства.
- `dwg_set_settings` — изменение 1–32 разрешённых параметров с точным профилем,
  прежними значениями, dry-run и обязательной проверкой нативного чтения.

Это фиксированный каталог, а не универсальный доступ ко всем вкладкам Options,
Civil 3D, реестру или произвольным именам свойств. Поддержка в исходниках не
означает, что инструменты уже присутствуют в установленном MCP.

`dwg_list_settings` и `dwg_get_settings` регистрируются в `query`;
`dwg_set_settings` — в `modify` и отсутствует в read-only режиме.
Каталог — описание возможностей, не проверка установленного хоста.

### Покрытие

| Группа | Параметров | С записью | Примеры |
| --- | ---: | ---: | --- |
| `display` | 17 | 17 | Размер/цвет перекрестия, фон модели и листа, вкладки, поля бумаги, затенение xref |
| `drafting` | 11 | 4 | Размер и ACI-цвет маркера привязки, размер/видимость апертуры |
| `selection` | 11 | 6 | Предварительный выбор, размер рамки и ручек, ACI-цвета ручек |
| `open_save` | 14 | 7 | Интервал автосохранения, резервные копии, доля инкрементального сохранения, журнал, proxy display |
| `user` | 11 | 5 | Клавиатура, приоритет координат, гиперссылки, резервные единицы DesignCenter |
| `system` | 8 | 2 | Звуковой сигнал и диалог масштаба OLE |
| `output` | 13 | 2 | Предупреждения о бумаге и конфликте очереди печати |
| `files` | 36 | 0 | Чтение путей к ресурсам и файлам |
| `profiles` | 1 | 0 | Текущий профиль; переключения нет |
| Всего | 122 | 43 | Остальные доступны только для чтения при поддержке свойства хостом |

Точный список ID и допустимых значений выдаёт `dwg_list_settings`; необязательный
`group` ограничивает результат одной группой. Наличие свойства в каталоге не
подтверждает его работу в конкретной версии AutoCAD/Civil 3D: getter вернёт
`available:false` и причину вместо выдуманного значения.

### Порядок работы

1. Найти нужные ID через `dwg_list_settings`.
2. Закончить активную команду, закрыть Options и прочитать только нужные ID.
3. Скопировать точные `profile` и `value` из ответа в ожидания setter.
4. Проверить запрос с `dry_run:true`, затем применить с `dry_run:false`.
5. Прочитать результат; внешний вид и отложенный эффект проверяются отдельно.

Пример getter:

```json
{
  "ids": ["display.model_background_color", "open_save.auto_save_interval"]
}
```

Пример setter ниже предполагает, что свежее чтение действительно вернуло профиль
`<<C3D_Metric>>` и интервал 10. Подставлять выдуманное прежнее значение нельзя:

```json
{
  "expected_profile": "<<C3D_Metric>>",
  "changes": [
    {
      "id": "open_save.auto_save_interval",
      "expected_value": {"integer": 10},
      "value": {"integer": 5}
    }
  ],
  "dry_run": true
}
```

Каждое значение содержит ровно одно типизированное поле: `boolean`, `integer`
или `rgb`. Целые числа не принимаются строками или дробями. Indexed ACI — это
`integer` в диапазоне конкретного параметра, а не RGB. Display-цвета принимают
`rgb:[R,G,B]` с тремя целыми компонентами 0–255.

Для Display-цветов getter возвращает `value:{"ole_color":...}` с беззнаковыми
32-битными OLE_COLOR, включая системные флаги, и отдельное информационное `rgb`.
Точное `value` копируется в `expected_value`; новое значение задаётся через `rgb`.
При системном цвете информационное `rgb` равно null. Произвольный raw OLE нельзя
назначить публичным setter, но внутреннее восстановление сохраняет исходные биты.

### Проверки и границы

Весь запрос проверяется до первого setter: известные ID, уникальность, writable,
типы, диапазоны, точный профиль и все прежние значения. Ошибка последнего элемента
не должна приводить к записи первого. Dry-run и запрос без фактических изменений
не вызывают setters. Перед каждой записью снова проверяются профиль и прежнее
значение; после записи читаются все запрошенные параметры.

`verified:true` и `verification:"stored_native_values"` означают совпадение
сохранённых нативных значений. Это не подтверждение немедленной перерисовки экрана,
будущего автосохранения, печати или поведения после перезапуска. `effect` сообщает,
когда настройка применяется; инструмент не запускает эти действия для проверки.
Изменение интервала автосохранения меняет будущую политику, но не выдаёт команду SAVE.

Используется штатный `Application.Preferences` внутри плагина, на главном потоке.
Общий адаптер обслуживает и прежние инструменты перекрестия; их публичный контракт
сохранён, см. [контракт перекрестия](#crosshair-settings). Новый общий setter
дополнительно требует точные прежние значения для каждого ID.

Настройки приложения находятся вне транзакций DWG и обычного Undo. При сбое
восстанавливаются предпринятые записи в обратном порядке, включая setter, который
успел изменить состояние и затем бросил исключение. Результат восстановления
проверяется; ошибка содержит `rollback_verified=true/false`. В другой активный
профиль восстановление не выполняется. Атомарность не гарантируется. После сбоя
или таймаута сначала перечитать состояние; автоматически повторять запись нельзя.
Неуказанные параметры не передаются setters. DWG не сохраняется.

Read-only оставлены свойства с неоднозначной семантикой, общими битовыми масками
или опасными зависимостями: старые Boolean-фасады PICKADD/PICKDRAG/GRIPS/STARTUP,
AUTOSNAP/TRACKPATH/POLARMODE, режимы правой кнопки мыши, пути, профили, загрузка
кода, сброс предупреждений, стили/устройства печати и другие явно описанные случаи.
Их расширение требует отдельного типизированного контракта с точными нативными
режимами и сохранением соседних битов, а не обхода через произвольный код/реестр.

### Проверка контракта

Offline проверяются строгая валидация, OLE/RGB, старые ожидания, полная предпроверка,
dry-run/no-op, проигнорированный setter, изменение перед исключением, отказ
восстановления, смена профиля, ошибки чтения, побочные изменения запрошенных полей,
реальные схемы MCP SDK и состав tools/list в обычном и read-only профилях.
Это не live-проверка AutoCAD. Установка/загрузка DLL и явно разрешённый цикл
get → dry-run → set → get выполняются пользователем по
[TEAM_SETUP.md](TEAM_SETUP.md); без сохранения DWG.

Официальные источники для каждого свойства включены в каталог. Дополнительные
ограничения проверены по [INSUNITSDEFSOURCE](https://help.autodesk.com/cloudhelp/2025/ENU/AutoCAD-Core/files/GUID-4C21BD16-8C17-4469-A91C-F7CC105B770F.htm),
[STARTUP](https://help.autodesk.com/cloudhelp/2026/ENU/AutoCAD-Core/files/GUID-941B7406-2342-47EA-9881-BAFF3A554310.htm)
и [OLEQUALITY](https://help.autodesk.com/cloudhelp/2018/ENU/AutoCAD-Core/files/GUID-1366AB24-F08B-40F9-9EFC-05BED0A1767C.htm).

<a id="crosshair-settings"></a>
## Настройки перекрестия

Для перекрестия предназначены `dwg_get_crosshair_settings` (query) и
`dwg_set_crosshair_settings` (modify). Они используют штатный
`Application.Preferences.Display` внутри плагина на главном потоке AutoCAD.
Внешний COM/ROT, клики по координатам, AutoLISP, C#-сценарии и правка реестра
для выполнения этих операций не используются. Обработчики общие для 2026/2027.
Setter отсутствует в read-only режиме.

Общий адаптер также обслуживает каталог остальных настроек. Новый
`dwg_set_settings` дополнительно проверяет прежнее значение каждого параметра;
см. [общие настройки](#settings). Контракт перекрестия ниже сохранён.

### Что можно изменить

| Поле | Значение |
| --- | --- |
| `crosshair_size` | Целое число 1–100; 100 — перекрестие на всю область чертежа. |
| `model_color_rgb` | `[R,G,B]` для перекрестия пространства модели. |
| `layout_color_rgb` | Отдельный `[R,G,B]` для перекрестия пространства листа. |
| `expected_profile` | Точное имя профиля из свежего чтения; обязательно. |
| `dry_run` | Проверить запрос и показать предполагаемые настройки без записи. |

Все компоненты RGB — целые числа 0–255. Это не ACI и не строка с названием
цвета. Нужно передать хотя бы одно изменяемое поле. Неуказанные поля сохраняются;
неизвестные настройки и некорректные типы отклоняются до записи.

Пример: сначала выполнить `dwg_get_crosshair_settings`, затем подставить
возвращённое поле `profile` в `expected_profile`:

```json
{
  "expected_profile": "<<C3D_Metric>>",
  "crosshair_size": 100,
  "model_color_rgb": [0, 255, 0],
  "layout_color_rgb": [0, 255, 0],
  "dry_run": true
}
```

После проверки плана тот же запрос с `dry_run:false` применяет настройки.
Для зелёного перекрестия **в обоих пространствах** нужны оба поля цвета:
изменение только модели не меняет лист. Изменение не требует переключать
пространство, редактировать объекты или сохранять DWG.

### Как подтверждается результат

Getter возвращает профиль, размер, оба RGB и исходные OLE_COLOR. OLE_COLOR
кодируется как `0x00BBGGRR`; системные цвета с флагами сохраняются как raw OLE,
для них RGB возвращается null. Setter использует типы Integer/unsigned OLE_COLOR
штатного API, затем заново читает все три свойства. `verified:true` выдаётся
только при точном совпадении реального чтения с целевыми значениями.

До записи проверяется текущий профиль; при его смене запрос отклоняется.
Нужно закончить активную команду и закрыть диалог «Параметры». Эти настройки
относятся к профилю приложения и действуют для его чертежей; они не являются
свойствами DWG и не отменяются обычным Undo.

При сбое setter пытается восстановить затронутые свойства в обратном порядке
и проверяет результат. Ошибка содержит `rollback_verified=true/false`.
При сменившемся профиле восстановление в другой профиль не выполняется.
Это не транзакция и не гарантия атомарности. После таймаута/ошибки сначала
перечитать состояние; автоматически повторять запись нельзя.

Настройки оттенков осей X/Y/Z, цвета для отдельных 3D-контекстов, темы,
фона, безопасности и остальных вкладок в контракт этого отдельного инструмента
перекрестия не входят. Наличие этих
исходников не обновляет установленный сервер и загруженную DLL.

### Проверка контракта

Offline-тесты проверяют валидацию, независимость модели/листа, RGB/OLE,
отказ при смене профиля, dry-run, молча проигнорированную запись, частичный
сбой, успешное/неуспешное восстановление и реальные MCP SDK-схемы.
Stdio-тесты выполняют только initialize/tools-list, без CAD-вызовов.

Установка новой сборки и live-проверка выполняются отдельно по TEAM_SETUP.md.
Для проверки хоста после согласованной установки пользователь разрешает
чтение/изменение настроек: get → dry-run → set → get, затем проверяет вид в
модели и на листе. При необходимости возвращаются ранее прочитанные RGB/размер;
исходный системный OLE-цвет с `RGB:null` возвращается вручную через AutoCAD,
поскольку произвольный raw OLE не принимается публичным setter.
Сохранение DWG не требуется. Offline PASS не подтверждает внешний вид курсора
или доступность COM-свойств в работающей сессии AutoCAD/Civil 3D.

Официальные контракты Autodesk:

- [Настройки AutoCAD из .NET](https://help.autodesk.com/cloudhelp/2026/JPN/OARX-DevGuide-Managed/files/GUID-7E1AC51A-CDD6-4CD5-8703-3D3A472BFFC5.htm).
- [CursorSize](https://help.autodesk.com/cloudhelp/2026/ENU/AutoCAD-ActiveX-Reference/files/GUID-5158B66E-025B-455A-8815-340D784DD974.htm).
- [ModelCrosshairColor](https://help.autodesk.com/cloudhelp/2016/FRA/AutoCAD-ActiveX/files/GUID-1AD5F1B8-300A-4FD3-A3C8-B4A9F4B98EC9.htm).
- [LayoutCrosshairColor](https://help.autodesk.com/cloudhelp/2018/DEU/AutoCAD-ActiveX-Reference/files/GUID-464211ED-2883-41D7-BA53-421ADB81F0FD.htm).

<a id="checks"></a>
## Проверка контрактов

Реальные MCP-схемы проверяются SDK-клиентом, а не выводятся из успешной сборки.
Для веса/заливок и текста используются `tools/check-style-mcp.py` и
`tools/check-text-mcp.py` из корня cad-bridge. Python нужен с зависимостями из
`requirements-dev.txt`; на этом компьютере используется существующее окружение
`D:\CAD-Automation\.venv`, на другом — своё. Пример проверки только схем:

```powershell
& 'D:\CAD-Automation\.venv\Scripts\python.exe' -X utf8 .\tools\check-style-mcp.py --target 2027
& 'D:\CAD-Automation\.venv\Scripts\python.exe' -X utf8 .\tools\check-text-mcp.py --target 2027
```

`&` запускает программу; `-X utf8` включает UTF-8; `--target` выбирает год.
Для AutoCAD 2026 заменить значение на `2026`. Без live-флагов эти проверки
читают список и схемы инструментов, не вызывают AutoCAD и не меняют регистрацию
подключения. По умолчанию style-проверка запускает собранную
`dwg-mcp/src/server/bin/Release/net8.0/Bimwright.Dwg.Server.dll` через `dotnet`
из PATH. `--server` задаёт другую DLL, `--dotnet` — другой исполняемый файл .NET.
Исходники style-операций: `dwg-mcp/src/server/Tools/EntityStyleTools.cs`,
`dwg-mcp/src/shared/Handlers/`, `dwg-mcp/src/shared/Cad/`.

Live-проверка требует отдельного разрешения пользователя, соответствующей
загруженной DLL и пустого тестового чертежа Model. Не переключать вкладки:
`--document` должен совпадать с текущим `document_name`. Пример разрешённой
проверки весов и заливок:

```powershell
& 'D:\CAD-Automation\.venv\Scripts\python.exe' -X utf8 .\tools\check-style-mcp.py --target 2027 --live --document 'Чертеж1.dwg'
```

`--live` включает запись в AutoCAD; `--document` ограничивает тест указанным
именем. Тест создаёт свой уникальный слой и объекты, проверяет веса и две
штриховки, затем удаляет их. Удаление пустого тестового слоя использует
доверенный C# через `dwg_send_code` временного клиента. При неопределённом
результате мутации повторы блокируются; имена и handles остаются в отчёте
для разбора cleanup. Чужие объекты и настройки не являются тестовыми данными.

После текстовой операции перечитать те же handles с geometry, сверить стиль,
reference anchor и остальные нужные значения. Проверки Undo/Redo, grip/action
поведения блоков, обновления Properties и повторного открытия выполняются
отдельно в AutoCAD. Схемы, компиляция и test doubles их не подтверждают.
