from pathlib import Path

ROOT = Path('/mnt/data/Talvora')


def test_broker_client_uses_throw_helper_for_nonpositive_timeout():
    text = (ROOT / 'src/Talvora.Ipc.Client/BrokerClient.cs').read_text(encoding='utf-8-sig')
    assert 'ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(timeout, TimeSpan.Zero' in text
    assert 'throw new ArgumentOutOfRangeException(nameof(timeout))' not in text


def test_broker_pipe_factory_uses_actual_dotnet10_named_pipe_endpoint_property():
    text = (ROOT / 'src/Talvora.ElevatedBroker/BrokerPipeFactory.cs').read_text(encoding='utf-8-sig')
    assert 'context.NamedPipeEndPoint.PipeName' in text
    assert 'context.NamedPipeEndpoint.PipeName' not in text


def test_broker_program_imports_windows_service_extension_namespace():
    text = (ROOT / 'src/Talvora.ElevatedBroker/Program.cs').read_text(encoding='utf-8-sig')
    assert 'using Microsoft.Extensions.Hosting;' in text
