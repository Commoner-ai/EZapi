"""Build a complete, signature-keyed JSON inventory from the current EZapi DLL."""
from pathlib import Path
import argparse, collections, hashlib, json, re, subprocess

root=Path(__file__).resolve().parent.parent
p=argparse.ArgumentParser()
p.add_argument('--dll',type=Path,default=root/'src/EZapi/bin/Release/net6.0/EZapi.dll')
p.add_argument('--inspector',type=Path,default=root/'work/ezapi-inspect/bin/Release/net8.0/Inspect.dll')
p.add_argument('--api-index',type=Path,default=root/'docs/API.index.zh-CN.json')
p.add_argument('--output',type=Path,default=root/'docs/public-api.index.json')
p.add_argument('--version',required=True)
a=p.parse_args()
dll=a.dll.resolve(strict=True);inspector=a.inspector.resolve(strict=True)
process=subprocess.run(['dotnet',str(inspector),str(dll),'--developer-surface'],capture_output=True,text=True,encoding='utf-8',timeout=60)
if process.returncode:raise SystemExit(process.stderr or process.stdout)
surface=json.loads(process.stdout)
if surface['version']!=a.version+'.0':raise SystemExit(f"assembly version mismatch: {surface['version']} != {a.version}.0")
manual=json.loads(a.api_index.read_text(encoding='utf-8-sig'))
# Explicit task routes reviewed against our source files, not guesses based on
# reference-framework names. All partial declarations retain all their sources.
routes=[
 ('framework','框架与能力','Framework and capabilities','runtime-capabilities.zh-CN.md','', 'FrameworkInfo RuntimeCapabilities EZapiBootstrap'),
 ('registration','声明与目录应用','Declarations and catalog application','registration.zh-CN.md','RegistrationExamples','Registration RegistrationApi RegistrationBatch JsonRegistration Content'),
 ('packs','JSON内容包与依赖','JSON content packs and dependencies','content-packs.zh-CN.md','ExampleMod','ContentPacks PackManifest'),
 ('items','物品字段与注册','Item fields and registration','native-items.zh-CN.md','NativeItemExamples','ItemDefinition DefinitionRules JsonItems ItemRegistry Items NativeItems'),
 ('activation','物品激活','Item activation','item-activation.zh-CN.md','ExampleMod','ItemActivation'),
 ('stock','物品夜间供应','Nightly item stock','item-stock.zh-CN.md','ItemStockExamples','ItemStockContract'),
 ('npc-stock','NPC货位','NPC trade stock','npc-stock.zh-CN.md','ItemStockExamples','NpcStockApi NpcStockDefinition'),
 ('loot','掉落表','Loot tables','loot-tables.zh-CN.md','ExampleMod','LootDefinition JsonLoot LootTables'),
 ('events','生命周期','Lifecycle','lifecycle-content.zh-CN.md','LifecycleContentExamples','GameEvents GameRunIdentity Lifecycle'),
 ('save','周目数据','Run data','run-data-records.zh-CN.md','RunDataRecordExamples','RunData RunDataApi'),
 ('item-data','物品自有版本化数据','Versioned item-owned JSON','item-data.zh-CN.md','ItemDataExamples','ItemDataApi'),
 ('language','当前语言与CSV','Game language and CSV','game-language.zh-CN.md','LanguageExamples','GameLanguage Localization LocalizedItemText'),
 ('sprites','嵌入图像与肖像','Embedded sprites and portraits','sprite-assets.zh-CN.md','SpriteAssetExamples','EmbeddedSpriteDefinition SpriteAssets'),
 ('effects','效果与生产数值','Effects and production values','production-effects.zh-CN.md','ProductionEffectExamples','Effects ProductionValue LiquidComposition'),
 ('modules','模块与节点','Modules and nodes','modules.zh-CN.md','ModuleExamples','Modules'),
 ('power','电量','Power','power.zh-CN.md','PowerExamples','Power PowerBatchMath'),
 ('external-power','外接电源','External power','external-power.zh-CN.md','ExternalPowerExamples','ExternalPower'),
 ('liquids','液体组成与转移','Liquid composition and transfer','liquid-transfer.zh-CN.md','LiquidTransferExamples','Liquids LiquidMetadata LiquidTransfer'),
 ('inventory','库存观察与移动','Inventory observation and movement','inventory-services.zh-CN.md','InventoryExamples','InventoryServices InventoryPlacement ItemTransfer'),
 ('guards','库存准入与冻结','Inventory admission and freeze','inventory-guards.zh-CN.md','InventoryGuardExamples','InventoryAdmission InventoryFreeze ListInventory'),
 ('endpoint','持久库存端点','Persistent inventory endpoints','inventory-endpoints.zh-CN.md','InventoryExamples','InventoryEndpoint'),
 ('save-graph','物品图保存','Item graph persistence','save-graph.zh-CN.md','SaveGraphExamples','SaveGraph'),
 ('save-readback','存档只读回看','Read-only save inspection','save-readback.zh-CN.md','SaveReadbackExamples','SaveReadback'),
 ('layouts','机器布局','Machine layouts','machine-layouts-guide.zh-CN.md','MachineGuideExamples','MachineLayoutDefinition MachineLayoutCallbacks MachineLayouts'),
 ('recipes','机器配方','Machine recipes','machine-recipes.zh-CN.md','MachineRecipeExamples','MachineRecipeDefinition MachineRecipes'),
 ('machine-registration','机器登记','Machine registration','machine-registration.zh-CN.md','MachineRegistrationExamples','MachineRegistration'),
 ('machine-transactions','机器事务','Machine transactions','machine-transactions.zh-CN.md','MachineTransactionExamples','MachineTransactions'),
 ('machine-conditions','机器容器条件','Machine container conditions','machine-container-conditions.zh-CN.md','MachineRecipeExamples','MachineContainerConditions'),
 ('machine-runtime','机器自动化与跨夜','Machine automation and nightly processing','machine-automation.zh-CN.md','MachineAutomationExamples','MachineAutomation MachineRuntime'),
 ('terminal','制造终端','Manufacturing terminal','manufacturing-terminal.zh-CN.md','ManufacturingTerminalExamples','ManufacturingTerminal'),
 ('guide','配方图鉴','Recipe guide','recipe-guide-localization.zh-CN.md','MachineGuideExamples','RecipeGuide RecipeGuideDefinition'),
 ('presentation','来源名与提示','Owner names and tooltip text','presentation.zh-CN.md','PresentationExamples','PresentationApi'),
 ('native-ui','原生UI积木与AI预览','Native UI components and AI preview','native-ui-preview.zh-CN.md','','NativeUiDefinition NativeUiComponents NativeUiPreview'),
 ('reading','阅读面板','Reading panel','reading.zh-CN.md','ReadingExamples','Reading PresentationResult'),
 ('dialogue','对话','Dialogue','dialogue.zh-CN.md','DialogueExamples','Dialogue DialogueDefinition'),
 ('departure','自适应外出地点按钮','Adaptive departure buttons','departure-buttons.zh-CN.md','DepartureExamples','DepartureApi DepartureButton'),
 ('phone','电话','Phone contacts','phone.zh-CN.md','PhoneExamples','Phone PhoneContact'),
 ('visits','顾客到访','Customer visits','client-visits.zh-CN.md','ClientVisitExamples','ClientVisit'),
 ('suppliers','供应商','Suppliers','suppliers.zh-CN.md','SupplierExamples','SupplierApi SupplierDefinition'),
 ('purchases','购买与通用事务','Purchases and transactions','run-purchase.zh-CN.md','RunPurchaseExamples','RunPurchase Transactions'),
 ('start','开局身份','Start identities','start.zh-CN.md','StartExamples','StartApi'),
 ('workshop','工坊进度','Workshop progression','workshop.zh-CN.md','WorkshopExamples','WorkshopApi WorkshopDefinitions'),
 ('progression','升级服务与前置条件','Upgrades services and requirements','progression-requirements.zh-CN.md','ProgressionExamples','Progression Requirements RequirementApi'),
 ('helpers','日志与窗口输入','Logging and window input','public-helpers.zh-CN.md','PublicHelperExamples','ModLog WindowInputCapture'),
 ('infrastructure','原生适配基础设施','Native adapter infrastructure','runtime-capabilities.zh-CN.md','','NativeHooks NativeDelegateBridge'),
]
source_routes={};domain_rows=[]
for key,zh,en,guide,example,files in routes:
    assert (root/'docs'/guide).is_file(),guide
    example_path='../src/EZapi.Sample/'+example+'.cs' if example else None
    if example_path:assert (root/'docs'/example_path).is_file(),example_path
    domain_rows.append(dict(id=key,taskZh=zh,taskEn=en,guide=guide,example=example_path))
    for file in files.split():
        assert file not in source_routes,file
        source_routes[file]=key
