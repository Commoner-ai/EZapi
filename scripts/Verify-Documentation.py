"""Check current/historical documentation boundaries without running the game."""
from pathlib import Path
import argparse
import hashlib
import json
import re
import urllib.parse

parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument('--root', type=Path, default=Path(__file__).resolve().parents[1])
args = parser.parse_args()
root = args.root.resolve(strict=True)
docs = root/'docs'
status = json.loads((docs/'release-status.json').read_text(encoding='utf-8-sig'))
assert status['nativeRuntimeVerified'] is False
assert status['license'] == 'MIT'
assert status['offlineVerification']['offlineSuitesPassed'] == 59
assert status['offlineVerification']['cliProcessChecksPassed'] == 25
tag, version = status['releaseTag'], status['assemblyVersion']
errors = []
flags = re.compile(r'(?:native.*verified|runtime.*verified|verified.*runtime)', re.I)

def inspect(value, file, path='', historical=False):
    if isinstance(value, list):
        for i, row in enumerate(value):
            inspect(row, file, path+'/'+str(i), historical)
    elif isinstance(value, dict):
        for key, row in value.items():
            old = historical or key.lower().startswith('historical')
            if not old and flags.search(key) and row is True:
                errors.append({'file': file, 'reason': 'unqualified native/runtime pass', 'path': path+'/'+key})
            inspect(row, file, path+'/'+key, old)

indexes = 0
for file in sorted(docs.glob('*.json')):
    d = json.loads(file.read_text(encoding='utf-8-sig'))
    if file.name == 'release-status.json':
        continue
    context = d.get('documentationContext', {})
    if context.get('releaseTag') != tag or context.get('assemblyVersion') != version:
        errors.append({'file': file.name, 'reason': 'stale documentation context'})
    for key in ('version', 'implementationVersion'):
        if re.fullmatch(r'0\.1\.\d+', str(d.get(key, ''))) and d[key] != version:
            errors.append({'file': file.name, 'reason': 'stale top-level '+key})
    inspect(d, file.name)
    indexes += 1

links = 0
pages = [root/'README.md', root/'README.zh-CN.md', *docs.rglob('*.md')]
for file in pages:
    content = file.read_text(encoding='utf-8-sig')
    for match in re.finditer(r'!?\[[^\]\n]*\]\(([^\s\)]+)(?:\s+"[^"]*")?\)', content):
        destination = match[1].strip('<>')
        uri = urllib.parse.urlsplit(destination)
        if uri.scheme or destination.startswith('#'):
            continue
        links += 1
        if not (file.parent/urllib.parse.unquote(uri.path)).exists():
            errors.append({'file': str(file.relative_to(root)), 'reason': 'missing local link', 'target': destination})
    if 'history' not in file.relative_to(root).parts and 'releases' not in file.relative_to(root).parts:
        for line in content.splitlines():
            if line.startswith('>'):
                continue
            if re.search(r'(?:当前\s*DLL\s*已通过\s*415|(?:The )?current DLL passed 415)', line):
                errors.append({'file': str(file.relative_to(root)), 'reason': 'historical 415 checks claimed as current'})

for name in ['README.md', 'README.zh-CN.md', 'AI-START-HERE.zh-CN.md',
             'AI-START-HERE.en.md', 'install-and-debug.zh-CN.md', 'install-and-debug.en.md']:
    file = root/name if name.startswith('README') else docs/name
    if tag not in file.read_text(encoding='utf-8-sig'):
        errors.append({'file': name, 'reason': 'current entry does not name current release'})
    if re.search(r'implementation.*remains private|框架源码保持私有|框架源码不公开', file.read_text(encoding='utf-8-sig'), re.I):
        errors.append({'file': name, 'reason': 'obsolete closed-source policy'})

full = json.loads((docs/'public-api.index.json').read_text(encoding='utf-8-sig'))
assert full['version'] == version
if full['dllSha256'] != status['runtimeDllSHA256']:
    # Source builds regenerate metadata for their local DLL without changing the
    # official release's identity or inheriting its evidence.
    rebuilt = root/'src/EZapi/bin/Release/net6.0/EZapi.dll'
    assert rebuilt.is_file() and hashlib.sha256(rebuilt.read_bytes()).hexdigest().upper() == full['dllSha256']
assert all(not re.search(r'-0\.1\.\d+\.', d.get('guide', '')) for d in full['domains'])
assert not errors, json.dumps(errors, ensure_ascii=False, indent=2)
print(json.dumps({'passed': True, 'releaseTag': tag, 'indexesChecked': indexes,
                  'markdownPagesChecked': len(pages), 'localLinksChecked': links,
                  'currentNativeAcceptancePending': True}))
