# CLAUDE.md

Инструкции для Claude Code при работе в этом репозитории (LivewireBrowser — WPF/.NET 8 приложение для сканирования и прослушивания Livewire AoIP-сети).

## Версионность

Версия хранится в одном месте: [`src/LivewireBrowser.Core/AppVersion.cs`](src/LivewireBrowser.Core/AppVersion.cs) (константы `Version` и `ReleaseDate`). Она отображается на splash screen и больше нигде не дублируется.

Схема версии: `major.minor.debug`, например `0.01.001`.

- **Начальная версия**: `0.01.001`.
- **debug** (последнее число) — увеличивай на 1 самостоятельно при **каждом** запросе пользователя, который меняет код приложения (фикс, фича, рефакторинг и т.д.). Не увеличивай при чисто документационных правках, не затрагивающих код.
- **major** и **minor** — изменяй **только по явной команде пользователя**, никогда самостоятельно.
- При каждом изменении версии обновляй `ReleaseDate` на дату текущего изменения.
- Параллельно с правкой версии добавляй запись в [`HISTORY.md`](HISTORY.md) по уже сложившемуся в файле формату (запрос пользователя полностью, ответ — кратким резюме).

## Структура проекта

- `src/LivewireBrowser.Core` — модели, сетевое обнаружение (LWRP/SAP/Advertisement), кэш, настройки.
- `src/LivewireBrowser.Audio` — приём RTP, декодирование PCM, воспроизведение через WASAPI.
- `src/LivewireBrowser.App` — WPF UI (MVVM).
- `tests/LivewireBrowser.Core.Tests` — xUnit-тесты (покрывают Core и Audio через `InternalsVisibleTo`).

Подробности протоколов и архитектуры — см. [README.md](README.md).

## После изменений

- Прогоняй `dotnet test tests/LivewireBrowser.Core.Tests/LivewireBrowser.Core.Tests.csproj`.
- Пересобирай и переопубликовывай в `release/` через `publish.ps1`, если просили проверить реальную сборку.
