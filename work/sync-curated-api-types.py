"""Replace embedded legacy DTO signatures with the same compiled metadata as the full index."""
from pathlib import Path
import json
root=Path(__file__).resolve().parent.parent
path=root/'docs/API.index.zh-CN.json'
manual=json.loads(path.read_text(encoding='utf-8-sig'))
full=json.loads((root/'docs/public-api.index.json').read_text(encoding='utf-8-sig'))
by_name={row['name']:row for row in full['types']}
for short,old in manual['types'].items():
 if not isinstance(old,dict):
  continue # Hand-written property lists are contracts, not metadata snapshots.
 name=old.get('name','EZapi.'+short)
 if name not in by_name:raise SystemExit('embedded type absent from current DLL: '+name)
 current=by_name[name]
 for field in ('name','baseType','interfaces','fields','properties','events','methods','documentation','sources'):
  old[field]=current[field]
 old['metadataSource']='public-api.index.json'
 old['metadataDllSHA256']=full['dllSha256']
manual['embeddedTypeMetadataPolicy']='Embedded types are regenerated from the compiled full public index; domain contract prose is retained. Full index is authoritative for all declarations.'
path.write_text(json.dumps(manual,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
print(json.dumps({'embeddedTypesSynchronized':len(manual['types']),'dllSha256':full['dllSha256']}))
