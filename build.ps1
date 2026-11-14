[Console]::OutputEncoding = [System.Text.Encoding]::UTF8

# Скрипт всегда работает от корня проекта, даже если запущен из другой папки.
Set-Location -LiteralPath $PSScriptRoot

Write-Host "=== СБОРКА YT SHORTS MANAGER ===" -ForegroundColor Cyan
Write-Host ""

# ============================================================
# 0. ПРОВЕРКА СТРУКТУРЫ ПРОЕКТА
# ============================================================
Write-Host "[0/5] Проверка структуры проекта..." -ForegroundColor Yellow

$projectRoot = Get-Location
$extensionPath = Join-Path $projectRoot "yt-bridge-extension"
$extensionExists = Test-Path $extensionPath

# Обязательные утилиты
$requiredUtils = @("yt-dlp.exe", "ffmpeg.exe", "deno.exe", "icon.ico")
$missingUtils = @()
foreach ($file in $requiredUtils) {
    if (-not (Test-Path (Join-Path $projectRoot $file))) { $missingUtils += $file }
}

# Обязательные файлы расширения
$extensionRequiredFiles = @("manifest.json", "background.js", "content.js", "icon.png")
$missingExtFiles = @()
if ($extensionExists) {
    foreach ($file in $extensionRequiredFiles) {
        if (-not (Test-Path (Join-Path $extensionPath $file))) {
            $missingExtFiles += $file
        }
    }
}

# Вывод результатов проверки
if ($missingUtils.Count -gt 0) {
    Write-Host "⚠️  Не найдены утилиты: $($missingUtils -join ', ')" -ForegroundColor Red
    Write-Host "    Программа будет работать неправильно без них!" -ForegroundColor Red
} else {
    Write-Host "✅ Все утилиты на месте." -ForegroundColor Green
}

if (-not $extensionExists) {
    Write-Host "⚠️  Папка 'yt-bridge-extension' не найдена в корне проекта!" -ForegroundColor Red
    Write-Host "    Положите её в корень рядом с .csproj, иначе расширение НЕ попадёт в архив." -ForegroundColor Red
} elseif ($missingExtFiles.Count -gt 0) {
    Write-Host "⚠️  В 'yt-bridge-extension' не хватает: $($missingExtFiles -join ', ')" -ForegroundColor Red
} else {
    Write-Host "✅ Папка расширения найдена и укомплектована." -ForegroundColor Green
}

Write-Host ""

# ============================================================
# 1. ОЧИСТКА СТАРЫХ ФАЙЛОВ
# ============================================================
Write-Host "[1/5] Очистка старых файлов..." -ForegroundColor Yellow
if (Test-Path "bin\Release") { Remove-Item -Path "bin\Release" -Recurse -Force }
if (Test-Path "obj") { Remove-Item -Path "obj" -Recurse -Force }

# ============================================================
# 2. СБОРКА ПРОГРАММЫ
# ============================================================
Write-Host "[2/5] Сборка программы (займёт около минуты)..." -ForegroundColor Yellow
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeAllContentForSelfExtract=true

if ($LASTEXITCODE -ne 0) {
    Write-Host "❌ Сборка .NET завершилась с ошибкой (код $LASTEXITCODE)!" -ForegroundColor Red
    Pause
    exit 1
}

$publishDir = Get-ChildItem -Path "bin\Release" -Recurse -Directory | Where-Object { $_.Name -eq "publish" } | Select-Object -First 1
if (-not $publishDir) {
    Write-Host "❌ Не удалось найти папку publish!" -ForegroundColor Red
    Pause
    exit 1
}
$publishPath = $publishDir.FullName
Write-Host "📁 Папка сборки: $publishPath" -ForegroundColor Green
Write-Host ""

# ============================================================
# 3. КОПИРОВАНИЕ УТИЛИТ, РАСШИРЕНИЯ И ОЧИСТКА ОТ МУСОРА
# ============================================================
Write-Host "[3/5] Копирование утилит и расширения..." -ForegroundColor Yellow

# Удаляем временные файлы, если они случайно попали в сборку
$junkFiles = @("cookies.txt", "cookies_temp.txt", "app.log", "config.json", "history.json", "update.zip", "update.bat", "update.ps1")
foreach ($junk in $junkFiles) {
    $junkPath = Join-Path $publishPath $junk
    if (Test-Path $junkPath) {
        Remove-Item -Path $junkPath -Force -ErrorAction SilentlyContinue
        Write-Host "🧹 Удалён мусор: $junk" -ForegroundColor DarkGray
    }
}

