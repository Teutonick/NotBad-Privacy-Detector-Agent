# Политика конфиденциальности / Privacy Policy

## Русский

Сканирование, детекторы, обучение персональной модели и рекомендации работают локально. Телеметрии, рекламы, облачного анализа, удалённого хранения признаков и внешнего API анализа нет. Пути, имена, хеши, находки, оценки, feature vectors и веса персональной модели не передаются.

Сеть используется только после явного действия: для загрузки опциональной MIT-модели YuNet и лицензии с закреплённых официальных URL GitHub либо для открытия ссылки в браузере. YuNet проверяется по SHA-256. Обычные аудиты и персональная модель сети не требуют.

Собственные данные находятся в %LOCALAPPDATA%\NotBadPrivacyDetectorAgent. Диалог удаления очищает вторичные кеши/аудиты с сохранением персональной модели и оценок либо удаляет весь собственный каталог. Проверяемые файлы не затрагиваются. Portable EXE удаляется вручную.

При закрытии отменяются внутрипроцессные операции; службы, трей-процесс, задача планировщика, автозагрузка и постоянный worker не остаются.

---

## English

Scanning, detectors, personal model training, and recommendations operate locally. There is no telemetry, advertising, cloud analysis, remote feature storage, or external analysis API. File paths, names, hashes, findings, ratings, feature vectors, and personal model weights are never transmitted.

Network access is used only after an explicit user action: to download the optional MIT-licensed YuNet model and its license from pinned official GitHub URLs, or to open a link in the browser. YuNet is verified against a pinned SHA-256 hash. Regular audits and the personal model do not require network access.

Application data resides in `%LOCALAPPDATA%\NotBadPrivacyDetectorAgent`. The cleanup dialog can clear secondary caches and audit results while preserving your personal model and ratings, or delete the entire application directory. Audited user files are never touched. The portable EXE is removed manually.

Closing the application cancels in-process operations; no services, tray processes, scheduled tasks, startup entries, or persistent background workers remain.
