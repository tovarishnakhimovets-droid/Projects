# Чтение динамического блока

`dwg_inspect_block` — read-only инструмент query-профиля. Он читает одну явно
указанную вставку и её effective definition. Не меняет чертёж, текущий вид,
выделение, динамические значения и не сохраняет DWG. Реализация общая для
плагинов 2026/2027; состояние сборки и живой проверки — в WORK_STATUS.md.

## Параметры

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

## Ответ и единицы

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

## Ограничения

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
