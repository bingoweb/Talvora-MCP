from pathlib import Path
import unittest

ROOT = Path(__file__).resolve().parents[2]


class MsTestConstantAssertionRegressions(unittest.TestCase):
    def test_broker_protocol_test_uses_runtime_grpc_descriptor(self):
        source = (ROOT / 'tests/Talvora.Ipc.Tests/BrokerProtocolTests.cs').read_text(encoding='utf-8-sig')

        self.assertNotIn('Assert.AreEqual(1, BrokerProtocol.CurrentVersion);', source)
        self.assertNotIn('Assert.AreEqual("Talvora.ElevatedBroker.v1", BrokerProtocol.DefaultPipeName);', source)
        self.assertIn('BrokerControl.Descriptor', source)


if __name__ == '__main__':
    unittest.main()
