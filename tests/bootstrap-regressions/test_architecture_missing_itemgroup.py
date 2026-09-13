from pathlib import Path

script = Path(__file__).resolve().parents[2] / 'scripts' / 'Test-Architecture.ps1'
text = script.read_text(encoding='utf-8-sig')

assert '.Project.ItemGroup.ProjectReference' not in text, (
    'Architecture test must not assume ItemGroup exists; zero-reference projects are valid.'
)
assert "SelectNodes('/Project/ItemGroup/ProjectReference')" in text, (
    'Architecture test must query zero-or-more ProjectReference nodes safely.'
)
