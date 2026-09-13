from pathlib import Path
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[2]


def parse(relative: str):
    return ET.parse(ROOT / relative).getroot()


def package_refs(relative: str):
    root = parse(relative)
    return {node.attrib['Include'] for node in root.findall('.//PackageReference')}


def package_versions():
    root = parse('Directory.Packages.props')
    return {node.attrib['Include'] for node in root.findall('.//PackageVersion')}


def target_framework(relative: str) -> str:
    root = parse(relative)
    node = root.find('.//TargetFramework')
    assert node is not None and node.text
    return node.text.strip()


def test_net10_does_not_explicitly_reference_framework_bundled_pipe_acl_package():
    assert 'System.IO.Pipes.AccessControl' not in package_refs('src/Talvora.Ipc.Client/Talvora.Ipc.Client.csproj')
    assert 'System.IO.Pipes.AccessControl' not in package_refs('src/Talvora.ElevatedBroker/Talvora.ElevatedBroker.csproj')
    assert 'System.IO.Pipes.AccessControl' not in package_versions()


def test_architecture_tests_target_windows_when_loading_windows_ipc_client():
    assert target_framework('tests/Talvora.Architecture.Tests/Talvora.Architecture.Tests.csproj') == 'net10.0-windows'
