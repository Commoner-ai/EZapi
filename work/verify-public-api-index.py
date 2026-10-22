"""Compare the generated index against the established independent metadata mode."""
from pathlib import Path
import hashlib,json,subprocess
root=Path(__file__).resolve().parent.parent
dll=root/'src/EZapi/bin/Release/net6.0/EZapi.dll'
index=json.loads((root/'docs/public-api.index.json').read_text(encoding='utf-8-sig'))
inspector=root/'work/ezapi-inspect/bin/Release/net8.0/Inspect.dll'
if not inspector.is_file():inspector=root/'verification/inspector/Inspect.dll'
old=subprocess.run(['dotnet',str(inspector),str(dll),'--public-surface'],capture_output=True,text=True,encoding='utf-8',check=True,timeout=60)
surface=json.loads(old.stdout)
assert index['dllSha256']==hashlib.sha256(dll.read_bytes()).hexdigest().upper()
assert index['version']+'.0'==surface['version']==index['assemblyVersion']
types={t['name']:t for t in index['types']};original={t['name']:t for t in surface['types']}
assert set(types)==set(original),(set(types)-set(original),set(original)-set(types))
member_keys=set();counts={k:0 for k in ['types','fields','properties','events','methods']};counts['types']=len(types)
for name,typ in types.items():
    raw=original[name]
    assert {m['signature'] for m in typ['methods']}=={m['signature'] for m in raw['methods']},name
    assert {(m['name'],m['type']) for m in typ['properties']}=={(m['name'],m['type']) for m in raw['properties']},name
    assert {(m['name'],m['type']) for m in typ['fields']}=={(m['name'],m['type']) for m in raw['fields']},name
    for category in ['fields','properties','events','methods']:
        counts[category]+=len(typ[category])
        for member in typ[category]:
            assert member['id'] not in member_keys,member['id']
            member_keys.add(member['id'])
    assert typ['sources'],name
    for source in typ['sources']:
        path=root/'docs'/source['path'];lines=path.read_text(encoding='utf-8-sig').splitlines()
        assert name.split('.')[-1].split('`')[0] in lines[source['line']-1],(name,source)
    if typ['baseType']=='System.Enum':assert all(f['hasConstant'] or f['name']=='value__' for f in typ['fields']),name
for key,value in counts.items():assert index['counts'][key]==value,(key,value)
assert index['counts']['members']==len(member_keys)
domains={row['id']:row for row in index['domains']}
for typ in types.values():
    route=domains[typ['domain']]
    assert (root/'docs'/route['guide']).is_file(),route
    if route['example']:assert (root/'docs'/route['example']).is_file(),route
manual=json.loads((root/'docs/API.index.zh-CN.json').read_text(encoding='utf-8-sig'))
assert len({row['id'] for row in manual['entries']})==len(manual['entries'])
for row in manual['entries']:
    if row.get('memberKey'):assert row['memberKey'] in member_keys,row['memberKey']
for short,embedded in manual.get('types',{}).items():
    if not isinstance(embedded,dict):continue
    current=types[embedded['name']]
    for category in ['fields','properties','events','methods']:
        assert embedded[category]==current[category],('stale curated type metadata',short,category)
    assert embedded['metadataDllSHA256']==index['dllSha256'],short
language=types['EZapi.GameLanguage'];resolvers=[m for m in language['methods'] if m['name']=='Resolve']
assert len(resolvers)==3 and len({m['id'] for m in resolvers})==3
assert any([p['type'] for p in m['parameters']][:2]==['EZapi.LocalizedItemText','EZapi.LocalizationCatalog'] for m in resolvers)
assert any(any(p['isParams'] for p in m['parameters']) for m in resolvers)
assert types['EZapi.GameRunIdentity']['properties'][0]['initOnly']
assert types['EZapi.LocalizedItemText']['documentation'] is not None
assert any(m['compilerGenerated'] for m in types['EZapi.LocalizedItemText']['methods'])
report={'passed':True,'version':index['version'],'counts':index['counts'],'dllSha256':index['dllSha256'],
 'curatedEntries':len(manual['entries']),'curatedEntriesWithExactMemberKey':sum('memberKey' in row for row in manual['entries']),
 'scope':'Complete declaration comparison against existing public-surface inspector mode, source line references, unique overload IDs, task routes, constructor/property flags and curated backlinks. Metadata coverage is not gameplay evidence.'}
(root/'work/public-api-index-verification.json').write_text(json.dumps(report,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
print(json.dumps(report,ensure_ascii=False))
