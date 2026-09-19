# Windows development environment

Talvora is developed and validated on the owner Windows machine. The environment is intentionally broad enough to build, test, package, and debug applications across the common Windows, web, backend, mobile, native, and container toolchains.

## Package-management rule

- Chocolatey is the Windows package manager for Talvora development and setup.
- WinGet is not used.
- Before installing a toolchain package, compare the installed Chocolatey version with the current feed.
- For SDKs that have their own upstream release channel, verify the upstream stable release as well. Chocolatey metadata can lag upstream releases.

## Verified toolchains

The following environment was verified on 2026-09-19:

| Toolchain | Verified version / state |
| --- | --- |
| .NET SDK | 10.0.401 |
| .NET runtime | 10.0.12 |
| Python | 3.14.7 |
| pip | 26.2.1 |
| Node.js LTS | 24.21.0 |
| npm | 11.19.0 |
| PowerShell | 7.6.6 |
| Git | Installed and used for Talvora development |
| Visual Studio Build Tools | Visual Studio 2022 Build Tools 17.14.41 |
| MSBuild | 17.14.60.43110 |
| MSVC x64 compiler | 19.44.35229 |
| Windows SDK | 10.0.26100.0 |
| CMake | 4.4.3 |
| Ninja | 1.13.2 |
| Go | 1.27.1 |
| Rust | 1.98.1 stable |
| Cargo | 1.98.1 |
| Java | Eclipse Temurin 21.0.12.1+1 LTS |
| Gradle | 9.7.1 |
| Flutter | 3.47.5 stable |
| Dart | 3.13.4 stable |
| Android platform | android-36 |
| Android Build Tools | 37.0.0 |
| Android platform-tools / ADB | 37.0.1 |
| Android Emulator | 37.1.11.0 |
| Docker CLI | 29.8.1 |
| Docker Compose | 5.5.1 |
| Docker Desktop | 4.91.0 |
| WSL | 2.7.14 |

## Windows native development

Visual Studio 2022 Build Tools is installed with the VC++ workload. The Windows SDK discovery surface sees SDK 10.0.26100.0 with x64, x86, and ARM64 tools, including RC, MT, and SignTool.

CMake and Ninja are installed independently and are also discoverable through Talvora. Native C/C++ projects can therefore use MSBuild/Visual Studio generators or CMake + Ninja as appropriate.

## Java and Android

The Java 21 LTS runtime is taken from the current Eclipse Adoptium GA release instead of assuming the Chocolatey Temurin package is current. `JAVA_HOME` points to the verified current JDK.

Android uses the current Google command-line tools release with its published SHA-256 verified before installation. The machine has:

- `ANDROID_HOME=C:\Android\android-sdk`
- `ANDROID_SDK_ROOT=C:\Android\android-sdk`
- platform-tools
- android-36 platform
- Build Tools 37.0.0
- emulator

The current command-line tools warn that `sdkmanager` is deprecated in favor of the newer Android CLI. Existing automation may continue to use `sdkmanager` while it remains functional, but new work should prefer the supported Android CLI path.

## Rust

Rustup is installed and the stable toolchain is updated from the Rust upstream channel. Both the Talvora LocalSystem profile and the interactive owner profile have a Rust toolchain; the machine PATH includes the interactive user's Cargo bin directory so normal development shells and Talvora can resolve `rustc` and `cargo`.

## Flutter

The Chocolatey Flutter package is only a bootstrap/install source. After installation, Flutter is moved to the upstream `stable` channel and upgraded with Flutter's own updater. The active SDK is therefore allowed to be newer than the Chocolatey package metadata.

## Docker Desktop and WSL

Docker Desktop 4.91.0 is installed together with Docker CLI 29.8.1 and Docker Compose 5.5.1.

Two Windows-specific installation details were discovered during setup:

1. Docker Desktop's MSI needs the Windows Server service (`LanmanServer`) when it creates the local `docker-users` group. If that service is disabled, the MSI can fail with error 1603 at the `CreateGroup` custom action.
2. Docker Desktop 4.91.0 can hit a WiX/DTF managed-custom-action named-pipe collision during installation. The MSI itself exposes the supported `DISABLEANALYTICS` property. Installing with `DISABLEANALYTICS=1` avoids the asynchronous analytics action that caused the collision on this machine.

The `docker-users` local group exists and contains the interactive owner account.

Docker's WSL2 backend requires both Windows optional features:

- `Microsoft-Windows-Subsystem-Linux`
- `VirtualMachinePlatform`

After either feature is enabled, Windows must be restarted before treating Docker engine readiness as a meaningful test. Firmware virtualization is enabled on the machine; Windows reports an active hypervisor.

## PATH and service refresh

Talvora runs as a Windows service and inherits its process environment at service start. Installing a new SDK or changing the machine PATH does not retroactively update the already-running Talvora process. Restart Talvora, or reinstall/switch the runtime through the native installer when appropriate, before interpreting a missing command as a missing installation.

## Validation principle

Do not rerun the complete Talvora regression matrix merely because an external SDK was installed. Validate the affected toolchain directly (version, compiler/runtime invocation, and a small smoke build/run where useful), then run Talvora's broad regression suites only when Talvora source changes justify them.
