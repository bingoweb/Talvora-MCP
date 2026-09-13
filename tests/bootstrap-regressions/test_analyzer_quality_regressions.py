from pathlib import Path
import re
import unittest

ROOT = Path(__file__).resolve().parents[2]

class AnalyzerQualityRegressions(unittest.TestCase):
    def test_generic_result_uses_non_generic_factory(self):
        source = (ROOT / 'src/Talvora.Abstractions/TalvoraResult.cs').read_text(encoding='utf-8')
        self.assertNotRegex(source, r'public\s+static\s+TalvoraResult<T>\s+(Success|Failure)\s*\(')
        self.assertRegex(source, r'public\s+static\s+class\s+TalvoraResult\b')


    def test_generic_tool_envelope_uses_non_generic_factory(self):
        source = (ROOT / 'src/Talvora.Adapter.Mcp/ToolEnvelope.cs').read_text(encoding='utf-8')
        self.assertNotRegex(source, r'public\s+static\s+ToolEnvelope<T>\s+From\s*\(')
        self.assertRegex(source, r'public\s+static\s+class\s+ToolEnvelope\b')

    def test_elevated_broker_avoids_runtime_formatted_logging_extensions(self):
        sources = '\n'.join(
            path.read_text(encoding='utf-8')
            for path in (ROOT / 'src/Talvora.ElevatedBroker').rglob('*.cs')
        )
        self.assertNotIn('.LogInformation(', sources)
        self.assertNotIn('.LogWarning(', sources)
        self.assertNotIn('.LogError(', sources)

    def test_csharp_test_method_names_do_not_use_underscores(self):
        violations = []
        pattern = re.compile(r'public\s+(?:async\s+)?(?:Task|void)\s+([A-Za-z0-9]+_[A-Za-z0-9_]*)\s*\(')
        for path in (ROOT / 'tests').rglob('*.cs'):
            for match in pattern.finditer(path.read_text(encoding='utf-8')):
                violations.append(f'{path.relative_to(ROOT)}:{match.group(1)}')
        self.assertEqual([], violations)

    def test_shell_tests_reuse_expected_argument_arrays(self):
        source = (ROOT / 'tests/Talvora.Shell.Tests/ShellLaunchSpecFactoryTests.cs').read_text(encoding='utf-8')
        self.assertNotIn('new[] {', source)
        self.assertIn('static readonly string[]', source)

if __name__ == '__main__':
    unittest.main()
