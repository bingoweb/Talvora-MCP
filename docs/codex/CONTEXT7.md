# Context7 map — Talvora

Maintained by Claude (CONTEXT7 PROTOCOL in AGENTS.md). Every id below was checked against Context7 on 2026-11-01. Use the id exactly as written; do not call `resolve-library-id` for these. **Gap** = the pinned version is newer than the newest version Context7 has indexed: use the id for long-stable APIs and confirm changes in the Official link. Versions come from `global.json` and `Directory.Packages.props`.

| Area | Pinned | Context7 id | Official (vendorSources) |
|---|---|---|---|
| .NET / C# / SDK and CLI | .NET SDK 10.0.404 | `/dotnet/docs`; runtime source: `/dotnet/runtime` | https://learn.microsoft.com/dotnet/ |
| ASP.NET Core (MCP HTTP host) | .NET 10 | `/dotnet/aspnetcore.docs` | https://learn.microsoft.com/aspnet/core/ |
| MCP C# SDK | `ModelContextProtocol` 2.4.0 | `/modelcontextprotocol/csharp-sdk`; newest indexed version `/modelcontextprotocol/csharp-sdk/v2.3.0` — **gap** (2.4.0: release notes) | https://github.com/modelcontextprotocol/csharp-sdk |
| Roslyn | `Microsoft.CodeAnalysis.*` | `/dotnet/roslyn` | https://learn.microsoft.com/dotnet/csharp/roslyn-sdk/ |
| SQLite (ADO.NET) | `Microsoft.Data.Sqlite` 10.0.14 | `/dotnet/docs` (Microsoft.Data.Sqlite pages); SQL: `/websites/sqlite_docs` | https://learn.microsoft.com/dotnet/standard/data/sqlite/ |
| MVVM | `CommunityToolkit.Mvvm` | `/websites/learn_microsoft_en-us_dotnet_communitytoolkit_mvvm` | https://learn.microsoft.com/dotnet/communitytoolkit/mvvm/ |
| Desktop UI | `WPF-UI` 4.3.0 | `/lepoco/wpfui` | https://wpfui.lepo.co/ |
| WPF (animation, controls, XAML) | .NET 10 WPF | `/dotnet/docs-desktop` | https://learn.microsoft.com/dotnet/desktop/wpf/ |
| WinForms (setup window, KUR1) | .NET 10 WinForms | `/dotnet/docs-desktop` | https://learn.microsoft.com/dotnet/desktop/winforms/ |
| Win32 API (DWM title bar, KUR1) | Windows 11 | `/websites/learn_microsoft_en-us_windows_win32_api` | https://learn.microsoft.com/windows/win32/api/ |
| TOML | `Tomlyn` 2.11.0 | `/xoofx/tomlyn/2.10.1` — **gap** (2.10.2 and 2.11.0: release notes) | https://github.com/xoofx/Tomlyn |
| YAML | `YamlDotNet` 18.1.0 | `/aaubry/yamldotnet/v16.3.0` — **gap** | https://github.com/aaubry/YamlDotNet/wiki |
| Diffs | `DiffPlex` | `/mmanela/diffplex` | https://github.com/mmanela/diffplex |
| Git CLI | Git for Windows 2.56 | `/websites/git-scm` | https://git-scm.com/docs |
| GitHub Actions (release workflow, RL4) | `windows-latest` runner | `/websites/github_en_actions` | https://docs.github.com/actions |
| MSBuild (PK2) | `Microsoft.Build.Framework` 18.9.6 and `Microsoft.NET.StringTools` 18.9.6 (both `ExcludeAssets="runtime"`; the SDK's MSBuild is 18.9.15) | `/dotnet/msbuild`; how-to pages: `/websites/learn_microsoft_visualstudio_msbuild`; Locator: `/microsoft/msbuildlocator` | https://learn.microsoft.com/visualstudio/msbuild/ |
| uv (Python runs) | uv 0.13.x | `/astral-sh/uv` | https://docs.astral.sh/uv/ |
| Visual Studio and Windows SDK tools | VS 2022 Build Tools | `/microsoftdocs/visualstudio-docs` | https://learn.microsoft.com/visualstudio/ |
| Android tools | Android SDK command-line tools | `/websites/developer_android_develop` | https://developer.android.com/tools |
| Context7 itself (CTX1, CTX3) | v2 API | `/upstash/context7` (`GET https://context7.com/api/v2/context?libraryId=<id>&query=<question>&type=txt` with `Authorization: Bearer <key>`; Talvora reads the user's key from `C:\ProgramData\Talvora\Context7\api-key`) | https://context7.com/docs |
