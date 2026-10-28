# CAD-мост: памятка совместной работы

Все участвуют в разработке: у каждого свой GitHub-аккаунт, своя локальная копия
исходников и постоянная ветка. Общий принятый код — `develop`.
**Git обновляет исходники; установщик обновляет работающий мост.**

Команды — для обычного Windows PowerShell **без запуска от администратора**.
Исходники у всех находятся в `D:\CAD-Automation\team\Projects`.
В примерах `ivan` — личная ветка сотрудника: замени на согласованное имя.
Год — 2027; для AutoCAD 2026 замени его в командах установки и настройки.
Выполняй шаги по порядку; при ошибке остановись и покажи вывод Codex.

## 1. Подключение новой машины — один раз

**1. Программы и доступ.** Установить [Git for Windows](https://git-scm.com/install/windows),
локальный Codex, AutoCAD/Civil 3D и [.NET SDK 8 и 10](https://dotnet.microsoft.com/en-us/download).
После установки Git открыть новое окно PowerShell.

Сотрудник создаёт **свой GitHub-аккаунт** и сообщает Андрею логин.
Андрей открывает репозиторий **Projects → Settings → Collaborators → Add people**
и приглашает этот логин; сотрудник принимает приглашение.
Это даёт право отправлять изменения в общий репозиторий.
[Порядок приглашения на GitHub](https://docs.github.com/en/repositories/managing-your-repositorys-settings-and-features/repository-access-and-collaboration/inviting-collaborators-to-a-personal-repository).

**2. Создать SSH-ключ и привязать к своему аккаунту.** Почту заменить своей.
Если `id_ed25519` уже есть в папке `.ssh` своего профиля, генерацию пропустить;
существующий ключ не перезаписывать.

```powershell
ssh-keygen -t ed25519 -C "your-email@example.com"
```

`-t ed25519` выбирает тип ключа, `-C` добавляет подпись с почтой.
На вопрос о файле нажать Enter — оставить стандартный путь; затем задать
и повторить пароль ключа. Он вводится при запросах SSH.
[Создание ключа по инструкции GitHub](https://docs.github.com/en/authentication/connecting-to-github-with-ssh/generating-a-new-ssh-key-and-adding-it-to-the-ssh-agent).

```powershell
Get-Content ~\.ssh\id_ed25519.pub
```

`~` — папка своего профиля. Скопировать **всю выведенную публичную строку**.
В **своём профиле GitHub** открыть **Settings → SSH and GPG keys → New SSH key**,
тип **Authentication Key**, вставить строку и сохранить. Файл без `.pub` —
закрытый ключ: его не отправляем и в GitHub не вставляем.
[Инструкция GitHub](https://docs.github.com/en/authentication/connecting-to-github-with-ssh/adding-a-new-ssh-key-to-your-github-account).

```powershell
ssh -T git@github.com
```

`-T` проверяет вход без открытия терминала на GitHub. При первом подключении
сверить отпечаток с [официальной инструкцией](https://docs.github.com/en/authentication/connecting-to-github-with-ssh/testing-your-ssh-connection)
и ответить полностью `yes`. Успех — `Hi свой-логин! You've successfully authenticated...`.
Код возврата 1 у этой проверки нормален. SSH-ключ добавляется в профиль сотрудника;
доступ к Projects выдаётся отдельно через приглашение из шага 1.

**3. Получить исходники и завести личную ветку.** Имя, почту и `ivan` заменить своими:

```powershell
git clone --branch develop git@github.com:tovarishnakhimovets-droid/Projects.git 'D:\CAD-Automation\team\Projects'
Set-Location 'D:\CAD-Automation\team\Projects'
git config user.name 'Имя Фамилия'
git config user.email 'your-email@example.com'
git switch -c ivan
git push -u origin ivan
```

`clone --branch develop` скачивает исходники и выбирает `develop`.
`Set-Location` переходит в папку; `config user.name/user.email` задаёт автора
коммитов в этой копии репозитория;
`switch -c` создаёт ветку; `push -u` публикует её и связывает с одноимённой веткой
на GitHub. Это делается один раз. Если личная ветка уже есть на GitHub,
вместо последних двух команд выполни `git switch ivan`.

**4. Установить мост из общей папки.** Сохранить работу и закрыть AutoCAD/Civil 3D и Codex.
В Проводнике открыть общую папку установщика, где видны `install.ps1`
и `setup-codex.ps1`. Нажать правой кнопкой по свободному месту → **Открыть в терминале**;
использовать PowerShell. Обе команды ниже выполнить **из этой папки**.
Отдельно переходить на `Z:` не требуется; ZIP на компьютер копировать тоже не нужно.

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\install.ps1 -Years 2027 -Client none
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\setup-codex.ps1 -AcadYear 2027 -BridgeSourceRoot 'D:\CAD-Automation\team\Projects\cad-bridge'
```

`.\` означает файл в текущей папке. `-NoProfile` запускает PowerShell без личных
настроек; `-ExecutionPolicy Bypass` разрешает скрипт только в этом процессе;
`-File` задаёт запускаемый файл.
`-Years` выбирает плагин; `-Client none` оставляет настройку Codex второй команде.
В ней `-AcadYear` выбирает подключение, а `-BridgeSourceRoot` сообщает Codex,
где лежат местные исходники и общие правила доработки.

Если появилось **Codex CLI is not in PATH**, скрипт напечатает готовый блок
настроек и путь к `config.toml`. В настройках Codex добавь или замени только
этот блок `dwg-2027` вместе с его `.env`, затем перезапусти Codex.

**5. Проверить.** Запустить AutoCAD обычным способом, создать или открыть чертёж,
затем открыть в Codex папку `D:\CAD-Automation\team\Projects` и попросить:
«Проверь dwg-2027: получи сведения об активном чертеже, ничего не меняй и не сохраняй».
Ожидается успешный ответ со сведениями именно об этом чертеже.
Для 2026 в запросе использовать `dwg-2026`.

DLL и EXE устанавливаются **локально**, даже если установщик лежит на NAS.
Сервер запускается самим Codex; вручную запускать EXE, делать NETLOAD и держать
VS Code открытым при обычной работе не требуется.

## 2. Доработка → коммит → push → принятие в develop

**1. Открыть свою папку в терминале и проверить ветку:**

```powershell
Set-Location 'D:\CAD-Automation\team\Projects'
git status --short --branch
```

`--short --branch` показывает краткий статус и ветку. Должна быть своя личная
ветка (`ivan`, `andrew` и т.п.). Codex перед первыми правками сам обновляет
личную ветку и локальную `develop` по правилам `cad-bridge/AGENTS.md`.
При незакоммиченных файлах или расхождении истории остановится и объяснит причину.
Если нужно слияние — человек выполняет согласованный с Codex merge, проверяет
и фиксирует результат. В середине работы автообновление не запускается.

**2. Сделать доработку с Codex.** Передать задачу и указать прочитать
`cad-bridge/AGENTS.md`. Сначала выбрать место доработки по
[CAD_WORKFLOW.md](CAD_WORKFLOW.md#выбор-способа-доработки): существующие инструменты
и инструкция, типизированный MCP или самостоятельная команда LISP.
Codex меняет код, проводит нужные offline-проверки
и записывает результат в `WORK_STATUS.md`; действия в AutoCAD выполняет сотрудник.
Перед пересекающимися правками договориться с коллегой, кто меняет общий участок.

**Для LISP тот же Git-процесс.** Файлы находятся в
`D:\CAD-Automation\team\Projects\cad-bridge\lisp`; после принятия правок коллеги
получают их через обновление репозитория. Сервер и установщик пересобирать
не требуется. Создание и запуск — в [CAD_WORKFLOW.md](CAD_WORKFLOW.md#создание-и-обновление-autolisp).

**3. Просмотреть и опубликовать результат.** Команда `add` ниже подходит,
если все изменения внутри `cad-bridge` относятся к этой работе:

```powershell
git diff
git add cad-bridge
git diff --cached --stat
git commit -m "Краткое описание доработки"
git push
```

`diff` показывает правки; `add` включает их в будущий коммит;
`--cached --stat` показывает список подготовленных файлов и объём правок;
`commit -m` сохраняет их с указанным сообщением; `push` отправляет свою ветку.

**4. Открыть Pull Request на GitHub:** **base: develop**, **compare: твоя ветка**.
После проверки выполняем merge в `develop`. Личную ветку сохраняем;
перед следующей задачей повторяем пункт 1. Если общий код успел измениться,
сначала включаем его в свою ветку и проверяем объединённый результат.

Коммиты, push и обычный merge делает человек; автоматизировано только fast-forward обновление.
Новые инструменты появятся в работающем
MCP после выпуска и установки пакета из следующего раздела.

## 3. Андрей: новый установщик → обновление всех машин

**1. После принятия правок получить чистую develop.** В статусе не должно
быть незакоммиченных файлов:

```powershell
Set-Location 'D:\CAD-Automation\team\Projects'
git status --short --branch
git switch develop
git pull --ff-only origin develop
```

`--ff-only` разрешает обновление только вперёд по истории, без нового merge-коммита;
при расхождении команда остановится. `origin develop` выбирает общую ветку на GitHub.
Попросить Codex проверить готовность выпуска: нужные offline-тесты, поддержку
обоих годов и незавершённые пункты в `WORK_STATUS.md`. Уже успешные проверки
того же кода не повторяем без причины. Для одной правки документации новый
установщик не нужен.

**2. Собрать новую версию.** Текущий выпуск — **v3**. Мелкие изменения —
**v3.1, v3.2**, крупные — **v4, v5**. Метки team/rc не используем;
готовый выпуск не перезаписываем. Нужны SDK 8/10 и установленный AutoCAD 2027
для ссылок при сборке плагина. Можно поручить саму сборку Codex:

```powershell
Set-Location 'D:\CAD-Automation\team\Projects\cad-bridge'
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tools\package-team-dwg.ps1 -Version 3
```

`-Version` задаёт номер, например `3` или `3.1`. Готовый ZIP
`DwgMcp.Setup-v3-win-x64.zip` появится в `cad-bridge/releases/3/`;
он содержит сервер и оба плагина. Внутреннее числовое поле Autodesk/.NET
может показывать `3.0.0`; это тот же v3.

**3. Разместить на NAS.** Один раз распаковать ZIP в **новую папку версии**.
Внутри должны быть `install.ps1`, `setup-codex.ps1`, `manifest.json`, `bundle/`
и `server/`. Готовую папку больше не редактировать; следующий выпуск — другая
папка. Ручная проверка SHA не нужна: установщик сам проверяет файлы по manifest.

**4. Сначала обновить свой компьютер и проверить обычный запуск и чтение MCP.**
Затем сообщить коллегам путь к проверенной папке. На **каждой** машине закрыть
AutoCAD/Civil 3D и Codex. Через Проводник открыть терминал **в папке нового
установщика**, как в шаге 4 раздела 1, и выполнить:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\install.ps1 -Years 2027 -Client none
```

Для 2026 указать `-Years 2026`. Настройку `setup-codex.ps1` повторять не нужно,
если год и пути не менялись. После установки — обычный запуск CAD, чертёж
и проверка чтения из раздела 1. Проверка у Андрея не заменяет проверку Civil 3D
или другого года AutoCAD на машине коллеги.

Чужая работа от публикации новой папки на NAS не прервётся: каждый использует
свою локальную установку и обновляется, когда сам закроет программы.
После сборки вернуться к своей ветке:

```powershell
Set-Location 'D:\CAD-Automation\team\Projects'
git switch andrew
```
