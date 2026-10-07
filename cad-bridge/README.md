# AutoCAD MCP: общий мост команды

Один проект исходников, внешний сервер и два плагина:

| AutoCAD | Плагин | Подключение |
| --- | --- | --- |
| 2026 с .NET 8 | plugin-acad26 / net8.0-windows | dwg-2026 |
| 2027 | plugin-acad27 / net10.0-windows | dwg-2027 |

Внешний сервер — net8.0; установочный ZIP содержит его собственную среду .NET.
Профиль `query,modify,drawing,view` содержит 42 инструмента. Подключение работает
с локальным AutoCAD каждого участника; новый проект чертежа использует тот же мост.

## Где что находится

- `upstream/dwg-mcp/` — снимок upstream v2.0.1 с уже внесёнными исправлениями.
  Вложенного `.git` нет; изменения отслеживает общий репозиторий Projects.
- `tools/` — диагностика MCP и сборка командного ZIP.
- `packaging/team/` — русская установка и настройка Codex.
- `patches/` — история первоначальных изменений относительно upstream.
  После клонирования Projects применять эти патчи повторно не требуется.
- `docs/` — работа с мостом, установка, параметры style-инструментов и происхождение
  исходников. [WORK_STATUS.md](WORK_STATUS.md) хранит проверенные результаты и пробелы.
- `releases/`, `.venv/`, `bin/`, `obj/` — локальные результаты, исключённые из Git.

Сейчас это подготовленный импорт в `feat/cad-bridge`. Он не переключает
действующее подключение и не устанавливает пакет. До первого merge исходная
папка `D:\CAD-Automation\upstream\dwg-mcp` остаётся рабочей базой; после merge
нужно отдельно обновить указатель на общий source checkout.

## Сборка и offline-проверка

Команды из каталога `cad-bridge`. Для разработки нужен .NET 10 SDK.
Сборка плагина 2027 использует DLL установленного AutoCAD 2027. Плагин 2026
собирается с официальными compile-only ссылками AutoCAD.NET 25.1.0;
Autodesk DLL не входят в поставку. Не заменяйте эту версию на 25.1.1 для .NET 8.

```powershell
dotnet test .\upstream\dwg-mcp\tests\Bimwright.Dwg.Tests\Bimwright.Dwg.Tests.csproj -c Release --logger 'console;verbosity=minimal'
```

Дополнительная проверка MCP-схем использует официальный Python SDK и не обращается
к AutoCAD без явных live-флагов:

```powershell
python -m venv .venv
.\.venv\Scripts\python.exe -m pip install -r .\requirements-dev.txt
.\.venv\Scripts\python.exe -X utf8 .\tools\check-style-mcp.py --target 2026
```

Полный ZIP с обоими плагинами:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tools\package-team-dwg.ps1 -Version 2.0.1-team.2
```

Результат: `releases/team/2.0.1-team.2/`. Для повторной сборки выберите новую
версию или пустой `-OutputDir` внутри этого проекта: существующий stage защищён
от перезаписи. Manifest содержит commit общего репозитория и признак dirty;
до коммита пакет пригоден для проверки, окончательный выпуск собирается из
принятых исходников. Установка описана в [docs/TEAM_SETUP.md](docs/TEAM_SETUP.md).

## Развитие

Пользователь создаёт ветку, делает commit/push и merge. Агент меняет код,
собирает и запускает offline-тесты. Пользователь загружает DLL и выполняет
явно включённые live-проверки. После merge новую версию надо собрать и установить
на каждом компьютере: merge сам по себе работающий мост не обновляет.

Общие CAD-операции добавляются как typed MCP-инструменты; оформление конкретного
чертежа остаётся сценарием проекта. Подробнее: [docs/CAD_WORKFLOW.md](docs/CAD_WORKFLOW.md).

Исходный проект: [bimwright/dwg-mcp](https://github.com/bimwright/dwg-mcp), Apache-2.0.
Сохранены [LICENSE](upstream/dwg-mcp/LICENSE) и
[THIRD_PARTY_NOTICES.md](upstream/dwg-mcp/THIRD_PARTY_NOTICES.md).
