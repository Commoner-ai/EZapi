"""Check an explicitly exported source checkout without printing matched secrets."""
from pathlib import Path
import argparse
import hashlib
import json
import re
import subprocess

parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument('--root', type=Path, default=Path(__file__).resolve().parents[1])
parser.add_argument('--write-manifest', action='store_true')
args = parser.parse_args()
root = args.root.resolve(strict=True)
names = subprocess.check_output(
    ['git', 'ls-files', '--cached', '--others', '--exclude-standard', '-z'],
    cwd=root).decode('utf-8').split('\0')
names = sorted(set(filter(None, names)))
root_files = {'AGENTS.md', 'README.md', 'README.zh-CN.md', 'LICENSE',
              'THIRD-PARTY-NOTICES.md', '.gitignore', 'Build-EZapi.ps1',
              'distribution-verification.json', 'source-distribution.json'}
work_files = {'build-public-api-index.py', 'sync-curated-api-types.py',
              'verify-public-api-index.py', 'verify-capability-index.py',
              'verify-validator-cli.py', 'Enter-EZapiTestStore.ps1',
              'Invoke-EZapiNativeTest.ps1', 'Invoke-EZapiTestBatch.ps1',
              'Invoke-EZapiGuideLocalization.ps1'}
script_files = {'Package-EZapi.py', 'Fetch-OfficialDocs.ps1',
                'Test-EZapiOffline.ps1', 'Check-SourceDistribution.py', 'Verify-Documentation.py'}
patterns = {
    'private key': r'-----BEGIN (?:OPENSSH |RSA |EC |DSA )?PRIVATE KEY-----',
    'access token': r'\b(?:gh[pousr]_[A-Za-z0-9]{20,}|github_pat_[A-Za-z0-9_]{20,}|sk-[A-Za-z0-9_-]{20,})',
    'credential assignment': r'''(?i)\b(?:password|passwd|api[_-]?key|access[_-]?token|secret)\s*[:=]\s*["'][^"']{6,}''',
    'private notification endpoint': r'ntfy\.sh/[^\s"\)<>]+',
    'private user directory': r'(?i)C:[/\\]+Users[/\\]+Administrator',
}
errors = []
rows = []
for name in names:
    path = Path(name)
    parts = path.parts
    allowed = (len(parts) == 1 and name in root_files)
    if len(parts) >= 2 and parts[0] in {'docs', 'schemas', 'developer-tools', 'distribution'}:
        allowed = path.suffix in {'.md', '.json', '.png', '.cs', '.csproj', '.xml', '.ps1', '.csv', '.txt'}
    if len(parts) >= 3 and parts[:2] == ('examples', 'ContentPacks'):
        allowed = path.suffix in {'.json', '.png', '.csv'}
    if len(parts) >= 3 and parts[:2] in {('src', 'EZapi'), ('src', 'EZapi.Sample'), ('src', 'EZapi.ContentValidator')}:
        allowed = path.suffix in {'.cs', '.csproj', '.json', '.png', '.xml', '.csv'}
    if len(parts) == 2 and parts[0] == 'scripts':
        allowed = parts[1] in script_files
    if name == '.github/workflows/offline.yml':
        allowed = True
    if len(parts) == 3 and parts[0] == 'work':
        allowed = (re.fullmatch(r'ezapi-.+-tests', parts[1]) is not None
                   and path.suffix in {'.cs', '.csproj'})
        allowed |= parts[1] == 'ezapi-inspect' and parts[2] in {'Inspect.csproj', 'Program.cs'}
    if len(parts) == 2 and parts[0] == 'work':
        allowed = parts[1] in work_files
    if not allowed or any(part in {'bin', 'obj', '.git', 'references', 'outputs', 'UserData'} for part in parts):
        errors.append({'path': name, 'reason': 'outside source allowlist'})
        continue
    file = root / path
    if file.is_symlink():
        errors.append({'path': name, 'reason': 'symlink forbidden'})
        continue
    if path.suffix != '.png':
        content = file.read_text(encoding='utf-8-sig')
        for label, pattern in patterns.items():
            if re.search(pattern, content):
                errors.append({'path': name, 'reason': label})
    if name != 'source-distribution.json':
        rows.append({'path': name, 'sha256': hashlib.sha256(file.read_bytes()).hexdigest().upper()})
assert not errors, json.dumps(errors, ensure_ascii=False)
assert (root/'src/EZapi/EZapi.csproj').is_file(), 'framework source missing'
assert len(list((root/'work').glob('ezapi-*-tests/*.csproj'))) == 59, 'offline suite missing'
if args.write_manifest:
    (root/'source-distribution.json').write_text(json.dumps({
        'schemaVersion': 1, 'license': 'MIT', 'frameworkSourceIncluded': True,
        'oldPrivateHistoryImported': False, 'gameAssembliesIncluded': False,
        'verificationScope': 'source file allowlist, hashes and sensitive text patterns; not native gameplay',
        'files': rows}, ensure_ascii=False, indent=2)+'\n', encoding='utf-8')
print(json.dumps({'passed': True, 'filesChecked': len(rows), 'license': 'MIT'}))
