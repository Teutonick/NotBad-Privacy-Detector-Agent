# Политика конфиденциальности / Privacy Policy

## Русский

Сканирование, детекторы, изучение предпочтений и ИИ-рекомендации работают локально. Телеметрии, рекламы, облачного анализа, удалённого хранения признаков и внешнего API анализа нет. Пути, имена, хеши, находки, оценки, feature vectors и веса рекомендаций не передаются.
Глубокое расследование происхождения запускается только явной кнопкой для выбранной находки. Оно использует ограниченный локальный контекст, Registry и forensic-следы без сетевых запросов и сохраняет результат только в локальной SQLite.
Чтобы приложение не захламляло профиль, аудиты и provenance старше 183 дней удаляются автоматически при запуске, логи ограничены 10 MiB, JSON-снимок последнего аудита — 256 MiB и теоретическим пределом 1 000 000 000 находок, а история оценок ИИ-рекомендаций — 100 000 записей. Если snapshot превышает 256 MiB, сам аудит не прерывается: SQLite остаётся источником истины, просто snapshot не заменяется. Файл обученных весов не ограничивается искусственно: его размер определяется схемой признаков, а не числом оценок.

Сеть используется только после явного действия: для загрузки опциональной MIT-модели YuNet и лицензии с закреплённых официальных URL GitHub, ручной проверки версии по публичным метаданным GitHub Releases либо открытия ссылки в браузере. Проверка обновления не передаёт данные аудита, ничего не скачивает и не устанавливает. YuNet проверяется по SHA-256. Обычные аудиты и ИИ-рекомендации сети не требуют.

Собственные данные находятся в %LOCALAPPDATA%\NotBadPrivacyDetectorAgent. Диалог удаления очищает вторичные кеши/аудиты с сохранением ИИ-рекомендаций и оценок либо удаляет весь собственный каталог. Проверяемые файлы не затрагиваются. Portable EXE удаляется вручную.

При закрытии отменяются внутрипроцессные операции; службы, трей-процесс, задача планировщика, автозагрузка и постоянный worker не остаются.

---

## English

Scanning, detectors, preference learning, and AI recommendations operate locally. There is no telemetry, advertising, cloud analysis, remote feature storage, or external analysis API. File paths, names, hashes, findings, ratings, feature vectors, and recommendation weights are never transmitted.
Deep provenance investigation starts only after an explicit button click for a selected finding. It uses bounded local context, Registry and forensic traces without network requests and stores the result only in local SQLite.
To prevent profile bloat, audits and provenance older than 183 days are pruned at startup, logs are capped at 10 MiB, the last-audit JSON snapshot at 256 MiB with a theoretical ceiling of 1,000,000,000 findings, and AI-recommendation rating history at 100,000 rows. If the snapshot exceeds 256 MiB, the audit itself still completes; SQLite remains canonical and the snapshot is simply not replaced. The trained weights file is not artificially capped: its size depends on the feature schema, not the number of ratings.

Network access is used only after an explicit user action: to download the optional MIT-licensed YuNet model and its license from pinned official GitHub URLs, manually read public GitHub Releases version metadata, or open a link in the browser. The update check sends no audit data and downloads or installs nothing. YuNet is verified against a pinned SHA-256 hash. Regular audits and AI recommendations do not require network access.

Application data resides in `%LOCALAPPDATA%\NotBadPrivacyDetectorAgent`. The cleanup dialog can clear secondary caches and audit results while preserving AI recommendations and ratings, or delete the entire application directory. Audited user files are never touched. The portable EXE is removed manually.

Closing the application cancels in-process operations; no services, tray processes, scheduled tasks, startup entries, or persistent background workers remain.
