# Общий CAD-мост для команды

Каждый участник устанавливает плагин своего AutoCAD и локальный сервер:
ГИП — AutoCAD 2026 / `dwg-2026`, исходный компьютер — AutoCAD 2027 / `dwg-2027`.
Командная подписка Codex Business сама по себе не объединяет локальные
подключения, личные `AGENTS.md` и историю чатов. Согласованные правила и результаты
работы передаются файлами и Git.

Общий проект для разработки теперь находится в monorepo `Projects`, каталог
`cad-bridge/`; его исходники — `cad-bridge/upstream/dwg-mcp/`. PR #2 с импортом
слит в `develop` 2026-10-07. Единственный source root на компьютере автора —
`D:\CAD-Automation\team\Projects\cad-bridge`; общие правила — в его `AGENTS.md`,
состояние — в `WORK_STATUS.md`. Действующий runtime AutoCAD 2027 сохранён в
`D:\CAD-Automation\releases\dwg-2027\2026-10-06-style-tools`.
Очистка исходников не переустанавливает пакет и не меняет подключение `dwg-2027`.

## Установка коллеге

Дистрибутив для обоих годов: `releases/team/2.0.1-team.2/DwgMcp.Setup-v2.0.1-team.2-win-x64.zip`.
Скачать: [ZIP team.2 для AutoCAD 2026/2027 с Яндекс Диска](https://disk.yandex.ru/d/tzTFb_evK8vwJg).
2026-10-07 публичные метаданные подтвердили имя, размер 39 622 751 байт и
SHA256 канонического проверенного ZIP из `cad-bridge/releases/`:

```text
d74a568bbfccb462987d7cff82f9d05c8108e9c1b267a45338f83a7c9bee092d
```

Проверка скачанного файла из папки с ZIP:

```powershell
Get-FileHash -LiteralPath .\DwgMcp.Setup-v2.0.1-team.2-win-x64.zip -Algorithm SHA256
```

`-LiteralPath` задаёт точное имя файла; `-Algorithm SHA256` выбирает алгоритм
контрольной суммы. Значение `Hash` должно совпасть с указанным выше.

Краткая инструкция внутри ZIP — `INSTALL-RU.md`; её исходник находится
в `packaging/team/INSTALL-RU.md`. Этот ZIP не пересобирался при очистке:
внутри остаются документы и manifest времени его сборки. Дальнейшие правки
инструкций и генерация канонического AGENTS.md попадут в следующий выпуск.

Перед извлечением скачанный ZIP разблокируется через свойства файла или
`Unblock-File -LiteralPath .\DwgMcp.Setup-v2.0.1-team.2-win-x64.zip` из папки с ZIP.
Установщик разблокирует сервер, но не плагин. Коллега извлекает ZIP в локальную
папку, закрывает AutoCAD и Codex и для AutoCAD 2026 выполняет:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\install.ps1 -Years 2026 -Client none
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\setup-codex.ps1 -AcadYear 2026
```

На исходном компьютере с AutoCAD 2027:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\install.ps1 -Years 2027 -Client none
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\setup-codex.ps1 -AcadYear 2027
```

`-NoProfile` отключает личный профиль PowerShell; `-ExecutionPolicy Bypass`
разрешает скрипт в этом процессе; `-File` задаёт скрипт. `-Years` выбирает
плагин нужного года; `-Client none` оставляет настройку клиента второму скрипту;
`-AcadYear` выбирает год подключения Codex. `Unblock-File` снимает отметку
скачанного файла, а `-LiteralPath` задаёт точное имя ZIP.

Сервер установлен в `%LOCALAPPDATA%\Bimwright\Dwg\server\current\dwg-mcp.exe`;
плагин — в `%APPDATA%\Autodesk\ApplicationPlugins\Bimwright.Dwg.bundle`.
Внешний сервер самодостаточный: Python, .NET 8 Runtime и SDK для работы не нужны.
Плагин использует среду AutoCAD: .NET 8 для 2026 и .NET 10 для 2027.
AutoCAD 2026 Update 1.2 переходит на .NET 10; пакет 2026 выбран для подтверждённой
у ГИПа среды .NET 8, обновление AutoCAD не требуется.

`setup-codex.ps1 -AcadYear 2026` или `-AcadYear 2027` регистрирует соответствующее
подключение с профилем `query,modify,drawing,view`, target выбранного года,
`BIMWRIGHT_DWG_READ_ONLY=false`, `BIMWRIGHT_DWG_ENABLE_TOOLBAKER=false`.
Если CLI отсутствует, он печатает TOML для ручной настройки. Скрипт сохраняет
имеющиеся личные инструкции и добавляет указатель на установленный
`server/current/bridge-docs/AGENTS.md`; повторный запуск обновляет указатель без
дублирования, включая блок первого выпуска. Новые пакеты получают этот файл
прямым копированием канонического `cad-bridge/AGENTS.md`, без отдельного исходника
правил runtime. Для проекта разработчика можно указать
`-BridgeSourceRoot 'D:\CAD-Automation\team\Projects\cad-bridge'`;
в этом каталоге обязательны `AGENTS.md`, `docs/CAD_WORKFLOW.md` и `WORK_STATUS.md`.

`-WhatIf` показывает предполагаемые изменения без установки. Проверка `--help`
при обычной установке подтверждает запуск EXE, но не загрузку плагина в AutoCAD.
После запуска AutoCAD и Codex первый запрос — только сведения об активном
чертеже через `dwg_get_drawing_info`.

Версия командного дистрибутива — `2.0.1-team.2`; идентификатор upstream/MCP остаётся
`2.0.1`. Профиль обоих годов содержит те же 42 инструмента. Три typed-операции
оформления уже прошли живую проверку в исходной сессии AutoCAD 2027.
Работа плагина в AutoCAD 2026, установка этого ZIP и живое подключение на
компьютере ГИПа ещё не подтверждены.

## Что передавать через Яндекс Диск

Храните версии ZIP, инструкции, состояние проверок и согласованные файлы проекта.
Дистрибутив устанавливается локально на каждом компьютере. Не размещайте активные
DLL, сервер `current` или рабочую папку `.git` в синхронизируемой папке:
файловая синхронизация не координирует выполняющиеся процессы и изменения исходников.

Правила конкретного чертежа — слои, обозначения и оформление — остаются в его
проектном `AGENTS.md`. Общие правила моста и фактическое состояние доработок
хранятся отдельно в `AGENTS.md`, `docs/CAD_WORKFLOW.md` и `WORK_STATUS.md`.

## Совместная разработка

Для двух разработчиков нужны два локальных checkout и общий Git remote.
Каждый работает в своей ветке; изменения объединяются через ревью и обычный
Git merge. Git setup, создание веток, коммиты, push и merge выполняет пользователь;
агент готовит и проверяет изменения. Проверяйте незакоммиченные изменения до
правок общих исходников.
Сборки и offline-проверки выполняет агент; загрузку DLL и явно разрешённые
живые проверки выполняет пользователь. Одновременные изменения одной
сессии AutoCAD следует выполнять последовательно.

Пользователь клонирует общий репозиторий `Projects`, затем переходит в проект моста:

```powershell
git clone git@github.com:tovarishnakhimovets-droid/Projects.git Projects
cd .\Projects
cd .\cad-bridge
```

Пользователь выбирает согласованную ветку репозитория. Вложенного `.git` у
`upstream/dwg-mcp` нет: исходники и последующие правки отслеживает Git `Projects`.

Исходная база — upstream v2.0.1, commit
`4d0f75646631186612af926944d7e33affc4757d`. В `upstream/dwg-mcp` уже включены
все локальные исправления импорта. `patches/` сохраняет снимок уже применённых
изменений и их происхождение; повторно клонировать upstream или применять
патчи к этим исходникам не требуется. Дальнейшая история правок ведётся в Git
`Projects`. Патчи также включаются в ZIP в `server/bridge-docs/changes/`.

Разработка необязательна для пользователя пакета. Для сборки плагина 2027 нужны
.NET 10 SDK и ссылки установленного AutoCAD 2027. Плагин 2026
собирается как `net8.0-windows` с официальным пакетом Autodesk
[AutoCAD.NET 25.1.0](https://www.nuget.org/packages/AutoCAD.NET/25.1.0)
и его зависимостями только для компиляции; Autodesk DLL не включаются в поставку.
Командный пакет собирается из общего проекта скриптом `tools/package-team-dwg.ps1`;
в ZIP входят плагины AutoCAD 2026/2027, самодостаточный сервер, инструкции
и зафиксированные изменения. Первый выпуск `2.0.1-team.1` содержал только 2027.

Команды сборки и offline-проверки выполняются из `cad-bridge/`:

```powershell
dotnet build .\upstream\dwg-mcp\src\server\Bimwright.Dwg.Server.csproj -c Release
dotnet test .\upstream\dwg-mcp\tests\Bimwright.Dwg.Tests\Bimwright.Dwg.Tests.csproj -c Release
& 'D:\CAD-Automation\.venv\Scripts\python.exe' -X utf8 .\tools\check-style-mcp.py --target 2027
& 'D:\CAD-Automation\.venv\Scripts\python.exe' -X utf8 .\tools\check-dotnet-mcp.py --list-tools --target 2026
```

`-c Release` выбирает конфигурацию сборки; `-X utf8` включает UTF-8;
`--target` задаёт год AutoCAD; `--list-tools` читает только список инструментов.
Используется существующий `D:\CAD-Automation\.venv`; на другом компьютере — свое окружение Python. Зависимости перечислены в requirements-dev.txt.

Для Python helpers нужна среда с пакетами `anyio` и `mcp`; локальное `.venv`
в Git не переносится. Все три helper-скрипта (`check-style-mcp.py`,
`check-dotnet-mcp.py`, `run-dotnet-code.py`) поддерживают `--server`,
`--target 2026` / `--target 2027` и `--dotnet`. По умолчанию используется
собственная сборка `upstream/dwg-mcp/src/server/bin/Release/net8.0/Bimwright.Dwg.Server.dll`,
target 2027 и `dotnet` из PATH. Например, другую DLL можно проверить так:

```powershell
& 'D:\CAD-Automation\.venv\Scripts\python.exe' -X utf8 .\tools\check-style-mcp.py --server 'C:\CAD\build\Bimwright.Dwg.Server.dll' --target 2026 --dotnet 'C:\Program Files\dotnet\dotnet.exe'
```

`--server` задаёт проверяемую DLL; `--dotnet` задаёт исполняемый файл .NET.

`check-style-mcp.py` без `--live` и `check-dotnet-mcp.py --list-tools` проверяют
stdio-схемы без CAD-вызовов. `run-dotnet-code.py` исполняет переданный C# в
AutoCAD и требует отдельно разрешённой задачи. Живые режимы и загрузка новых
плагинов выполняются пользователем по отдельному явному разрешению;
успешная offline-проверка не означает развёртывание новой версии.

Документация: [MCP в Codex](https://learn.chatgpt.com/docs/extend/mcp),
[синхронизация Яндекс Диска](https://yandex.com/support/yandex-360/customers/disk/desktop/windows/en/sync-how-works),
[Git workflows](https://git-scm.com/docs/gitworkflows).
