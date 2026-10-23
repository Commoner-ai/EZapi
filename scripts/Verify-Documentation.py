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
assert status['offlineVerification']['offlineSuitesPassed'] == 67
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
            # Current guide introductions must not retain an older candidate after a bump.
            # Historical release notes and explicit block quotes are excluded above.
            for candidate in re.finditer(r'(?:Local\s+(?:unpublished\s+)?candidate|本地(?:未发布)?候选)\s*[:：]?\s*\*{0,2}(0\.1\.\d+(?:-preview\.\d+)?)', line, re.I):
                if candidate[1] not in {version, tag}:
                    errors.append({'file': str(file.relative_to(root)), 'reason': 'stale candidate in current guide', 'found': candidate[1]})

for name in ['README.md', 'README.zh-CN.md', 'AI-START-HERE.zh-CN.md',
             'AI-START-HERE.en.md', 'install-and-debug.zh-CN.md', 'install-and-debug.en.md']:
    file = root/name if name.startswith('README') else docs/name
    if tag not in file.read_text(encoding='utf-8-sig'):
        errors.append({'file': name, 'reason': 'current entry does not name current release'})
    entry_text = file.read_text(encoding='utf-8-sig')
    for match in re.finditer(r'(?:assembly(?:\s+version)?|程序集(?:版本)?(?:为)?)\s*[:：]?\s*\*{0,2}(0\.1\.\d+)(?!\d|\.\d)', entry_text, re.I):
        if match[1] != version:
            errors.append({'file': name, 'reason': 'stale assembly version in current entry', 'found': match[1]})
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

# Origin guides and exact signatures travel together. This verifies the SDK
# contract, not native heat, detection or save behavior.
origin = json.loads((docs/'item-origin.index.json').read_text(encoding='utf-8-sig'))
origin_type = next(t for t in full['types'] if t['name'] == 'EZapi.ItemOriginApi')
assert {m['memberKey'] for m in origin['methods']} == {m['id'] for m in origin_type['methods']}
fixed = next(m for m in origin_type['methods'] if m['name'] == 'StageStolen')
assert fixed['parameters'][1]['name'] == 'heatPercent' and fixed['parameters'][1]['defaultValue'] == 100
assert origin['offlineVerification']['failed'] == 0 and origin['nativeAcceptance'].startswith('Pending')
assert (docs/origin['example']).is_file() and (docs/origin['project']).is_file()
for lang in ['zh-CN', 'en']:
    assert 'item-origin' in (docs/f'AI-START-HERE.{lang}.md').read_text(encoding='utf-8-sig')

artwork = json.loads((docs/'artwork-inspection.index.json').read_text(encoding='utf-8-sig'))
art_types = [t for t in full['types'] if t['name'] in {'EZapi.ArtworkPixels', 'EZapi.SpriteArtworkApi'}]
art_methods = {m['id'] for t in art_types for m in t['methods']}
assert {m['memberKey'] for m in artwork['methods']} == art_methods
assert artwork['limits'] == {'pixels': 1048576, 'vertices': 1024, 'indices': 6144, 'rasterWork': 8388608}
assert artwork['nativeAcceptance'].startswith('Pending')
assert (docs/artwork['example']).is_file() and (docs/artwork['pureExample']).is_file()
for lang in ['zh-CN', 'en']:
    assert 'artwork-inspection' in (docs/f'AI-START-HERE.{lang}.md').read_text(encoding='utf-8-sig')

receipt = json.loads((docs/'delivery-receipts.index.json').read_text(encoding='utf-8-sig'))
receipt_types = [t for t in full['types'] if t['name'] in {'EZapi.ItemReceiptApi', 'EZapi.DeliveryReceiptAudit'}]
assert {m['memberKey'] for m in receipt['methods']} == {m['id'] for t in receipt_types for m in t['methods']}
assert receipt['limits']['items'] == 4096 and receipt['limits']['jsonCharactersPerPass'] == 1048576
assert receipt['nativeAcceptance'].startswith('Pending')
assert (docs/receipt['example']).is_file() and (docs/receipt['project']).is_file()
for lang in ['zh-CN', 'en']:
    assert 'delivery-receipts' in (docs/f'AI-START-HERE.{lang}.md').read_text(encoding='utf-8-sig')

prepared = json.loads((docs/'prepared-items.index.json').read_text(encoding='utf-8-sig'))
prepared_type = next(t for t in full['types'] if t['name'] == 'EZapi.PreparedItemApi')
assert {m['memberKey'] for m in prepared['methods']} == {m['id'] for m in prepared_type['methods']}
assert len(prepared['recipes']) == 12 and len({r['kind'] for r in prepared['recipes']}) == 12
assert next(r for r in prepared['recipes'] if r['kind'] == 'EfficiencyModule')['itemId'] == 'system_module_efficiancy'
assert next(m for m in prepared_type['methods'] if m['name'] == 'Create')['parameters'][1]['defaultValue'] is True
assert prepared['nativeAcceptance'].startswith('Pending')
assert (docs/prepared['example']).is_file() and (docs/prepared['project']).is_file()
for lang in ['zh-CN', 'en']:
    assert 'prepared-items' in (docs/f'AI-START-HERE.{lang}.md').read_text(encoding='utf-8-sig')

graphs = json.loads((docs/'delivery-graphs.index.json').read_text(encoding='utf-8-sig'))
graph_types = [t for t in full['types'] if t['name'] in {'EZapi.DeliveryGraphAudit', 'EZapi.ItemGraphReceiptApi'}]
assert {m['memberKey'] for m in graphs['methods']} == {m['id'] for t in graph_types for m in t['methods']}
assert len(graphs['methods']) == 5
assert graphs['limits'] == {'nodes': 8192, 'items': 4096, 'combinedGraphReferences': 16384, 'jsonCharactersPerPass': 1048576}
handoff = next(m for t in graph_types for m in t['methods'] if m['name'] == 'CheckHandoff')
assert handoff['parameters'][3]['hasDefault'] and handoff['parameters'][3]['defaultValue'] is None
assert handoff['parameters'][4]['name'] == 'allowChildOwnershipInitialization' and handoff['parameters'][4]['defaultValue'] is False
assert graphs['nativeAcceptance'].startswith('Pending')
assert (docs/graphs['example']).is_file() and (docs/graphs['project']).is_file()
for lang in ['zh-CN', 'en']:
    assert 'delivery-graphs' in (docs/f'AI-START-HERE.{lang}.md').read_text(encoding='utf-8-sig')
assert not errors, json.dumps(errors, ensure_ascii=False, indent=2)
print(json.dumps({'passed': True, 'releaseTag': tag, 'indexesChecked': indexes,
                  'markdownPagesChecked': len(pages), 'localLinksChecked': links,
                  'artReferenceSpritesChecked': len(assets['items']),
                  'currentNativeAcceptancePending': True}))
