# CAD bridge: состояние командного проекта

## Подготовка импорта — 2026-10-07

- Репозиторий команды: git@github.com:tovarishnakhimovets-droid/Projects.git.
  Пользователь создал локальную ветку feat/cad-bridge. Git-команды, PR и merge
  выполняет пользователь; агент готовит исходники и offline-проверки.
- Включён снимок bimwright/dwg-mcp v2.0.1, commit
  4d0f75646631186612af926944d7e33affc4757d, со всеми текущими исправлениями
  и новыми C#-файлами. История upstream не импортирована; исходный checkout
  сохранён. Нет вложенного .git, Autodesk DLL, рабочих чертежей и локальной конфигурации.
- Пять первоначальных патчей уже применены: invariant numbers, JsonElement
  переменной, lineweight/hatch, ссылки 2026 и командная упаковка. Патчи лежат
  для происхождения изменений; применять повторно в Projects не надо.
- Диагностические helpers используют собственный built server и параметры
  --server, --target 2026/2027, --dotnet. Упаковка находит Git root Projects,
  включает LICENSE и THIRD_PARTY_NOTICES. Manifest импорта фиксирует source hashes.
- В новом каталоге прошли 708/708 .NET offline-тестов. Настоящий stdio SDK
  с target2026 подтвердил 42 tools и три style-схемы без CAD-вызовов.
  Установочный ZIP с обоими плагинами собран; EXE --help запускается.
  Плагин 2026: 1 прежнее предупреждение WebClient, 2027: 4 прежних предупреждения
  WebClient/ServicePointManager/Roslyn, ошибок нет. Проверены 286 source hashes,
  сохранность исходных файлов относительно общей базы и все 40 файлов ZIP.
  LICENSE/THIRD_PARTY_NOTICES включены, Autodesk DLL отсутствуют. SDK проверил
  initialize/tools-list упакованного self-contained EXE: 42 tools, target2026,
  без CAD-вызовов. Пять патчей прошли reverse --check на импортированных файлах.
  ZIP SHA256: d74a568bbfccb462987d7cff82f9d05c8108e9c1b267a45338f83a7c9bee092d.
  Manifest dirty=true честно отмечает проверочную сборку до коммита; финальный
  выпуск нужно собрать после merge. Установка и конфигурация компьютера не менялись.
- Импорт ещё не закоммичен/слит/развёрнут. Действующие исходники автора пока в
  D:\CAD-Automation\upstream\dwg-mcp, runtime dwg-2027 — предыдущий style-tools.
  Не менять две базы параллельно. После первого merge согласовать переключение
  глобального source-pointer на локальный cad-bridge; runtime обновлять отдельно.

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
  Установка, автозагрузка и нативное подключение 2026 пока не подтверждены.
- Пакет team.2 включает оба года и self-contained win-x64 сервер. team.1
  был только для 2027. ZIP вне Git; установка описана в docs/TEAM_SETUP.md.

## Известные пробелы из рабочих задач

- Full-path document guard отсутствует. has_saved_path может относиться к DWT
  и не подтверждает сохранённый DWG. Нужны отдельный контракт и тесты диагностики.
- AutoLISP блокируется upstream; не обходить отказ через C#.
- SOLID hatch поддерживает одну Circle/closed planar lightweight Polyline,
  без островов и произвольных узоров. Наклонные границы и видимый draw order
  отдельно live не подтверждены.
- В другом проекте прототипированы inventory/удаление Civil ProxyObject и
  network noding. Эти typed-инструменты пока отсутствуют. Следующий шаг:
  bounded inventory с complete/owners/classification и explicit erase с
  expected document/class, затем чистая геометрия noding, handlers/schema/tests.
  Сценарии конкретного чертежа не импортированы и не являются готовыми инструментами.
- dwg_save_drawing через Database.SaveAs текущего открытого DWG дал eFilerError;
  пользователь сохранил через Ctrl+S. Следующий шаг: document/DB filename,
  read-only/file-lock diagnostics и bounded fix; live-save только по запросу.

## Следующие действия

1. Импорт, упаковка и offline-проверки завершены. Пользователь добавляет файлы
   в index, просматривает staged diff, делает commit/push и PR в develop, затем merge.
2. После merge перевести общий source-pointer на этот checkout; собрать финальный
   пакет из принятого commit и установить ГИПу с -Years 2026 / -AcadYear 2026.
3. Первый live-запрос ГИПа: dwg_get_drawing_info, без изменений и сохранения.
   Записать результат; style smoke test включать только отдельно по согласию.
