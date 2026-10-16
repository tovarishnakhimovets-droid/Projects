# Общий CAD-мост для команды

Командный выпуск 2.0.1-team.3 содержит self-contained сервер Windows x64,
плагины AutoCAD 2026 (.NET 8) и AutoCAD 2027 (.NET 10), инструкции и manifest.
Версия upstream/MCP остаётся 2.0.1. Для запуска не нужны Python, SDK или отдельная
установка .NET для сервера; плагин использует среду своего AutoCAD.

Исходники и общие инструкции находятся в Projects/cad-bridge. У каждого
разработчика своя локальная копия репозитория. Пакет устанавливается отдельно
на каждом компьютере; общий аккаунт или рабочее пространство не выполняют установку.

## Получить и установить выпуск

Ответственный публикует ZIP и его .sha256 в отдельной папке версии на NAS.
Локальный результат упаковки: releases/team/2.0.1-team.3/.
Точный путь к общей папке сообщает ответственный; старую ссылку на team.2
нельзя использовать как ссылку на новый выпуск. Идентификатор и состав
распакованного пакета находятся в manifest.json.

1. Скопируйте ZIP выбранной версии и соседний файл .sha256 в локальную папку.
   Проверьте контрольную сумму из этой папки:

~~~powershell
Get-FileHash -LiteralPath .\DwgMcp.Setup-v2.0.1-team.3-win-x64.zip -Algorithm SHA256
~~~

-LiteralPath задаёт точное имя файла; -Algorithm SHA256 выбирает алгоритм.
Значение Hash должно совпасть с .sha256, опубликованным рядом с этим ZIP.
Для другой версии замените имя файла.

2. Если Windows пометила ZIP как скачанный, разблокируйте его через свойства
   файла либо Unblock-File -LiteralPath с тем же именем. Извлеките весь ZIP
   в локальную папку. Установщик не запускается изнутри архива.
3. Закройте AutoCAD и Codex. Из распакованной папки в Windows PowerShell выполните:

~~~powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\install.ps1 -Years 2026 -Client none
~~~

Для AutoCAD 2027 замените 2026 на 2027. -NoProfile отключает профиль PowerShell;
-ExecutionPolicy Bypass разрешает скрипт в этом процессе; -File выбирает скрипт;
-Years задаёт год плагина; -Client none оставляет настройки клиента без изменений.

4. При первой установке настройте MCP нужного года:

~~~powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\setup-codex.ps1 -AcadYear 2026
~~~

-AcadYear задаёт год подключения. Для 2027 замените число.
Скрипт регистрирует dwg-2026 или dwg-2027 и добавляет указатель на установленные
инструкции, сохраняя существующий текст. Если CLI отсутствует, он выводит
точный TOML для редактора настроек. Не добавляйте второй блок с тем же именем.
При обычном обновлении по прежнему пути повторная настройка обычно не требуется.

Стандартные пути установки:

- Плагин: %APPDATA%\Autodesk\ApplicationPlugins\Bimwright.Dwg.bundle.
- Сервер: %LOCALAPPDATA%\Bimwright\Dwg\server\current\dwg-mcp.exe.
- Инструкции: server/current/bridge-docs/AGENTS.md в каталоге сервера.

Профиль подключения: query,modify,drawing,view; target соответствует AutoCAD;
BIMWRIGHT_DWG_READ_ONLY=false, BIMWRIGHT_DWG_ENABLE_TOOLBAKER=false.
Явные параметры подключения имеют приоритет над общим dwgmcp.config.json.
Дополнительный -WhatIf у install.ps1 показывает план без установки.
При обычной установке проверка EXE --help подтверждает запуск сервера,
но не загрузку плагина или связь с AutoCAD.

## Первая проверка и обновления

Запустите AutoCAD и откройте нужный DWG, затем запустите Codex.
Первый запрос — сведения об активном чертеже через dwg_get_drawing_info
нужного подключения, без изменений и сохранения.

У выпуска проверены 1251 offline-тест, сборки обоих плагинов и настоящие MCP
stdio-схемы. Рабочий профиль содержит 58 инструментов; read-only — 20.
Это не подтверждение нативной работы каждой операции. История live-проверок
относится к конкретным компьютерам и версиям; новые операции проверяются отдельно.

Запуск доверенных .lsp доступен после пользовательского MCPTRUSTLISP.
Полный порядок и ограничения — в [CAD_WORKFLOW.md](CAD_WORKFLOW.md#запуск-autolisp).
Сами пользовательские файлы из отдельной папки lisp в этот ZIP не включены.

Новый выпуск публикуется в новой папке NAS. Уже опубликованные ZIP, SHA256
и содержимое старой версии не перезаписываются. Сотрудник обновляет локальную
установку между сеансами, после закрытия AutoCAD и Codex. Автоматического
обновления с NAS пока нет. Рабочие исходники не редактируются в общей папке
всеми чатами одновременно; развитие идёт через Git по правилам ниже.

Инструкции редактируются в одном исходнике и копируются в пакет упаковщиком.
Правила конкретного чертежа остаются в его проектном AGENTS.md.
Предыдущий выпуск team.2 сохраняется без изменений; его история и контрольная
сумма записаны в WORK_STATUS.md исходного проекта.

## Совместная разработка

Для разработки каждый участник получает свою локальную папку репозитория
(checkout). Общая история находится на GitHub. Порядок рабочих веток, проверки
пересекающихся правок и PR в develop описан в
[CAD_WORKFLOW.md](CAD_WORKFLOW.md#совместные-правки); очередь — в
[WORK_STATUS.md](../WORK_STATUS.md#очередь-доработок-из-рабочих-задач).
Git-команды и merge выполняет пользователь; агент готовит и проверяет изменения.

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