$updateTempPath = Join-Path $publishPath "update_temp"
if (Test-Path $updateTempPath) {
    Remove-Item -Path $updateTempPath -Recurse -Force -ErrorAction SilentlyContinue
    Write-Host "🧹 Удалена папка update_temp" -ForegroundColor DarkGray
}

# Копируем утилиты
foreach ($file in $requiredUtils) {
    $src = Join-Path $projectRoot $file
    if (Test-Path $src) {
        Copy-Item -Path $src -Destination $publishPath -Force
        Write-Host "✅ Скопирован: $file" -ForegroundColor Green
    }
}

# КОПИРУЕМ ПАПКУ С РАСШИРЕНИЕМ
if ($extensionExists) {
    $extDest = Join-Path $publishPath "yt-bridge-extension"
    if (Test-Path $extDest) {
        Remove-Item -Path $extDest -Recurse -Force
    }
    Copy-Item -Path $extensionPath -Destination $publishPath -Recurse -Force
    
    $copiedCount = (Get-ChildItem -Path $extDest -Recurse -File).Count
    Write-Host "✅ Скопирована папка 'yt-bridge-extension' ($copiedCount файлов)" -ForegroundColor Green
    
    # Покажем список файлов, которые попали в сборку
    Write-Host "   Содержимое расширения:" -ForegroundColor DarkGray
    Get-ChildItem -Path $extDest -Recurse -File | ForEach-Object {
        $relative = $_.FullName.Substring($extDest.Length + 1)
        Write-Host "     • $relative" -ForegroundColor DarkGray
    }
} else {
    Write-Host "⏭️  Пропущено копирование расширения (папка не найдена)." -ForegroundColor Yellow
}

Write-Host ""

# ============================================================
# 4. СОЗДАНИЕ ИНСТРУКЦИИ
# ============================================================
Write-Host "[4/5] Создание инструкции..." -ForegroundColor Yellow

$instruction = @"
===============================================================
       YT SHORTS MANAGER (Автор: IZYXxx)
       Подробная инструкция по использованию
===============================================================

Привет! Держи программу для скачивания и обрезки видео с YouTube.
Внутри архива есть ВСЁ необходимое для работы.

---------------------------------------------------------------
📦 ШАГ 1: УСТАНОВКА (Распаковка)
---------------------------------------------------------------
1. Распакуй скачанный ZIP-архив в ЛЮБУЮ папку (например, на Рабочий стол).
2. ВАЖНО: Не запускай программу прямо из архива! Сначала извлеки файлы.
3. Не переноси .exe файл отдельно от папки! В папке лежат важные утилиты 
   (yt-dlp.exe, ffmpeg.exe, deno.exe), без которых программа работать не будет.

---------------------------------------------------------------
🚀 ШАГ 2: ПЕРВЫЙ ЗАПУСК
---------------------------------------------------------------
1. Запусти файл "YTShortsManager.exe" двойным кликом.
2. Windows может показать СИНЕЕ ОКНО защиты (SmartScreen): 
   * Нажми "Подробнее" -> "Выполнить в любом случае". 
   * Это нормально для новых программ, не бойся.
3. Программа мгновенно свернется в системный трей (возле часов в правом нижнем углу). 
   Это правильно! Она должна работать в фоне.

---------------------------------------------------------------
🔥 ШАГ 3: КАК СКАЧИВАТЬ (Самый простой способ)
---------------------------------------------------------------
В программе есть "Умный буфер обмена". Тебе не нужно ничего открывать!
1. Открой YouTube в браузере.
2. Нажми под видео кнопку "Поделиться" -> "Копировать ссылку" (или просто Ctrl+C).
3. Переключись на окно программы (нажми на иконку в трее).
4. Ссылка САМА вставится в поле ввода! Просто нажми красную кнопку "СКАЧАТЬ ВИДЕО".
5. Прогресс-бар покажет статус загрузки, а видео появится в папке "Загрузки\YT_Shorts".

---------------------------------------------------------------
🌐 ШАГ 4: КНОПКА ПРЯМО НА САЙТЕ (Для удобства)
---------------------------------------------------------------
Если хочешь скачивать видео в 1 клик прямо с сайта YouTube:
1. Открой браузер (Chrome, Яндекс или Edge).
2. Введи в адресную строку: chrome://extensions/ (или browser://extensions/)
3. Включи "Режим разработчика" (переключатель справа вверху).
4. Нажми "Загрузить распакованное расширение".
5. Выбери папку "yt-bridge-extension" (она лежит внутри папки с программой).
6. Зайди на YouTube, обнови страницу (F5). Возле лайков появится синяя кнопка ⬇️.

