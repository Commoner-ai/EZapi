#nullable enable annotations
using Mono.Cecil;
var a=AssemblyDefinition.ReadAssembly(args[0]);
if(args.Contains("--developer-surface")) {
 // This mode reads our own DLL metadata and compiler XML documentation only.
 // It intentionally does not include method bodies or call graphs.
 bool Exported(TypeDefinition t)=>t.IsPublic||t.IsNestedPublic&&t.DeclaringType is not null&&Exported(t.DeclaringType);
 bool Generated(Mono.Cecil.ICustomAttributeProvider value)=>value.CustomAttributes.Any(c=>c.AttributeType.FullName=="System.Runtime.CompilerServices.CompilerGeneratedAttribute");
 string TypeId(TypeReference type)=>type.FullName.Replace('/','.');
 string ParameterId(TypeReference type)=>type switch {
    GenericParameter p=>(p.Type==GenericParameterType.Method?"``":"`")+p.Position,
    ByReferenceType b=>ParameterId(b.ElementType)+"@",
    PointerType p=>ParameterId(p.ElementType)+"*",
    ArrayType v=>ParameterId(v.ElementType)+(v.Rank==1?"[]":"["+string.Join(",",Enumerable.Repeat("0:",v.Rank))+"]"),
    GenericInstanceType g=>System.Text.RegularExpressions.Regex.Replace(TypeId(g.ElementType),@"`\d+","")+"{"+string.Join(",",g.GenericArguments.Select(ParameterId))+"}",
    RequiredModifierType m=>ParameterId(m.ElementType),
    OptionalModifierType m=>ParameterId(m.ElementType),
    _=>TypeId(type)};
 string MethodId(MethodDefinition m)=>"M:"+TypeId(m.DeclaringType)+"."+(m.IsConstructor?m.Name.Replace('.','#'):m.Name.Replace('.','#'))+
    (m.HasGenericParameters?"``"+m.GenericParameters.Count:"")+
    (m.HasParameters?"("+string.Join(",",m.Parameters.Select(p=>ParameterId(p.ParameterType)))+")":"")+
    (m.Name is "op_Implicit" or "op_Explicit"?"~"+ParameterId(m.ReturnType):"");
 var xmlPath=Path.ChangeExtension(args[0],".xml");
 var xml=File.Exists(xmlPath)?System.Xml.Linq.XDocument.Load(xmlPath):null;
 var comments=xml?.Descendants("member").ToDictionary(x=>(string)x.Attribute("name")!,x=>x)??new();
 string? Content(System.Xml.Linq.XElement? element)=>element is null?null:string.Join(" ",element.Value.Split((char[]?)null,StringSplitOptions.RemoveEmptyEntries));
 object? Docs(string id)=>comments.TryGetValue(id,out var c)?new{summary=Content(c.Element("summary")),remarks=Content(c.Element("remarks")),returns=Content(c.Element("returns")),parameters=c.Elements("param").ToDictionary(p=>(string)p.Attribute("name")!,p=>Content(p))}:null;
 object Parameters(ParameterDefinition p)=>new{name=p.Name,type=p.ParameterType.FullName,optional=p.IsOptional,hasDefault=p.HasConstant,defaultValue=p.HasConstant?p.Constant:null,isOut=p.IsOut,isParams=p.CustomAttributes.Any(c=>c.AttributeType.Name=="ParamArrayAttribute")};
 var types=a.MainModule.Types.SelectMany(All).Where(Exported).OrderBy(t=>t.FullName,StringComparer.Ordinal).Select(t=>new {
    id="T:"+TypeId(t),name=t.FullName,baseType=t.BaseType?.FullName,interfaces=t.Interfaces.Select(i=>i.InterfaceType.FullName).ToArray(),
    kind=t.IsEnum?"enum":t.IsInterface?"interface":t.IsValueType?"struct":"class",isAbstract=t.IsAbstract,isSealed=t.IsSealed,compilerGenerated=Generated(t),
    genericParameters=t.GenericParameters.Select(g=>new{name=g.Name,attributes=g.Attributes.ToString(),constraints=g.Constraints.Select(c=>c.ConstraintType.FullName).ToArray()}).ToArray(),documentation=Docs("T:"+TypeId(t)),
    fields=t.Fields.Where(f=>f.IsPublic).OrderBy(f=>f.Name,StringComparer.Ordinal).Select(f=>new{id="F:"+TypeId(t)+"."+f.Name,name=f.Name,type=f.FieldType.FullName,isStatic=f.IsStatic,isReadOnly=f.IsInitOnly,hasConstant=f.HasConstant,constant=f.HasConstant?f.Constant:null,compilerGenerated=Generated(f),documentation=Docs("F:"+TypeId(t)+"."+f.Name)}).ToArray(),
    properties=t.Properties.Where(p=>p.GetMethod?.IsPublic==true||p.SetMethod?.IsPublic==true).OrderBy(p=>p.FullName,StringComparer.Ordinal).Select(p=>new{
        id="P:"+TypeId(t)+"."+p.Name+(p.HasParameters?"("+string.Join(",",p.Parameters.Select(v=>ParameterId(v.ParameterType)))+")":""),name=p.Name,type=p.PropertyType.FullName,
        readable=p.GetMethod?.IsPublic==true,writable=p.SetMethod?.IsPublic==true,initOnly=p.SetMethod?.ReturnType.FullName.Contains("System.Runtime.CompilerServices.IsExternalInit")==true,
        isStatic=(p.GetMethod??p.SetMethod)!.IsStatic,parameters=p.Parameters.Select(Parameters).ToArray(),compilerGenerated=Generated(p),documentation=Docs("P:"+TypeId(t)+"."+p.Name+(p.HasParameters?"("+string.Join(",",p.Parameters.Select(v=>ParameterId(v.ParameterType)))+")":""))}).ToArray(),
    events=t.Events.Where(e=>e.AddMethod?.IsPublic==true||e.RemoveMethod?.IsPublic==true).OrderBy(e=>e.Name,StringComparer.Ordinal).Select(e=>new{id="E:"+TypeId(t)+"."+e.Name,name=e.Name,type=e.EventType.FullName,isStatic=(e.AddMethod??e.RemoveMethod)!.IsStatic,documentation=Docs("E:"+TypeId(t)+"."+e.Name)}).ToArray(),
    methods=t.Methods.Where(m=>m.IsPublic&&!m.IsGetter&&!m.IsSetter&&!m.IsAddOn&&!m.IsRemoveOn).OrderBy(m=>m.FullName,StringComparer.Ordinal).Select(m=>new {
        id=MethodId(m),name=m.Name,signature=m.FullName,returnType=m.ReturnType.FullName,isStatic=m.IsStatic,isConstructor=m.IsConstructor,isAbstract=m.IsAbstract,isVirtual=m.IsVirtual,compilerGenerated=Generated(m),
        genericParameters=m.GenericParameters.Select(g=>new{name=g.Name,attributes=g.Attributes.ToString(),constraints=g.Constraints.Select(c=>c.ConstraintType.FullName).ToArray()}).ToArray(),parameters=m.Parameters.Select(Parameters).ToArray(),documentation=Docs(MethodId(m))}).ToArray()
 }).ToArray();
 Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(new{assembly=a.Name.Name,version=a.Name.Version.ToString(),xmlDocumentationPresent=xml is not null,
    scope="Exported public type declarations and declared public members; property/event accessors represented by their declarations; inherited members resolved through baseType/interfaces; compiler-generated members retained and flagged; metadata nullability is not inferred",types}));return;
}
if(args.Contains("--native-address-index")) {
 var rows=a.MainModule.Types.SelectMany(All).SelectMany(t=>t.Methods).Select(m=>new{method=m.FullName,
  address=m.CustomAttributes.FirstOrDefault(c=>c.AttributeType.Name=="AddressAttribute")})
  .Where(x=>x.address is not null).Select(x=>new{method=x.method,
   fields=x.address!.Fields.ToDictionary(f=>f.Name,f=>f.Argument.Value)}).ToArray();
 Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(rows));return;
}
if(args.Contains("--assembly-references")) {
 Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(new {assembly=a.Name.Name,version=a.Name.Version.ToString(),references=a.MainModule.AssemblyReferences.Select(r=>r.FullName).ToArray()}));return;
}
if(args.Contains("--validate-unity-unstripping")) {
 var directory=args[Array.IndexOf(args,"--validate-unity-unstripping")+1];
 var assemblies=Directory.GetFiles(directory,"UnityEngine*.dll").Select(AssemblyDefinition.ReadAssembly).ToArray();
 string MethodKey(MethodReference reference) {var method=reference is GenericInstanceMethod g?g.ElementMethod:reference;
  return method.DeclaringType.Scope.Name.Replace(".dll","")+"|"+method.FullName;}
 var known=assemblies.SelectMany(x=>x.MainModule.Types.SelectMany(All)).SelectMany(t=>t.Methods).GroupBy(MethodKey).ToDictionary(g=>g.Key,g=>g.First());
 var roots=a.MainModule.Types.SelectMany(All).SelectMany(t=>t.Methods).Where(m=>m.HasBody).SelectMany(m=>m.Body.Instructions)
  .Select(i=>i.Operand).OfType<MethodReference>().Where(m=>m.DeclaringType.Scope.Name.StartsWith("UnityEngine")).GroupBy(MethodKey).Select(g=>g.First()).ToArray();
 var failures=new List<object>();var visited=new HashSet<string>();
 foreach(var root in roots) {
  var queue=new Queue<(string Key,string[] Path)>();var seen=new HashSet<string>();queue.Enqueue((MethodKey(root),new[]{root.FullName}));
  while(queue.Count>0) {
   var entry=queue.Dequeue();if(!seen.Add(entry.Key)||!known.TryGetValue(entry.Key,out var method)||!method.HasBody)continue;visited.Add(entry.Key);
   if(method.Body.Instructions.Any(i=>i.Operand is string s&&s=="Method unstripping failed")) {failures.Add(new{directReference=root.FullName,path=entry.Path});continue;}
   foreach(var call in method.Body.Instructions.Select(i=>i.Operand).OfType<MethodReference>().Where(m=>m.DeclaringType.Scope.Name.StartsWith("UnityEngine")))
    if(entry.Path.Length<32)queue.Enqueue((MethodKey(call),entry.Path.Concat(new[]{call.FullName}).ToArray()));
  }
 }
 Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(new{assembly=a.Name.Version.ToString(),directUnityReferences=roots.Length,reachableUnityMethods=visited.Count,
  unstrippingStubs=failures,passed=failures.Count==0,scope="static Unity wrapper calls, bounded to32 levels; native virtual dispatch and behavior still require game verification"}));
 if(failures.Count>0)Environment.Exit(1);return;
}
if(args.Contains("--validate-game-references")) {
 var gamePath=args[Array.IndexOf(args,"--validate-game-references")+1];
 var game=AssemblyDefinition.ReadAssembly(gamePath);
 var known=game.MainModule.Types.SelectMany(All).ToDictionary(t=>t.FullName);
 var references=a.MainModule.Types.SelectMany(All).SelectMany(t=>t.Methods).Where(m=>m.HasBody)
  .SelectMany(m=>m.Body.Instructions).Select(i=>i.Operand).OfType<MethodReference>()
  .Where(m=>m.DeclaringType.Scope.Name==game.Name.Name).GroupBy(m=>m.FullName).Select(g=>g.First()).ToArray();
 string Key(TypeReference type)=>type switch {
  GenericParameter parameter=>(parameter.Type==GenericParameterType.Method?"!!":"!")+parameter.Position,
  GenericInstanceType instance=>instance.ElementType.FullName+"<"+string.Join(",",instance.GenericArguments.Select(Key))+">",
  ByReferenceType byRef=>Key(byRef.ElementType)+"&",
  ArrayType array=>Key(array.ElementType)+"["+new string(',',array.Rank-1)+"]",
  PointerType pointer=>Key(pointer.ElementType)+"*",
  _=>type.FullName};
 bool Exists(MethodReference reference) {
  if(reference is GenericInstanceMethod genericMethod)reference=genericMethod.ElementMethod;
  var typeName=reference.DeclaringType.GetElementType().FullName;
  while(known.TryGetValue(typeName,out var type)) {
   if(type.Methods.Any(m=>m.Name==reference.Name&&Key(m.ReturnType)==Key(reference.ReturnType)&&
      m.HasThis==reference.HasThis&&m.GenericParameters.Count==reference.GenericParameters.Count&&
      m.Parameters.Select(p=>Key(p.ParameterType)).SequenceEqual(reference.Parameters.Select(p=>Key(p.ParameterType)))))return true;
   typeName=type.BaseType?.FullName??"";
  }
  return false;
 }
 var missing=references.Where(m=>!Exists(m)).Select(m=>m.FullName).ToArray();
 Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(new{assembly=a.Name.Version.ToString(),gameAssembly=gamePath,
   scope="direct method references including getters/setters; exact signature and inherited game members; does not validate reflection/hook installation/native behavior",
   checkedReferences=references.Length,missing,passed=missing.Length==0}));
 if(missing.Length>0)Environment.Exit(1);return;
}
if(args.Contains("--field-attributes")) {
 foreach(var t in a.MainModule.Types.SelectMany(All).Where(t=>args.Skip(2).Contains(t.Name)))foreach(var f in t.Fields){Console.WriteLine(f.FullName);foreach(var attr in f.CustomAttributes)Console.WriteLine(attr.AttributeType.FullName+" "+string.Join(";",attr.Fields.Select(x=>x.Name+"="+x.Argument.Value)));}return;
}
if(args.Contains("--method-attributes")) {
 foreach(var t in a.MainModule.Types.SelectMany(All).Where(t=>args.Skip(2).Contains(t.Name)))foreach(var m in t.Methods) {
  Console.WriteLine(m.FullName);foreach(var attr in m.CustomAttributes)Console.WriteLine(attr.AttributeType.FullName+" "+string.Join(";",attr.Fields.Select(f=>f.Name+"="+f.Argument.Value))+" "+string.Join(";",attr.Properties.Select(f=>f.Name+"="+f.Argument.Value)));
 }return;
}
if(args.Contains("--method-il")) {
 foreach(var t in a.MainModule.Types.SelectMany(All).Where(t=>args.Skip(2).Contains(t.Name))) {
   Console.WriteLine("TYPE "+t.FullName);
   foreach(var m in t.Methods.Where(m=>m.HasBody)) {
     Console.WriteLine(m.FullName);
     foreach(var i in m.Body.Instructions)Console.WriteLine("  "+i);
   }
 }return;
}
if(args.Contains("--method-il-named")) {
 foreach(var t in a.MainModule.Types.SelectMany(All)) foreach(var m in t.Methods.Where(m=>m.HasBody&&args.Skip(2).Contains(m.Name))) {
  Console.WriteLine(m.FullName);foreach(var i in m.Body.Instructions)Console.WriteLine("  "+i);
 }return;
}
if(args.Contains("--public-surface")) {
 var types=a.MainModule.Types.SelectMany(All).Where(t=>t.IsPublic||t.IsNestedPublic).ToArray();
 var result=new {assembly=a.Name.Name,version=a.Name.Version.ToString(),types=types.Select(t=>new {
    name=t.FullName,baseType=t.BaseType?.FullName,
    fields=t.Fields.Where(f=>f.IsPublic).Select(f=>new {name=f.Name,type=f.FieldType.FullName,constant=f.HasConstant?f.Constant:null}).ToArray(),
    properties=t.Properties.Where(p=>p.GetMethod?.IsPublic==true||p.SetMethod?.IsPublic==true).Select(p=>new {name=p.Name,type=p.PropertyType.FullName,writable=p.SetMethod?.IsPublic==true}).ToArray(),
    methods=t.Methods.Where(m=>m.IsPublic&&!m.IsGetter&&!m.IsSetter&&!m.IsAddOn&&!m.IsRemoveOn).Select(m=>new {name=m.Name,signature=m.FullName,parameters=m.Parameters.Select(p=>new {name=p.Name,type=p.ParameterType.FullName,optional=p.IsOptional,defaultValue=p.HasConstant?p.Constant:null}).ToArray(),calls=m.HasBody?m.Body.Instructions.Select(i=>i.Operand).OfType<MethodReference>().Select(c=>c.FullName).Distinct().ToArray():Array.Empty<string>()}).ToArray()
 }).ToArray()};
 Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(result));return;
}
if(args.Contains("--refactor-check")) {
 var types=a.MainModule.Types.SelectMany(All).ToArray();
 var methods=types.SelectMany(t=>t.Methods).Where(m=>m.HasBody).ToArray();
 var calls=methods.SelectMany(m=>m.Body.Instructions).Select(i=>i.Operand).OfType<MethodReference>().ToArray();
 var forbiddenTypes=types.Where(t=>new[]{"ItemStackPatch","TooltipCompletion","AssetRegistry","ResolveOwnedItem","ResolveOwnedSprite"}.Contains(t.Name)).Select(t=>t.Name).ToArray();
 var autoPatchAttrs=types.Count(t=>t.CustomAttributes.Any(c=>c.AttributeType.Name=="HarmonyPatch"));
 var builds=calls.Count(m=>m.DeclaringType.Name=="RichTextBuilder"&&m.Name=="Build");
 var stackSetter=calls.Any(m=>m.DeclaringType.Name=="GameItemFunc"&&m.Name=="set_mayStackItemFunc");
 var spriteSetter=calls.Any(m=>m.DeclaringType.Name=="GameItem"&&m.Name=="SetSprite");
 var hooks=new[]{"add_OnModItemDirectoryInit","remove_OnModItemDirectoryInit","add_OnCreateTooltipEarly","remove_OnCreateTooltipEarly","add_OnCreateTooltipLate","remove_OnCreateTooltipLate","add_OnLootTablesLoaded","remove_OnLootTablesLoaded"};
 var missingHooks=hooks.Where(h=>!calls.Any(m=>m.DeclaringType.Name=="ModHook"&&m.Name==h)).ToArray();
 var activationSetters=new[]{"set_mayActivateSlotItemFunc","set_canActivateSlotItemFunc","set_onActivateSlotItemFunc"};
 var missingActivationSetters=activationSetters.Where(h=>!calls.Any(m=>m.DeclaringType.Name=="GameItemFunc"&&m.Name==h)).ToArray();
 var phases=new[]{"GameLoadedInit","GameLoadedEarly","GameLoadedNormal","GameLoadedLate","ShutterOpenedEarly","ShutterOpenedLate",
     "ShutterClosedEarly","ShutterClosedLate","GoingSleepEarly","GoingSleepLate","WakingUpEarly","WakingUpLate",
     "LeavingStoreEarly","LeavingStoreLate","ReturningStoreEarly","ReturningStoreLate","HandlingNightlyServicesEarly","HandlingNightlyServicesLate"};
 var missingLifecycleCalls=phases.SelectMany(p=>new[]{"add_On"+p,"remove_On"+p}).Where(h=>!calls.Any(m=>m.DeclaringType.Name=="ModHook"&&m.Name==h)).ToArray();
 var saveRequests=calls.Count(m=>m.DeclaringType.Name=="PlayerStore"&&m.Name is "SaveGame" or "InitialSave");
 var nativeRunData=calls.Any(m=>m.DeclaringType.Name=="PlayerStore"&&m.Name=="get_modData");
 var lootCalls=calls.Where(m=>m.DeclaringType.Name=="LootRegistry").Select(m=>m.Name).Distinct().OrderBy(x=>x).ToArray();
 var lootWrites=calls.Where(m=>m.DeclaringType.Name=="LootRegistry" && m.Name.StartsWith("set_")).Select(m=>m.Name).Distinct().OrderBy(x=>x).ToArray();
 var nativeConsumableCalls=calls.Where(m=>m.DeclaringType.Name is "FoodItemHelper" or "LiquidContainerHelper"||m.DeclaringType.Name=="WaterHelper"&&m.Name is "InitLiquidContainerItem" or "FillWithBaseWater").Select(m=>m.FullName).Distinct().ToArray();
 // 0.1.18 的原生验证要求新版 WaterHelper，不再沿用旧 helper 的三调用计数。
 var nativeConsumableContractMatched=a.Name.Version>=new Version(0,1,18,0)
   ? new[]{
     "System.Void Il2Cpp.FoodItemHelper::InitFoodItem(Il2Cpp.GameItem,System.Int32,System.Boolean,System.Boolean)",
     "System.Void Il2Cpp.FoodItemHelper::InitFreshFood(Il2Cpp.GameItem,System.Int32)",
     "System.Void Il2Cpp.WaterHelper::InitLiquidContainerItem(Il2Cpp.GameItem,System.Int32,System.Int32,System.Boolean,System.Boolean,System.Boolean,System.Boolean,System.Int32)",
     "System.Void Il2Cpp.WaterHelper::FillWithBaseWater(Il2Cpp.GameItem)"}.OrderBy(x=>x).SequenceEqual(nativeConsumableCalls.OrderBy(x=>x))
   : nativeConsumableCalls.Length==3;
 var nativeContainerCalls=calls.Where(m=>m.DeclaringType.Name is "ContainerHelper" or "PixelWindow" or "GameGridInventory"||m.DeclaringType.Name=="GameItem"&&m.Name=="SetContentWindow").Select(m=>m.FullName).Distinct().ToArray();
 var nativeServiceCalls=calls.Where(m=>m.DeclaringType.Name is "PowerHelper" or "MachineryHelper" or "WaterHelper" or "WaterFeatureHelper" or "DirectoryMaster").Select(m=>m.FullName).Distinct().ToArray();
 var nativeInventoryCalls=calls.Where(m=>m.DeclaringType.Name is "GameInventory" or "GameGridInventory" or "GridShape" or "GridShapeBuilder" or "SlotMarker" or "GraphNodeStorage").Select(m=>m.FullName).Distinct().ToArray();
 var inventoryTypes=new[]{"InventoryApi","InventoryChecks","InventoryPlacementApi","ItemTransferApi","InventoryTransferAudit"};
 var inventoryMutationCalls=types.Where(t=>inventoryTypes.Contains(t.Name)||t.DeclaringType is not null&&inventoryTypes.Contains(t.DeclaringType.Name)).SelectMany(t=>t.Methods).Where(m=>m.HasBody).SelectMany(m=>m.Body.Instructions).Select(i=>i.Operand).OfType<MethodReference>()
    .Where(m=>m.DeclaringType.Name=="GameItem"&&m.Name is "set_unitCount" or "SetUnitCount" or "set_parentInventory" or "Destroy"||m.DeclaringType.Name=="GameInventory"&&m.Name is "Expel" or "UncheckedAccept").Select(m=>m.FullName).ToArray();
 var report=new{assembly=a.Name.Version.ToString(),forbiddenTypes,autoPatchAttributes=autoPatchAttrs,manualTooltipBuildCalls=builds,perItemStackSetter=stackSetter,nativeSetSprite=spriteSetter,missingOfficialHookCalls=missingHooks,
    missingActivationSetters,missingLifecycleCalls,automaticSaveRequests=saveRequests,nativeRunData,
    lootRegistryCalls=lootCalls,lootRegistryWriteCalls=lootWrites,nativeConsumableCalls,nativeConsumableContractMatched,nativeContainerCalls,nativeServiceCalls,nativeInventoryCalls,inventoryMutationCalls,
    assemblyReferences=a.MainModule.AssemblyReferences.Select(r=>r.Name).ToArray(),
    publicAPIs=types.Where(t=>t.Name is "ItemRegistry" or "ItemActivation" or "GameEvents" or "RunData" or "RunDataStore" or "LootTables" or "JsonLoot" or "FrameworkInfo" or "ContentPacks" or "Localization" or "LocalizationCatalog" or "Transactions" or "Items" or "ItemInspection" or "Power" or "Liquids" or "InventoryApi" or "InventoryPlacementApi" or "ItemTransferApi" or "HookApi" or "InventoryAdmissionApi" or "InventoryFreezeApi" or "ListInventoryApi" or "SaveGraphApi" or "LifecycleApi" or "ContentApi" or "EffectApi" or "ProductionValueApi" or "ProductionValueMath" or "MachinePowerMath" or "LiquidTransferApi" or "LiquidCompositionMath").SelectMany(t=>t.Methods).Where(m=>m.IsPublic).Select(m=>m.FullName).ToArray()};
 Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(report));
 if(forbiddenTypes.Length>0||autoPatchAttrs!=0||builds!=0||!stackSetter||!spriteSetter||missingHooks.Length>0||inventoryMutationCalls.Length>0||
    (a.Name.Version>=new Version(0,1,3,0)&&missingActivationSetters.Length>0)||
    (a.Name.Version>=new Version(0,1,4,0)&&(missingLifecycleCalls.Length>0||saveRequests>0||!nativeRunData))||lootWrites.Length>0||lootCalls.Length==0||
    (a.Name.Version>=new Version(0,1,7,0)&&!nativeConsumableContractMatched)||
    (a.Name.Version>=new Version(0,1,9,0)&&!nativeContainerCalls.Any(c=>c.Contains("::InitContainerItem("))))Environment.Exit(1);
 return;
}
if(args.Contains("--official-contract")) {
 var types=a.MainModule.Types.ToArray();
 var names=new[]{"SetSpriteAndShapeFromMod","SpawnItem","GetLocalized","PlaySound","GetSoundIdentifier",
     "SetModData","GetModData","SetModDataInt","GetModDataInt","SetModDataBool","GetModDataBool",
     "HasModData","RemoveModData","GetModDataKeys","ClearModData","RegisterTable","RegisterGroup","AddEntry","AddGroupEntry","RemoveEntry","RemoveGroupEntry","SetWeight","SetGroupWeight","RebuildIfDirty","Roll","RollGroup"};
 foreach(var name in names) {
   var found=types.SelectMany(t=>t.Methods).Where(m=>m.Name==name).Select(m=>m.FullName).ToArray();
   Console.WriteLine(name+": "+(found.Length==0?"MISSING":string.Join("; ",found)));
 }
 return;
}
if(args.Contains("--hotfix-check")) {
 var completion=a.MainModule.Types.Any(t=>t.Name=="TooltipCompletion");
 var builds=a.MainModule.Types.SelectMany(t=>t.Methods).Where(m=>m.HasBody).SelectMany(m=>m.Body.Instructions)
  .Select(i=>i.Operand).OfType<MethodReference>().Count(m=>m.DeclaringType.Name=="RichTextBuilder"&&m.Name=="Build");
 var stack=a.MainModule.Types.Single(t=>t.Name=="ItemStackPatch").Methods.Single(m=>m.Name=="Install");
 var patches=stack.Body.Instructions.Select(i=>i.Operand).OfType<MethodReference>().Count(m=>m.Name=="Patch"&&m.DeclaringType.Name=="Harmony");
 var forbidden=stack.Body.Instructions.Select(i=>i.Operand).OfType<string>().Any(s=>s=="GetTooltip"||s=="GetTooltipAdvanced");
 Console.WriteLine($"assembly={a.Name.Version}; TooltipCompletion={completion}; RichTextBuilder.Build calls={builds}; ItemStackPatch patch sites={patches}; tooltip patch target={forbidden}");
 if(completion||builds!=0||patches!=2||forbidden)Environment.Exit(1);
 return;
}
if(args.Contains("--addresses")) {
 foreach(var t in a.MainModule.Types)foreach(var m in t.Methods)foreach(var at in m.CustomAttributes.Where(x=>x.AttributeType.Name.Contains("Address")))
 Console.WriteLine(m.FullName+" "+string.Join(";",at.Fields.Select(f=>f.Name+"="+f.Argument.Value)));
 return;
}
IEnumerable<TypeDefinition> All(TypeDefinition t) => new[]{t}.Concat(t.NestedTypes.SelectMany(All));
if(args.Contains("--types")) { foreach(var t in a.MainModule.Types.SelectMany(All).Where(t=>args.Skip(2).Any(n=>t.Name.Contains(n,StringComparison.OrdinalIgnoreCase)))) Console.WriteLine(t.FullName); return; }
if(args.Contains("--names")) { foreach(var t in a.MainModule.Types.SelectMany(All).Where(t=>t.Name.Contains("Translat",StringComparison.OrdinalIgnoreCase)||t.Name.Contains("Language",StringComparison.OrdinalIgnoreCase)||t.Name.Contains("Localiz",StringComparison.OrdinalIgnoreCase))) Console.WriteLine(t.FullName); return; }
if(args.Contains("--tooltip")) { foreach(var t in a.MainModule.Types.SelectMany(All)) foreach(var m in t.Methods.Where(m=>m.Name.Contains("TooltipRich")||m.Name.Contains("CreateTooltip")||m.Name.Contains("GetTooltip"))) Console.WriteLine(m.FullName+" "+string.Join(",",m.Parameters.Select(p=>p.Name+":"+p.ParameterType))); return; }
foreach(var t in a.MainModule.Types.SelectMany(All).Where(t=>args.Skip(1).Any(s=>t.Name.Equals(s,StringComparison.OrdinalIgnoreCase))))
{
 Console.WriteLine("TYPE "+t.FullName+" BASE "+t.BaseType);
 foreach(var f in t.Fields.Where(f=>f.HasConstant)) Console.WriteLine(" CONST "+f.FullName+" = "+f.Constant);
 foreach(var m in t.Methods.Where(m=>m.Name==".ctor"))Console.WriteLine(" CTOR "+m.FullName+" PARAMS "+string.Join(",",m.Parameters.Select(p=>p.Name+":"+p.ParameterType+(p.HasConstant?"="+p.Constant:""))));
 foreach(var p in t.Properties.Where(p=>!p.Name.StartsWith("Native"))) Console.WriteLine(" PROPERTY "+p.FullName+" static="+p.GetMethod?.IsStatic+" setter="+(p.SetMethod is not null));
 foreach(var m in t.Methods.Where(m=>!m.Name.StartsWith("get_")&&!m.Name.StartsWith("set_")&&!m.Name.StartsWith("."))) Console.WriteLine(" METHOD "+m.FullName+" PARAMS "+string.Join(",",m.Parameters.Select(p=>p.Name+":"+p.ParameterType+(p.HasConstant?"="+p.Constant:""))));
}

