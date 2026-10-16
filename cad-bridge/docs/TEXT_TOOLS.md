# Текст и выбранные свойства: typed-инструменты

Текстовые операции объединены с CAD-01…06 в общих исходниках Projects/cad-bridge.
Профиль `query,modify,drawing,view` кандидата содержит 56 инструментов; с
`annotation` — 58, в read-only — 19. Выпущенный `team.2` содержит 42 до
отдельного обновления server/plugin. Изменения исходников не обновляют DLL
и не разрешают live-проверки. Результаты и ограничения записаны в `cad-bridge/WORK_STATUS.md` исходного репозитория.

Перед изменениями прочитать `dwg_get_drawing_info` и свойства нужных handles.
Для AutoCAD 2026 использовать `dwg-2026`, target 2026. Чертёж не сохранять без
прямой просьбы. Имена стилей и слоёв выбирать по данным чертежа и правилам проекта.

## Контракты

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

## Чтение оформления

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

## Примеры параметров

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