---------------------------------------------------------------
🎛️ ЧТО ЕЩЁ УМЕЕТ ПРОГРАММА
---------------------------------------------------------------
* Очередь загрузок: Можешь накидать 5 ссылок подряд, программа скачает их по очереди.
* История: Нажми кнопку "История", чтобы увидеть, что ты скачивал. Клик по видео откроет его в плеере.
* Обрезка: В панели программы можно указать время "От" и "До", чтобы скачать только кусок видео.
* Очистка кэша: Кнопка "Очистить кэш" удаляет историю и мусорные файлы.

---------------------------------------------------------------
⚠️ ВАЖНЫЕ СОВЕТЫ И РЕШЕНИЕ ПРОБЛЕМ
---------------------------------------------------------------
* Антивирус ругается: Добавь папку с программой в исключения антивируса. 
  Программа собрана самостоятельно, поэтому антивирусы могут её подозревать.
* Нет звука в видео: Убедись, что файл "ffmpeg.exe" лежит в той же папке, 
  что и "YTShortsManager.exe". Не удаляй его!
* Ошибка "Не хватает файлов": Программа сама скажет, какого файла нет. 
  Скачай его и положи рядом с программой.
* Не скачивается (ошибка 403/сессии): Нажми кнопку "Обновить yt-dlp" в программе, 
  чтобы скачать свежую версию движка.

===============================================================
           Приятного пользования! Сделано с душой 💙
                              IZYXxx
===============================================================
"@

$instruction | Out-File -FilePath "$publishPath\ИНСТРУКЦИЯ.txt" -Encoding utf8
Write-Host "✅ Инструкция создана." -ForegroundColor Green
Write-Host ""

# ============================================================
# 5. СОЗДАНИЕ ZIP-АРХИВА И ПРОВЕРКА СОДЕРЖИМОГО
# ============================================================
Write-Host "[5/5] Создание ZIP-архива..." -ForegroundColor Yellow

$releaseDir = Split-Path $publishPath -Parent
$zipPath = Join-Path $releaseDir "YTShortsManager.zip"

if (Test-Path $zipPath) { Remove-Item -Path $zipPath -Force }

try {
    Compress-Archive -Path "$publishPath\*" -DestinationPath $zipPath -Force -CompressionLevel Optimal
    Write-Host "✅ ZIP-архив создан." -ForegroundColor Green
} catch {
    Write-Host "❌ Ошибка создания ZIP: $_" -ForegroundColor Red
    Pause
    exit 1
}

# ============================================================
# ВЕРИФИКАЦИЯ: проверяем, что расширение реально в ZIP
# ============================================================
Write-Host ""
Write-Host "🔍 Проверка содержимого архива..." -ForegroundColor Yellow

