# Создание схемы и правка вершин

Запросы из чата «Уточнить задачу по ЭОМ» от 2026-10-08: TEXT/MTEXT с выбранным
стилем, создание схемы одним пакетом с одной отменой и изменение вершин без
удаления полилинии. TEXT/стили были интегрированы 2026-10-09; остальные исходники
добавлены 2026-10-16. Это общие CAD-операции: электрические обозначения,
пятижильные линии и проектные слои остаются в проекте чертежа.

Стандартный профиль кандидата `query,modify,drawing,view` содержит 56 инструментов,
с `annotation` — 58, read-only — 19. Наличие исходников не обновляет установленную
DLL. Source/offline/installed/loaded/live фиксируются в единственном WORK_STATUS.md.

## TEXT/MTEXT

`dwg_create_text` и `dwg_create_mtext` доступны через `drawing` или `annotation`,
при их объединении регистрируются однократно. Оба поддерживают существующий
`text_style`, высоту, поворот, слой и ACI. Полный контракт — [TEXT_TOOLS.md](TEXT_TOOLS.md).
Для отдельного MText ширина 0 означает отсутствие автоматического переноса.

## dwg_create_entities

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

## dwg_edit_polyline_vertices

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
capture. Исправление stale на конкретном динамическом DWG ещё требует live traces.

Официальные основания: [Autodesk arbitrary axis algorithm](https://help.autodesk.com/cloudhelp/2020/ENU/AutoCAD-DXF/files/GUID-E19E5B42-0CC7-4EBA-B29F-5E1D595149EE.htm),
[view transforms](https://help.autodesk.com/cloudhelp/2027/DEU/OARX-DevGuide-Managed/files/GUID-FAC1A5EB-2D9E-497B-8FD9-E11D2FF87B93.htm),
[document lock/Undo brackets](https://help.autodesk.com/cloudhelp/2019/ENU/OARXMAC-DevGuide/files/GUID-C2C3D641-AA30-49E6-B800-874DAFA49716.htm)
и [Database.UndoRecording](https://help.autodesk.com/cloudhelp/2026/ENU/OARX-ManagedRefGuide/files/OARX-ManagedRefGuide-Autodesk_AutoCAD_DatabaseServices_Database_UndoRecording.html).