source_pattern=re.compile(r'\bpublic\s+(?:(?:static|sealed|abstract|readonly|partial|new)\s+)*(?:record\s+(?:(?:struct|class)\s+)?|class\s+|struct\s+|interface\s+|enum\s+)([A-Za-z_]\w*)')
source_types=collections.defaultdict(list)
for path in sorted((root/'src/EZapi').glob('*.cs')):
    for match in source_pattern.finditer(path.read_text(encoding='utf-8-sig')):
        source_types[match.group(1)].append(dict(path='../src/EZapi/'+path.name,line=path.read_text(encoding='utf-8-sig')[:match.start()].count('\n')+1))
rows=[];ids=set();counts=collections.Counter();curated=collections.defaultdict(set)
for entry in manual.get('entries',[]):
    identifier=entry.get('id','')
    owner=next((t['name'] for t in surface['types'] if identifier==t['name'] or identifier.startswith(t['name']+'.')),None)
    if owner:curated[owner].add(identifier)
for typ in surface['types']:
    type_name=typ['name'].rsplit('.',1)[-1].split('`')[0]
    sources=source_types.get(type_name,[])
    if not sources:raise SystemExit('no source declaration for '+typ['name'])
    domains={source_routes.get(Path(s['path']).stem) for s in sources}
    if None in domains or len(domains)!=1:raise SystemExit('unreviewed or ambiguous task route: '+typ['name']+' '+repr(domains))
    members=[]
    for kind in ('fields','properties','events','methods'):
        for member in typ.get(kind,[]):
            identifier=member['id']
            if identifier in ids:raise SystemExit('duplicate metadata ID: '+identifier)
            ids.add(identifier);counts[kind]+=1
            members.append({'kind':kind[:-1] if kind.endswith('s') else kind,**member})
    type_id=typ['id']
    if type_id in ids:raise SystemExit('duplicate metadata ID: '+type_id)
    ids.add(type_id);counts['types']+=1
    rows.append({'id':type_id,'name':typ['name'],'kind':typ['kind'],'baseType':typ['baseType'],'interfaces':typ['interfaces'],
        'abstract':typ['isAbstract'],'sealed':typ['isSealed'],'compilerGenerated':typ['compilerGenerated'],
        'genericParameters':typ['genericParameters'],'documentation':typ['documentation'],'sources':sources,'domain':next(iter(domains)),
        'curatedEntryIds':sorted(curated.get(typ['name'],set())),
        'fields':typ['fields'],'properties':typ['properties'],'events':typ['events'],'methods':typ['methods']})