Add-Type -AssemblyName System.IO.Compression.FileSystem
$zip = [System.IO.Compression.ZipFile]::OpenRead($zipPath)
try {
    $totalEntries = $zip.Entries.Count
    
    # Проверка ключевых файлов
    $checks = @{
        "YTShortsManager.exe"            = "Программа"
        "yt-dlp.exe"                     = "Движок yt-dlp"
        "ffmpeg.exe"                     = "FFmpeg"
        "deno.exe"                       = "Deno"
        "ИНСТРУКЦИЯ.txt"                 = "Инструкция"
        "yt-bridge-extension/manifest.json" = "Расширение (manifest)"
        "yt-bridge-extension/background.js" = "Расширение (background)"
        "yt-bridge-extension/content.js"    = "Расширение (content)"
    }
    
    $allOk = $true
    foreach ($key in $checks.Keys) {
        $entry = $zip.Entries | Where-Object { $_.FullName.Replace('\', '/') -eq $key } | Select-Object -First 1
        if ($entry) {
            Write-Host "  ✅ $($checks[$key])" -ForegroundColor Green
        } else {
            Write-Host "  ❌ $($checks[$key]) — НЕ НАЙДЕНО!" -ForegroundColor Red
            $allOk = $false
        }
    }

    # Архив должен быть «плоским»: exe в корне, без лишней верхней папки.
    $nestedExe = $zip.Entries | Where-Object { $_.FullName.Replace('\', '/') -match '^[^/]+/YTShortsManager\.exe$' }
    if ($nestedExe) {
        Write-Host "  ❌ Архив упакован с верхней папкой — обновление установится неверно!" -ForegroundColor Red
        Write-Host "     Пересоберите ZIP этим скриптом и не пересжимайте папку вручную." -ForegroundColor Red
        $allOk = $false
    }
    
    # Количество файлов расширения в архиве
    $extEntries = $zip.Entries | Where-Object { $_.FullName.Replace('\', '/') -like "yt-bridge-extension/*" }
    $extCount = $extEntries.Count
    
    if ($extCount -ge 4) {
        Write-Host "  ✅ Расширение: $extCount файлов в архиве" -ForegroundColor Green
    } else {
        Write-Host "  ⚠️  Расширение: только $extCount файлов (ожидалось минимум 4)" -ForegroundColor Yellow
        $allOk = $false
    }
    
    Write-Host ""
    Write-Host "  📦 Всего файлов в архиве: $totalEntries" -ForegroundColor White
    Write-Host ""
    
    if (-not $allOk) {
        Write-Host "⚠️  ВНИМАНИЕ: Некоторые файлы отсутствуют в архиве!" -ForegroundColor Red
        Write-Host "    Проверьте структуру проекта перед публикацией релиза." -ForegroundColor Red
    }
} finally {
    $zip.Dispose()
}

# ============================================================
# ИТОГОВЫЙ ОТЧЁТ
# ============================================================
$hash = (Get-FileHash -Path $zipPath -Algorithm SHA256).Hash.ToLower()
$zipSizeMB = [math]::Round((Get-Item $zipPath).Length / 1MB, 2)

# Читаем версию из .csproj
$version = "0.0.0"
$csproj = Get-ChildItem -Path $projectRoot -Filter "*.csproj" | Select-Object -First 1
if ($csproj) {
    try {
        [xml]$xml = Get-Content -LiteralPath $csproj.FullName
        $versionNode = @($xml.Project.PropertyGroup | ForEach-Object { $_.Version } | Where-Object { $_ }) | Select-Object -First 1
        if ($versionNode) { $version = [string]$versionNode }
    } catch { }
}

Write-Host ""
Write-Host "════════════════════════════════════════════════════════════════" -ForegroundColor Cyan
Write-Host "                 ✅ СБОРКА УСПЕШНО ЗАВЕРШЕНА!" -ForegroundColor Green
Write-Host "════════════════════════════════════════════════════════════════" -ForegroundColor Cyan
Write-Host ""
Write-Host "  📦 Версия:       $version" -ForegroundColor White
Write-Host "  🗜️  ZIP-архив:   $zipPath" -ForegroundColor White
Write-Host "  💾 Размер ZIP:   $zipSizeMB MB" -ForegroundColor White
Write-Host ""
Write-Host "  🔑 SHA-256 (скопируйте в version.json):" -ForegroundColor Yellow
Write-Host "  $hash" -ForegroundColor White
Write-Host ""
Write-Host "────────────────────────────────────────────────────────────────" -ForegroundColor DarkGray
Write-Host "  📋 ШАБЛОН version.json:" -ForegroundColor Yellow
Write-Host ""
Write-Host "  {" -ForegroundColor DarkGray
Write-Host "    `"version`": `"$version`"," -ForegroundColor DarkGray
Write-Host "    `"download_url`": `"https://github.com/bestotarkov-max/YTShortsManager/releases/download/v$version/YTShortsManager.zip`"," -ForegroundColor DarkGray
Write-Host "    `"sha256`": `"$hash`"," -ForegroundColor DarkGray
Write-Host "    `"changelog`": `"Что изменилось в этой версии...`"" -ForegroundColor DarkGray
Write-Host "  }" -ForegroundColor DarkGray
Write-Host ""
Write-Host "────────────────────────────────────────────────────────────────" -ForegroundColor DarkGray
Write-Host "  📌 Следующие шаги:" -ForegroundColor Cyan
Write-Host "    1. Скопируйте SHA-256 выше в файл version.json" -ForegroundColor White
Write-Host "    2. Создайте релиз v$version на GitHub" -ForegroundColor White
Write-Host "    3. Прикрепите файл YTShortsManager.zip к релизу" -ForegroundColor White
Write-Host "    4. Запушьте обновлённый version.json в репозиторий" -ForegroundColor White
Write-Host "════════════════════════════════════════════════════════════════" -ForegroundColor Cyan
Write-Host ""

# Открываем папку с ZIP и выделяем сам файл
Start-Process explorer.exe "/select,`"$zipPath`""

Pause