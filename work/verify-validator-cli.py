"""Exercise the built CLI as a separate process, including argument and input failures."""
from pathlib import Path
import argparse, hashlib, json, subprocess, tempfile

root=Path(__file__).resolve().parent.parent
parser=argparse.ArgumentParser()
parser.add_argument('--validator',type=Path,default=root/'src/EZapi.ContentValidator/bin/Release/net8.0/ezapi-validate.dll')
parser.add_argument('--report',type=Path,default=root/'work/validator-cli-tests.json')
args=parser.parse_args()
validator=args.validator.resolve(strict=True)
checks=[]

def invoke(arguments,code,kind=None):
    run=subprocess.run(['dotnet',str(validator),*map(str,arguments)],capture_output=True,text=True,encoding='utf-8',timeout=20)
    assert run.returncode==code,(arguments,run.returncode,run.stdout,run.stderr)
    if arguments==['--help']:
        assert '--requirements requirements.json' in run.stdout
        return None
    result=json.loads(run.stdout)
    if kind is not None:assert result['kind']==kind
    if code:assert result['valid'] is False and result['error']
    return result

with tempfile.TemporaryDirectory(prefix='ezapi-cli-') as temporary:
    scratch=Path(temporary).resolve()
    sample=scratch/'requirements.json'
    bilingual={'kind':'all','message':{'chinese':'制造条件','english':'Manufacturing requirements'},'children':[{'kind':'credits-at-least','amount':100,'message':{'chinese':'余额足够','english':'Enough credits'}}]}
    source=json.dumps(bilingual,ensure_ascii=False).encode('utf-8')
    sample.write_bytes(source)
    result=invoke(['--requirements',sample],0,'requirements')
    assert result['valid'] and result['nodeCount']==2 and result['nativeValidationPerformed'] is False and result['evaluationPerformed'] is False
    assert result['definition']['message']==bilingual['message'] and result['definition']['children'][0]['message']==bilingual['children'][0]['message']
    checks.append('separate CLI process preserves bilingual text and validates without native evaluation')
    assert sample.read_bytes()==source and sorted(p.name for p in scratch.iterdir())==['requirements.json']
    checks.append('valid input file is byte-identical and no sidecar files created')
    sample.write_text(json.dumps(result['definition'],ensure_ascii=False),encoding='utf-8')
    assert invoke(['--requirements',sample],0,'requirements')['definition']==result['definition']
    checks.append('normalized CLI definition can be parsed again without introducing prohibited null fields')
    invoke(['--help'],0);checks.append('help lists the requirements command')
    for parameters in [[],['--unknown'],['--requirement',sample],['--requirements'],['--requirements',sample,'extra'],['--capabilities','extra']]:
        invoke(parameters,2);checks.append('invalid argument form returns structured usage error: '+repr(parameters[:1]))
    missing=invoke(['--requirements',scratch/'missing.json'],1)
    assert 'missing.json' in missing['error'];checks.append('missing input has a file error')
    invalid=[
        ('unknown field','{"kind":"all","extra":1}'),
        ('duplicate field','{"kind":"all","kind":"all"}'),
        ('null child','{"children":[null]}'),
        ('unknown kind','{"kind":"purchase"}'),
        ('empty any','{"kind":"any"}'),
        ('mixed group arguments','{"kind":"all","amount":1}'),
        ('invalid target ID','{"kind":"upgrade-ready","targetId":"not valid"}'),
        ('amount overflow','{"kind":"credits-at-least","amount":2147483648}'),
        ('null message','{"message":null}'),
        ('duplicate message language','{"message":{"english":"a","english":"b"}}'),
        ('JSON syntax','{'),
        ('JSON byte limit',' '*65537),
    ]
    for name,text in invalid:
        sample.write_text(text,encoding='utf-8');before=hashlib.sha256(sample.read_bytes()).hexdigest()
        invoke(['--requirements',sample],1)
        assert hashlib.sha256(sample.read_bytes()).hexdigest()==before
        checks.append('invalid declaration rejected without changing input: '+name)
    sample.write_text('{"kind":"not","children":[{"kind":"service-active","targetId":"still-unknown-to-offline-tool"}]}',encoding='utf-8')
    result=invoke(['--requirements',sample],0,'requirements')
    assert result['evaluationPerformed'] is False
    checks.append('offline parser accepts readable native target without pretending to check native existence')
    capabilities=invoke(['--capabilities'],0)
    assert any(c['Id']=='localization.game-language' for c in capabilities['capabilities'])
    checks.append('shared CLI capability declarations include current language support')

report={'passed':len(checks),'failed':0,'checks':checks,'validatorSha256':hashlib.sha256(validator.read_bytes()).hexdigest().upper(),
    'scope':'Actual built CLI subprocess exit codes, strict requirements JSON, bilingual preservation and file immutability; no game or native requirement evaluation.'}
args.report.parent.mkdir(parents=True,exist_ok=True)
args.report.write_text(json.dumps(report,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
print(json.dumps({k:report[k] for k in ['passed','failed','scope']},ensure_ascii=False))
