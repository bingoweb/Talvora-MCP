from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]


def read(path: str) -> str:
    return (ROOT / path).read_text(encoding="utf-8-sig")


def test_one_click_broker_service_launchers_exist():
    install = ROOT / "TALVORA-BROKER-KUR.bat"
    remove = ROOT / "TALVORA-BROKER-KALDIR.bat"

    assert install.is_file()
    assert remove.is_file()
    assert "Install-BrokerService.ps1" in install.read_text(encoding="utf-8")
    assert "Uninstall-BrokerService.ps1" in remove.read_text(encoding="utf-8")


def test_installer_publishes_broker_to_program_files_and_runs_as_local_system():
    script = read("scripts/Install-BrokerService.ps1")

    assert "Talvora.ElevatedBroker.csproj" in script
    assert "ProgramFiles" in script
    assert "Talvora\\Broker" in script or "Talvora\\Broker" in script.replace("/", "\\")
    assert "LocalSystem" in script
    assert "start=" in script
    assert "auto" in script.lower()
    assert "TalvoraElevatedBroker" in script


def test_installer_self_elevates_once_and_persists_calling_user_sid():
    script = read("scripts/Install-BrokerService.ps1")

    assert "WindowsIdentity" in script
    assert "User.Value" in script
    assert "-Verb RunAs" in script
    assert "AllowedUserSid" in script
    assert "appsettings.json" in script


def test_installer_configures_service_recovery_and_verifies_installed_pipe():
    script = read("scripts/Install-BrokerService.ps1")
    verify = read("scripts/Test-InstalledBrokerService.ps1")

    assert "failure" in script.lower()
    assert "restart/" in script.lower()
    assert "Test-InstalledBrokerService.ps1" in script
    assert "/health/broker" in verify
    assert "IsElevated" in verify or "isElevated" in verify
    assert "Talvora Elevated Broker Windows Service: GREEN" in verify


def test_uninstaller_is_idempotent_and_removes_persistent_installation():
    script = read("scripts/Uninstall-BrokerService.ps1")

    assert "-Verb RunAs" in script
    assert "TalvoraElevatedBroker" in script
    assert "Stop-Service" in script or "sc.exe" in script
    assert "delete" in script.lower()
    assert "ProgramFiles" in script


def test_installer_does_not_use_powershell_using_namespace_inside_function_scope():
    script = read("scripts/Install-BrokerService.ps1")
    assert "using namespace" not in script


def test_broker_sets_content_root_to_app_base_directory_before_loading_configuration():
    program = read("src/Talvora.ElevatedBroker/Program.cs")

    create_builder = program.index("WebApplication.CreateBuilder")
    app_base = program.index("AppContext.BaseDirectory")
    config_read = program.index("builder.Configuration[")

    assert app_base < config_read
    assert "WebApplicationOptions" in program[:create_builder + 300]
    assert "ContentRootPath = AppContext.BaseDirectory" in program


def test_installed_broker_verifier_reports_last_broker_probe_error():
    verify = read("scripts/Test-InstalledBrokerService.ps1")

    assert "$lastBrokerError" in verify
    assert "candidate.error" in verify
    assert "BrokerError=$lastBrokerError" in verify


def test_installer_explains_when_uac_prompt_may_not_appear():
    script = read("scripts/Install-BrokerService.ps1")

    assert "already elevated" in script.lower() or "zaten yönetici" in script.lower()
    assert "UAC" in script
