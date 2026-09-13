from pathlib import Path

script = Path(__file__).resolve().parents[2] / 'scripts' / 'Test-Architecture.ps1'
text = script.read_text(encoding='utf-8-sig')
needle = "[Parameter(Mandatory)][AllowEmptyCollection()][string[]]$Expected"
assert needle in text, 'Expected must explicitly allow an empty collection for zero-reference projects'
