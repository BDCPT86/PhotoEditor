# Licence texts

The full licence and notice texts for the third-party software shipped with FrameForge. This folder is copied next to
the app (`licenses\`). [THIRD-PARTY-NOTICES.md](../THIRD-PARTY-NOTICES.md) lists every component, its version and its
licence.

| File | Covers |
|---|---|
| `MIT-dotnet-foundation.txt` | Microsoft.Extensions.\*, Microsoft.EntityFrameworkCore.\*, Microsoft.Data.Sqlite.Core, System.Numerics.Tensors (MIT, .NET Foundation and Contributors) |
| `dotnet-runtime-10-THIRD-PARTY-NOTICES.txt` | Notices for code that Microsoft.Extensions.\* 10.0 includes from others |
| `dotnet-runtime-9-THIRD-PARTY-NOTICES.txt` | The same for System.Numerics.Tensors 9.0 |
| `CommunityToolkit.Mvvm-LICENSE.md`, `CommunityToolkit.Mvvm-ThirdPartyNotices.txt` | CommunityToolkit.Mvvm (MIT) |
| `onnxruntime-LICENSE.txt`, `onnxruntime-ThirdPartyNotices.txt` | Microsoft.ML.OnnxRuntime and .Managed, including the native `onnxruntime.dll` (MIT, © Microsoft Corporation) |
| `Apache-2.0.txt` | Serilog, Serilog.Extensions.Hosting, Serilog.Extensions.Logging, Serilog.Sinks.File (© Serilog Contributors); SQLitePCLRaw.\* (© SourceGear, LLC) |
| `VirtualizingWrapPanel-LICENSE.txt` | VirtualizingWrapPanel (MIT, © sbaeumlisberger) |
| `LibRaw-LICENSE.LGPL.txt`, `LibRaw-COPYRIGHT.txt` | LibRaw 0.22.3 (`libraw.dll`, © LibRaw LLC), used under the LGPL 2.1; the exact source is at https://www.libraw.org/data/LibRaw-0.22.3.tar.gz (see `third_party/libraw/0.22.3/README.md`) |

SQLite itself (`e_sqlite3.dll`) is in the public domain and needs no licence text.

A release build (`build/publish.ps1`) also adds the .NET runtime's licence and notices here, because a self-contained
app ships the runtime: `dotnet-netcore-runtime-<version>-*.txt` and `dotnet-windowsdesktop-runtime-<version>-LICENSE.txt`.

**Where the texts came from:** the NuGet packages themselves, as restored on the build machine. Where a package carries
only an SPDX licence expression and no text, the standard text was used: Apache-2.0 from another package's copy, and
MIT with the copyright holder named in the package. None of the Apache-2.0 packages ships a NOTICE file. See
[docs/licence-checklist.md](../docs/licence-checklist.md).
