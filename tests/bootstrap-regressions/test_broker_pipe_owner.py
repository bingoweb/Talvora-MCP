from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]


def read(path: str) -> str:
    return (ROOT / path).read_text(encoding="utf-8-sig")


def test_broker_pins_named_pipe_owner_to_server_identity():
    source = read("src/Talvora.ElevatedBroker/BrokerPipeFactory.cs")

    assert "security.SetOwner(serverUser)" in source
    assert "Unexpected Elevated Broker pipe owner" not in source