required={'EZapi.GameLanguage','EZapi.GameLanguageSnapshot','EZapi.ProgressionApi','EZapi.Requirements','EZapi.RequirementApi'}
names={x['name'] for x in rows}
if required-names:raise SystemExit('required public API absent: '+repr(sorted(required-names)))
member_count=sum(counts[k] for k in ('fields','properties','events','methods'))
document={
 'schemaVersion':1,'framework':'EZapi','version':a.version,'assembly':surface['assembly'],'assemblyVersion':surface['version'],
 'dllSha256':hashlib.sha256(dll.read_bytes()).hexdigest().upper(),
 'source':'compiled EZapi.dll public metadata plus compiler-generated XML documentation',
 'curatedUsageIndex':'API.index.zh-CN.json','typeLevelSourceIndex':'source.index.json','errorIndex':'errors.index.json',
 'scope':surface['scope'],'complete':True,'counts':{'types':counts['types'],'fields':counts['fields'],'properties':counts['properties'],'events':counts['events'],'methods':counts['methods'],'members':member_count,'declarations':counts['types']+member_count},
 'inheritanceRule':'Only members declared on a type appear in that type; resolve inherited members using baseType and interfaces.',
 'identifierRule':'Stable documentation IDs use T:/F:/P:/E:/M: prefixes; method IDs contain parameter signatures so overloads never collide.',
 'metadataLimits':['C# nullable annotations are not inferred from IL2CPP metadata.','Compiler-generated record members are retained and flagged; ordinary examples should use constructors, init properties and named APIs.','A signature inventory is not behavior evidence; follow curatedEntryIds and the linked guides/evidence.'],
 'domains':domain_rows,'types':rows
}
release_status=root/'docs/release-status.json'
if release_status.is_file():
    release=json.loads(release_status.read_text(encoding='utf-8-sig'))
    document['documentationContext']={
        'kind':'current-api-guide','releaseTag':release['releaseTag'],
        'assemblyVersion':a.version,'releaseStatus':'release-status.json',
        'nativeRuntimeVerified':False,
        'evidencePolicy':'Historical DLL evidence does not apply to this generated metadata.'}

a.output.parent.mkdir(parents=True,exist_ok=True)
a.output.write_text(json.dumps(document,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
print(json.dumps({'version':a.version,'dllSha256':document['dllSha256'],'counts':document['counts'],'complete':document['complete']},ensure_ascii=False))
