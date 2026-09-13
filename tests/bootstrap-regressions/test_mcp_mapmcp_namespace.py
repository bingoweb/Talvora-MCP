from pathlib import Path
import unittest

ROOT = Path(__file__).resolve().parents[2]

class McpMapMcpNamespaceRegression(unittest.TestCase):
    def test_mapmcp_extension_namespace_is_imported(self):
        source = (ROOT / 'src/Talvora.Adapter.Mcp/McpServiceCollectionExtensions.cs').read_text(encoding='utf-8-sig')
        self.assertIn('using Microsoft.AspNetCore.Builder;', source)
        self.assertIn('endpoints.MapMcp("/mcp");', source)

if __name__ == '__main__':
    unittest.main()
