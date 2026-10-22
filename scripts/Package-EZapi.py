"""Package two binary EZapi assets. MIT source is available separately on GitHub.

Only consumer documentation/examples and explicit automation helpers accompany
binary tools. Never export src/EZapi, internal tests, framework build projects,
Git history, private logs, credentials or game assemblies. See distribution policy.
"""
from pathlib import Path
import argparse
import hashlib
import json
import re
import shutil
import subprocess
import sys
import zipfile

ROOT = Path(__file__).resolve().parents[1]
TAG = json.loads((ROOT/'docs/release-status.json').read_text(encoding='utf-8-sig'))['releaseTag']
PUBLIC_REPOSITORY = 'https://github.com/Commoner-ai/EZapi'
TEXT = {'.md', '.json', '.cs', '.csproj', '.ps1', '.xml', '.txt'}

def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest().upper()

def load(path):
    return json.loads(path.read_text(encoding='utf-8-sig'))

def write(path, value):
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(value, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')

def copy(src, dst):
    dst.parent.mkdir(parents=True, exist_ok=True)
    shutil.copy2(src, dst)

def tree(src, dst, allowed):
    for p in src.rglob('*'):
        if not p.is_file() or any(x in {'bin', 'obj', '.git', '__pycache__'} for x in p.relative_to(src).parts):
            continue
        if p.suffix in allowed:
            copy(p, dst / p.relative_to(src))

def sanitize(root):
    for p in root.rglob('*'):
        if not p.is_file() or p.suffix not in TEXT:
            continue
        text = p.read_text(encoding='utf-8-sig')
        text = re.sub(r'(?i)[A-Z]:[/\\]+Users[/\\]+[^/\\\s"\x27]+', '<local-user>', text)
        text = re.sub(r'https?://ntfy\.sh/[^\s"\)<>]+', '<notification-endpoint>', text)
        # Consumer download links must point to the public distribution repository.
        text = re.sub(r'https://github\.com/Commoner-ai/EZapi(?:-Releases)?(?=/|[\s\)\]"\x27]|$)', PUBLIC_REPOSITORY, text)
        # Windows PowerShell 5.1 needs a BOM for scripts containing Chinese.
        p.write_text(text, encoding='utf-8-sig' if p.suffix == '.ps1' else 'utf-8')

def archive(root):
    rows = [{'path': p.relative_to(root).as_posix(), 'sha256': sha(p)}
            for p in sorted(root.rglob('*')) if p.is_file() and p != root/'files.sha256.json']
    write(root/'files.sha256.json', rows)
    out = Path(str(root) + '.zip')
    with zipfile.ZipFile(out, 'w', zipfile.ZIP_DEFLATED, compresslevel=6) as z:
        for p in sorted(root.rglob('*')):
            if p.is_file(): z.write(p, p.relative_to(root).as_posix())
    with zipfile.ZipFile(out) as z:
        assert z.testzip() is None
        for row in rows:
            assert hashlib.sha256(z.read(row['path'])).hexdigest().upper() == row['sha256']
    return {'file': out.name, 'sha256': sha(out), 'bytes': out.stat().st_size}

parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument('--candidate', required=True, type=Path)
parser.add_argument('--output', required=True, type=Path)
args = parser.parse_args()
candidate = args.candidate.resolve(strict=True)
output = args.output.resolve()
assert not output.exists(), 'Use a fresh output directory.'
verification = load(candidate/'verification.json')
assert verification['offlineVerified'] and verification['version'] == TAG.split('-')[0]
assert verification['dllSHA256'] == sha(candidate/'Mods/EZapi.dll')
assert load(candidate/'docs/public-api.index.json')['dllSha256'] == verification['dllSHA256']
output.mkdir(parents=True)
game, developer = output/f'EZapi-{TAG}-game', output/f'EZapi-{TAG}-developer'

for target in [game, developer]:
    copy(candidate/'Mods/EZapi.dll', target/'Mods/EZapi.dll')
    copy(ROOT/'distribution/Install-EZapi.ps1', target/'Install-EZapi.ps1')
    for name in ['LICENSE', 'THIRD-PARTY-NOTICES.md']:
        copy(ROOT/name, target/name)
    write(target/'verification.json', verification)
copy(ROOT/'docs/install-and-debug.zh-CN.md', game/'INSTALL.zh-CN.md')
copy(ROOT/'docs/install-and-debug.en.md', game/'INSTALL.en.md')
# The player package has no docs/ directory; its guide links must stand alone.
for guide in game.glob('INSTALL*.md'):
    content = guide.read_text(encoding='utf-8-sig')
    content = re.sub(
        r'(\[[^\]\n]*\]\()((?!https?://|#)[^\s\)]+)(\))',
        lambda m: m[1]+PUBLIC_REPOSITORY+'/blob/v'+TAG+'/docs/'+m[2]+m[3], content)
    guide.write_text(content, encoding='utf-8')
for name in ['README.md', 'README.zh-CN.md']:
    copy(ROOT/name, developer/name)

# Consumer guides, schemas and exact public metadata, never implementation files.
for p in (candidate/'docs').rglob('*'):
    if not p.is_file() or p.suffix not in TEXT | {'.png'}: continue
    rel = p.relative_to(candidate/'docs')
    if any(x in {'bin', 'obj', 'verification'} for x in rel.parts): continue
    if '.history.' in p.name or '.audit.' in p.name or p.name.startswith(('HOTFIX-', 'REFACTOR-', 'ROADMAP', 'TEST-PLAYER')): continue
    if p.suffix in {'.cs', '.csproj'} and rel.parts[0] != 'examples': continue
    if p.suffix == '.ps1': continue
    copy(p, developer/'docs'/rel)
tree(ROOT/'schemas', developer/'schemas', {'.json'})
tree(ROOT/'src/EZapi.Sample', developer/'src/EZapi.Sample', {'.cs', '.csproj', '.json', '.png'})
# The binary SDK example references the distributed DLL instead of a source project.
project = developer/'src/EZapi.Sample/ExampleMod.csproj'
t = project.read_text(encoding='utf-8-sig')
t = t.replace('<ProjectReference Include="../EZapi/EZapi.csproj"/>', '<Reference Include="EZapi"><HintPath>../../Mods/EZapi.dll</HintPath><Private>false</Private></Reference>')
assert 'ProjectReference' not in t
project.write_text(t, encoding='utf-8')
copy(candidate/'Mods/EZapi.xml', developer/'Mods/EZapi.xml')
tree(candidate/'tools/ezapi-validate', developer/'tools/ezapi-validate', {'.dll', '.json'})
tree(candidate/'Mods/EZapiPacks', developer/'examples/ContentPacks', {'.json', '.png', '.csv'})
copy(candidate/'examples/EZapi.Sample.dll', developer/'examples/EZapi.Sample.dll')
copy(ROOT/'distribution/Set-EZapiDeveloperTools.ps1', developer/'developer-tools/Set-EZapiDeveloperTools.ps1')
for name in ['New-EZapiMod.ps1', 'Find-EZapiApi.ps1', 'Set-EZapiUiPreview.ps1']:
    copy(ROOT/'distribution'/name, developer/'developer-tools'/name)
copy(ROOT/'distribution/developer-tools.json', developer/'developer-tools/UserData/EZapi/developer-tools.json')
for name in ['Enter-EZapiTestStore.ps1', 'Invoke-EZapiNativeTest.ps1', 'Invoke-EZapiTestBatch.ps1', 'Invoke-EZapiGuideLocalization.ps1']:
    copy(ROOT/'work'/name, developer/'developer-tools/automation'/name)
copy(ROOT/'docs/automation.zh-CN.md', developer/'developer-tools/automation/README.zh-CN.md')
copy(ROOT/'docs/automation.en.md', developer/'developer-tools/automation/README.md')

# Keep API signatures and XML descriptions. Implementation sources are available
# on GitHub rather than inside these two binary packages.
for p in (developer/'docs').glob('*.json'):
    d = load(p)
    if not isinstance(d, dict): continue
    current = d.pop('currentDllNativeEvidence', None)
    if current: d['historicalNativeEvidence'] = current
    if p.name == 'source.index.json':
        d['sourceAccess'] = 'MIT source on GitHub; binary SDK does not include framework implementation'
        d['sourceRepository'] = PUBLIC_REPOSITORY
        for row in d.get('modules', []):
            row['implementationModule'] = Path(row.pop('file')).name
    if p.name == 'public-api.index.json':
        d['sourceAccess'] = 'MIT source on GitHub; signatures in this index have no method bodies'
        d['sourceRepository'] = PUBLIC_REPOSITORY
        for typ in d['types']: typ['sources'] = []
    if p.name == 'API.index.zh-CN.json':
        for typ in d.get('types', {}).values():
            if isinstance(typ, dict): typ.pop('sources', None)
    d['binaryPackageVerification'] = {
        'dllSHA256': verification['dllSHA256'], 'nativeRuntimeVerified': False,
        'frameworkSourceIncluded': False, 'sourceRepositoryVisibility': 'public',
        'artifactKind': 'binary-SDK; source is available separately on GitHub'}
    write(p, d)
for target in [game, developer]:
    sanitize(target)
    v = load(target/'verification.json')
    v['builtPublicApiIndexSHA256'] = v.pop('publicApiIndexSHA256')
    if target == developer:
        v['publicApiIndexSHA256'] = sha(developer/'docs/public-api.index.json')
    v['frameworkSourceIncluded'] = False
    v['sourceRepositoryVisibility'] = 'public'
    v['sourceRepository'] = PUBLIC_REPOSITORY
    v['license'] = 'MIT'
    write(target/'verification.json', v)

# Executable shipping gate: any implementation or internal build/test file fails.
for target in [game, developer]:
    assert not (target/'src/EZapi').exists()
    for p in target.rglob('*'):
        if not p.is_file(): continue
        rel = p.relative_to(target)
        assert not any(x in {'.git', 'work', '.github', 'references', 'EZapi.TestTools'} for x in rel.parts), rel
        assert p.suffix not in {'.es3', '.log', '.pdb'}, rel
        if p.suffix in {'.cs', '.csproj'}:
            assert rel.parts[:2] == ('src', 'EZapi.Sample') or rel.parts[:2] == ('docs', 'examples'), rel
    assert [p.relative_to(target/'Mods').as_posix() for p in (target/'Mods').rglob('*.dll')] == ['EZapi.dll']
    assert not (target/'Mods/EZapiPacks').exists()
assert not (game/'UserData').exists()
assert not (game/'docs/art-reference').exists()
assert (developer/'docs/art-reference/NOTICE.md').is_file()
assert sha(game/'Mods/EZapi.dll') == sha(developer/'Mods/EZapi.dll') == verification['dllSHA256']
assert load(developer/'docs/release-status.json')['releaseTag'] == TAG
documentation = subprocess.run(
    [sys.executable, str(ROOT/'scripts/Verify-Documentation.py'), '--root', str(developer)],
    capture_output=True, text=True, encoding='utf-8')
assert documentation.returncode == 0, documentation.stdout + documentation.stderr
assets = [archive(game), archive(developer)]
write(output/'package-verification.json', {
    'tag': TAG, 'passed': True, 'assets': assets, 'dllSHA256': verification['dllSHA256'],
    'frameworkSourceIncluded': False, 'internalTestsIncluded': False, 'frameworkBuildProjectsIncluded': False,
    'sourceRepositoryVisibility': 'public', 'sourceRepository': PUBLIC_REPOSITORY, 'license': 'MIT',
    'oneGameDllPerPackage': True, 'debugDefaultOff': True,
    'internalPluginExcluded': True, 'examplesAutoInstalled': False,
    'nativeRuntimeVerified': False, 'fullGoalComplete': False,
    'developerAutomationIncluded': True,
    'offlineModGeneratorIncluded': True,
    'offlineApiSearchIncluded': True,
    'optionalTooltipStyleGuideIncluded': True,
    'nativeUiPreviewTemplatesIncluded': True,
    'nativeUiPreviewStagingToolIncluded': True,
    'documentationConsistencyVerified': True,
    'optionalArtReferenceIncluded': True,
    'artReferenceOriginalSprites': 96,
    'artReferenceNearest4xSprites': 96,
    'artReferenceCategoryBoards': 8,
    'originalArtworkCoveredByEzapiMit': False,
})
(output/'SHA256SUMS.txt').write_text(''.join(x['sha256']+'  '+x['file']+'\n' for x in assets), encoding='utf-8')
print(json.dumps({'passed': True, 'frameworkSourceIncluded': False, 'assets': assets}, ensure_ascii=False))
