# YT Shorts Manager

Приложение для Windows: скачивание и обрезка видео с YouTube (в первую очередь Shorts) — с расширением для браузера, очередью загрузок, историей и автообновлением.

**Автор:** IZYXxx
**Версия:** 1.5.0
**Платформа:** Windows 10/11 x64

---

## Возможности

- Скачивание видео и Shorts по ссылке.
- Обрезка фрагмента по времени начала и конца.
- Выбор качества (best, 2160, 1440, 1080, 720, 480, 360).
- Только аудио (MP3).
- Субтитры, обложка, SponsorBlock, поддержка плейлистов.
- Очередь загрузок с прогрессом и статусами.
- История загрузок с обложками.
- Расширение для Chrome/Edge/Firefox: кнопка на странице YouTube, передача куки для закрытых видео.
- Тёмная тема, локализация RU/EN.
- Работа в системном трее, автозапуск, автоподхват ссылки из буфера обмена.
- Автообновление с проверкой SHA-256 и откатом при ошибке.

## Установка

1. Скачайте `YTShortsManager-v1.5.0.zip` из раздела **Releases**.
2. Распакуйте архив в любую папку (не запускайте изнутри ZIP).
3. Запустите `YTShortsManager.exe`. .NET устанавливать не нужно.

### Расширение для браузера

1. Откройте `chrome://extensions` (или `edge://extensions`).
2. Включите **Режим разработчика**.
3. Нажмите **Загрузить распакованное расширение** и выберите папку `yt-bridge-extension`.

## Сборка из исходников

Требуется [.NET SDK 9.0](https://dotnet.microsoft.com/download) и PowerShell 5.1+.

1. Положите в корень проекта сторонние утилиты (в репозиторий они не входят):
   - `yt-dlp.exe` — https://github.com/yt-dlp/yt-dlp/releases/latest
   - `ffmpeg.exe` — https://www.gyan.dev/ffmpeg/builds/ (essentials)
   - `deno.exe` — https://github.com/denoland/deno/releases/latest
2. Запустите сборку:

```powershell
powershell -ExecutionPolicy Bypass -File .\build.ps1
```

или двойным кликом по `Собрать.bat`.

Результат: `bin/Release/net9.0-windows/win-x64/YTShortsManager.zip` (self-contained, single-file). Скрипт также проверяет содержимое архива и выводит SHA-256.

## Структура проекта

```
App.xaml / App.xaml.cs          — точка входа, единый экземпляр, глобальные ошибки
MainWindow.xaml / .cs           — главное окно, очередь, настройки
HistoryWindow.xaml / .cs        — окно истории
VideoUtils.cs                   — проверка ссылок YouTube
Models/                         — модели данных
Services/
  AppPaths.cs                   — все пути
  AppStrings.cs                 — локализация RU/EN
  AppUpdateService.cs           — проверка и установка обновлений
  HttpBridgeServer.cs           — локальный HTTP-мост для расширения (127.0.0.1:5000)
  YtDlpService.cs               — запуск yt-dlp, куки, обновление yt-dlp
  HistoryStore.cs               — история загрузок
  SettingsStore.cs              — настройки
  VideoInfoCache.cs             — кэш длительности видео
  FileLog.cs                    — лог с ротацией
  StartupManager.cs             — автозапуск
  WindowTheme.cs                — тема окна
yt-bridge-extension/            — расширение для браузера (Manifest V3)
build.ps1                       — сборка релизного ZIP
version.json                    — манифест обновлений
```

## Публикация релиза

1. Поднимите `<Version>` в `YTShortsManager.csproj`.
2. Соберите архив через `build.ps1` (архив должен быть «плоским», `exe` в корне).
3. Создайте релиз с тегом `vX.Y.Z` и прикрепите ZIP.
4. Обновите `version.json`:

```json
{
  "version": "X.Y.Z",
  "download_url": "https://github.com/<user>/YTShortsManager/releases/download/vX.Y.Z/YTShortsManager-vX.Y.Z.zip",
  "sha256": "<хэш из build.ps1>",
  "changelog": "Краткое описание изменений"
}
```

Имя файла в `download_url` должно совпадать с именем ассета. При пустом или неверном `sha256` автообновление блокируется.

## Безопасность

- Куки и логи хранятся в `%LOCALAPPDATA%\YTShortsManager`, а не рядом с программой.
- Локальный сервер слушает только `127.0.0.1` и принимает запросы лишь от расширений (`Origin: chrome-extension://` / `moz-extension://`).
- Ограничения: тело запроса 2 МБ, очередь 30 загрузок, история 50 записей, лог 2 МБ.
- Обновления скачиваются только с доверенных хостов GitHub по HTTPS и проверяются по SHA-256.
- Расширение запрашивает только разрешения `cookies` и `storage`.

## Лицензия

Лицензия не указана. Перед публикацией добавьте файл `LICENSE` (например, MIT), если планируете разрешать использование и модификацию кода.
