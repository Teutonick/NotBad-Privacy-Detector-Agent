# Лицензии сторонних компонентов / Third-Party Notices

## Русский

Приложение распространяется по MIT, но EXE содержит сторонние компоненты на условиях их собственных лицензий. Release ZIP включает их license-файлы и NuGet-манифесты в каталоге third-party-licenses/.

| Компонент | Версия | Назначение | Лицензия |
|---|---:|---|---|
| Microsoft.Data.Sqlite / Core | 8.0.11 | SQLite | MIT |
| Microsoft.ML / CpuMath / DataView | 4.0.2 | Персональная SDCA-модель | MIT |
| Microsoft.ML.OnnxRuntime / Managed | 1.20.1 | Локальный YuNet-инференс | MIT |
| SixLabors.ImageSharp | 3.1.12 | Изображения и EXIF | Six Labors Split License 1.0; для MIT open-source проекта применяется Apache-2.0 grant |
| SQLitePCLRaw bundle/core/provider/lib.e_sqlite3 | 2.1.6 | Нативный SQLite | Apache-2.0 |
| SQLite engine | bundled | Локальная БД | Public Domain |
| Newtonsoft.Json | 13.0.3 | Зависимость ML.NET | MIT |
| System.Collections.Immutable | 8.0.0 | Зависимость ML.NET | MIT |
| System.Memory | 4.6.0 | Зависимость ML.NET | MIT |
| System.Numerics.Tensors | 8.0.0 | Зависимость ML.NET | MIT |
| System.Reflection.Emit.Lightweight | 4.7.0 | Зависимость ML.NET | MIT |
| System.Threading.Channels | 8.0.0 | Зависимость ML.NET | MIT |
| System.Drawing.Common / Microsoft.Win32.SystemEvents / System.CodeDom | 8.0.x | Windows/runtime | MIT |
| .NET Windows Desktop Runtime и ILLink | 8.0.30 | Self-contained WPF runtime/build | MIT и уведомления .NET |
| Модель YuNet | 2026may, только явная загрузка | Опциональная модель лиц | MIT; SHA-256 закреплён в коде и проверяется |

Предобученная персональная модель не поставляется. Её веса и обучающие строки создаются только на компьютере пользователя. SDK телеметрии, рекламы, аналитики и облачного анализа отсутствуют.

---

## English

The application is distributed under the MIT license, but its binary executable contains third-party components under their own licenses. The release ZIP includes their license files and NuGet manifests in the `third-party-licenses/` directory.

| Component | Version | Purpose | License |
|---|---:|---|---|
| Microsoft.Data.Sqlite / Core | 8.0.11 | SQLite access | MIT |
| Microsoft.ML / CpuMath / DataView | 4.0.2 | Personal SDCA model | MIT |
| Microsoft.ML.OnnxRuntime / Managed | 1.20.1 | Local YuNet inference | MIT |
| SixLabors.ImageSharp | 3.1.12 | Image & EXIF processing | Six Labors Split License 1.0; Apache-2.0 grant applies to this MIT open-source project |
| SQLitePCLRaw bundle/core/provider/lib.e_sqlite3 | 2.1.6 | Native SQLite integration | Apache-2.0 |
| SQLite engine | bundled | Local database engine | Public Domain |
| Newtonsoft.Json | 13.0.3 | ML.NET dependency | MIT |
| System.Collections.Immutable | 8.0.0 | ML.NET dependency | MIT |
| System.Memory | 4.6.0 | ML.NET dependency | MIT |
| System.Numerics.Tensors | 8.0.0 | ML.NET dependency | MIT |
| System.Reflection.Emit.Lightweight | 4.7.0 | ML.NET dependency | MIT |
| System.Threading.Channels | 8.0.0 | ML.NET dependency | MIT |
| System.Drawing.Common / Microsoft.Win32.SystemEvents / System.CodeDom | 8.0.x | Windows / runtime support | MIT |
| .NET Windows Desktop Runtime and ILLink | 8.0.30 | Self-contained WPF runtime & build | MIT and bundled .NET notices |
| YuNet face detector model | 2026may, explicit download only | Optional face detection model | MIT; source-pinned SHA-256 is verified before use |

No pretrained personal model is distributed. Its weights and training records are created strictly on the user's computer. SDKs for telemetry, advertising, analytics, or cloud analysis are entirely absent.
