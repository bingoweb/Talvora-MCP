from pathlib import Path
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[2]


def read(relative: str) -> str:
    return (ROOT / relative).read_text(encoding='utf-8-sig')


def test_elevated_broker_uses_versioned_grpc_over_named_pipes():
    proto = ROOT / 'src/Talvora.Ipc.Contracts/Protos/broker.proto'
    assert proto.exists(), 'versioned gRPC contract must exist'
    proto_text = proto.read_text(encoding='utf-8')
    assert 'service BrokerControl' in proto_text
    assert 'rpc Health' in proto_text

    packages = read('Directory.Packages.props')
    for package in ('Grpc.AspNetCore', 'Grpc.Net.Client', 'Grpc.Tools', 'Grpc.Core.Api', 'Google.Protobuf'):
        assert f'Include="{package}"' in packages

    program = read('src/Talvora.ElevatedBroker/Program.cs')
    assert 'ListenNamedPipe' in program
    assert 'HttpProtocols.Http2' in program
    assert 'CurrentUserOnly = false' in program
    assert 'MapGrpcService<BrokerControlService>()' in program

    pipe_factory = read('src/Talvora.ElevatedBroker/BrokerPipeFactory.cs')
    assert 'PipeAccessRights.FullControl' in pipe_factory
    assert 'PipeAccessRights.ReadPermissions' in pipe_factory


def test_host_has_isolated_named_pipe_grpc_client_adapter():
    client_project = ROOT / 'src/Talvora.Ipc.Client/Talvora.Ipc.Client.csproj'
    assert client_project.exists(), 'named-pipe gRPC client must be isolated from Host'

    client_factory = read('src/Talvora.Ipc.Client/NamedPipeConnectionFactory.cs')
    assert 'TokenImpersonationLevel.None' in client_factory
    assert 'NamedPipeClientStream' in client_factory

    client = read('src/Talvora.Ipc.Client/BrokerClient.cs')
    assert 'GrpcChannel.ForAddress' in client
    assert 'BrokerProtocol.CurrentVersion' in client

    host = read('src/Talvora.Host/Program.cs')
    assert 'AddTalvoraBrokerClient' in host
    assert 'MapGet("/health/broker"' in host
    assert 'using Grpc.' not in host


def test_verify_pipeline_includes_real_broker_ipc_smoke_test():
    smoke = ROOT / 'scripts/Test-BrokerSmoke.ps1'
    assert smoke.exists(), 'broker smoke test script must exist'
    smoke_text = smoke.read_text(encoding='utf-8-sig')
    assert 'Talvora.ElevatedBroker' in smoke_text
    assert '/health/broker' in smoke_text

    verify = read('scripts/verify.ps1')
    assert 'Test-BrokerSmoke.ps1' in verify
