"""Check current/historical documentation boundaries without running the game."""
from pathlib import Path
import argparse
import hashlib
import json
import re
import struct
import urllib.parse

parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument('--root', type=Path, default=Path(__file__).resolve().parents[1])
args = parser.parse_args()
root = args.root.resolve(strict=True)
docs = root/'docs'
status = json.loads((docs/'release-status.json').read_text(encoding='utf-8-sig'))
assert status['nativeRuntimeVerified'] is False
assert status['license'] == 'MIT'
assert status['offlineVerification']['offlineSuitesPassed'] == 60
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

# Art references are a separate, optional developer asset set. Check the exact
# selected files and both PNG sizes without requiring imaging libraries in CI.
art = docs/'art-reference'
art_entry = json.loads((docs/'item-art.index.json').read_text(encoding='utf-8-sig'))
assets = json.loads((art/'art-reference.index.json').read_text(encoding='utf-8-sig'))
assert art_entry['originalArtworkCoveredByEzapiMit'] is False
assert art_entry['includedInDeveloperPackage'] and not art_entry['includedInGamePackage']
assert art_entry['pixelConvention']['newRuntimeSizeRestriction'] is False
assert len(assets['items']) == 96
assert len({row['spriteName'] for row in assets['items']}) == 96
selected = {'00-overview.png', '09-palette-reference.png'}
categories = set()
for row in assets['items']:
    categories.add(row['category'])
    for key, factor, hash_key in [('original', 1, 'sha256'), ('enlarged4x', 4, 'enlarged4xSHA256')]:
        rel = row[key]
        path = (art/rel).resolve(strict=True)
        assert path.is_relative_to(art.resolve()), rel
        data = path.read_bytes()
        assert data[:8] == b'\x89PNG\r\n\x1a\n', rel
        assert struct.unpack('>II', data[16:24]) == (row['widthPx']*factor, row['heightPx']*factor), rel
        assert hashlib.sha256(data).hexdigest().upper() == row[hash_key], rel
        selected.add(rel)
for category in categories:
    assert len([row for row in assets['items'] if row['category']==category]) == 12
    selected.add('boards/'+category+'.png')
assert len(categories) == 8
assert {p.relative_to(art).as_posix() for p in art.rglob('*.png')} == selected
for rel in selected:
    assert (art/rel).read_bytes().startswith(b'\x89PNG\r\n\x1a\n')
for name in ['AI-PROMPT.zh-CN.txt', 'AI-PROMPT.en.txt', 'NOTICE.md', 'README.md', 'verification.json']:
    assert (art/name).is_file()
assert 'excluded from EZapi' in (art/'NOTICE.md').read_text(encoding='utf-8')
assert 'art-reference' in (root/'THIRD-PARTY-NOTICES.md').read_text(encoding='utf-8')
assert 'item-art' in (docs/'AI-START-HERE.zh-CN.md').read_text(encoding='utf-8')
assert 'item-art' in (docs/'AI-START-HERE.en.md').read_text(encoding='utf-8')

# Tooltip recipes are existing-field examples, not a new runtime setting.
tooltip = json.loads((docs/'tooltip-style.index.json').read_text(encoding='utf-8-sig'))
recipes = {row['id']: row for row in tooltip['recipes']}
native = recipes['native']['jsonSettings']
assert native == {'descriptionPlacement': 'native', 'tooltip': {'showSource': False}}
assert recipes['extended']['jsonSettings']['tooltip']['showSource'] is True
item = json.loads((docs/'examples/tooltip-native/items.json').read_text(encoding='utf-8-sig'))[0]
assert item['descriptionPlacement'] == native['descriptionPlacement'] and item['tooltip'] == native['tooltip']
assert item['types'] and item['localizedFlavorText']['chinese'] and item['localizedFlavorText']['english']
schema = json.loads((root/'schemas/items.schema.json').read_text(encoding='utf-8-sig'))
assert 'tooltipStyle' not in schema['items']['properties']
assert schema['items']['properties']['tooltip']['properties']['showSource']['default'] is True
for lang in ['zh-CN', 'en']:
    assert 'tooltip-style' in (docs/f'AI-START-HERE.{lang}.md').read_text(encoding='utf-8-sig')
tool_dir = root/('distribution' if (root/'distribution/New-EZapiMod.ps1').is_file() else 'developer-tools')
for name in ['New-EZapiMod.ps1', 'Find-EZapiApi.ps1', 'Set-EZapiUiPreview.ps1']:
    assert (tool_dir/name).is_file(), name
    assert (tool_dir/name).read_bytes().startswith(b'\xef\xbb\xbf'), 'PowerShell 5.1 UTF-8 BOM required: '+name
# Native layout recipes and schema are shipped to AI consumers together. This
# checks their distribution contract, not the actual game font or rendering.
ui = json.loads((docs/'native-ui-preview.index.json').read_text(encoding='utf-8-sig'))
ui_schema = json.loads((root/'schemas/native-ui.schema.json').read_text(encoding='utf-8-sig'))
assert len(ui['templates']) == 3
assert ui['limits'] == {'bytes': 65536, 'nodes': 128, 'depth': 12, 'width': 1024, 'height': 768, 'inspectedTextNodes': 256}
assert ui_schema['$defs']['node']['properties']['vertical']['default'] == 'Center'
for rel in ui['templates']:
    recipe = json.loads((docs/rel).read_text(encoding='utf-8-sig'))
    assert recipe['schemaVersion'] == 1 and recipe['title']['chinese'] and recipe['title']['english']
    assert recipe['root']['kind'] == 'Column'
assert (docs/ui['example']).is_file()
for lang in ['zh-CN', 'en']:
    assert 'native-ui-preview' in (docs/f'AI-START-HERE.{lang}.md').read_text(encoding='utf-8-sig')
assert not errors, json.dumps(errors, ensure_ascii=False, indent=2)
print(json.dumps({'passed': True, 'releaseTag': tag, 'indexesChecked': indexes,
                  'markdownPagesChecked': len(pages), 'localLinksChecked': links,
                  'artReferenceSpritesChecked': len(assets['items']),
                  'currentNativeAcceptancePending': True}))
