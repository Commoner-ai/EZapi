using EZapi;
using Il2Cpp;
using System.Text.Json;
var checks=new List<object>();int failed=0;
void Check(bool ok,string name){checks.Add(new{name,passed=ok});if(!ok){failed++;Console.Error.WriteLine(name);}}
void Reject(Action action,string name){try{action();Check(false,name);}catch(ArgumentException){Check(true,name);}catch(InvalidOperationException){Check(true,name);}}
MachineLayoutDefinition Template(Func<GameItem,bool>? output=null,Func<GameItem,bool>? input=null)=>new(){Output=new(){Kind="container",ContainerCondition=output},LiquidInputs=new[]{new MachineContainerSlot{SlotId="water",Condition=input}}};
void Reset(){MachineContainerConditionApi.Stop();GameSession.OnThread=true;GameSession.Loaded=true;GameSession.Identity=new(1);MachineContainerConditionApi.Start();}
var machine=new GameItem();var item=new GameItem{identifier="vessel"};var inventory=new GameInventory{Owner=machine};
bool Allows(MachineLayoutDefinition t,string slot="output")=>MachineContainerConditionApi.Allows(item,inventory,machine,JsonMachineLayout.Serialize(t),slot);
Reset();var calls=0;Func<GameItem,bool> output=i=>{calls++;return i.unitValue==7;};Func<GameItem,bool> input=i=>i.unitValue==9;
var template=Template(output,input);var serialized=JsonMachineLayout.Serialize(template);var copy=JsonMachineLayout.Copy(template);
Check(!serialized.Contains("Condition",StringComparison.OrdinalIgnoreCase),"callback properties excluded from native JSON");
Check(copy.Output.ContainerCondition==output&&copy.LiquidInputs[0].Condition==input,"deep definition copy preserves both callbacks");
foreach(var json in new[]{"{\"output\":{\"kind\":\"container\",\"containerCondition\":{}}}","{\"liquidInputs\":[{\"slotId\":\"water\",\"condition\":{}}]}"}) {
 using var bytes=new MemoryStream(System.Text.Encoding.UTF8.GetBytes(json));
 try{JsonMachineLayout.Parse(bytes);Check(false,"JSON cannot silently accept callback fields");}catch(System.IO.InvalidDataException){Check(true,"JSON rejects runtime-only callback field");}
}
Reject(()=>MachineContainerConditionApi.RequireRegistered(machine.identifier,template),"low-level callbacks cannot be silently discarded");
using(var lease=MachineContainerConditionApi.Register("test.mod",machine.identifier,template)) {
 MachineContainerConditionApi.RequireRegistered(machine.identifier,template);Check(true,"registered copied callbacks satisfy layout creation");
 Check(!Allows(template)&&calls==1,"output false predicate rejects");item.unitValue=7;Check(Allows(template),"output true predicate allows");
 Check(!Allows(template,"liquid.water"),"input callback isolated from output");item.unitValue=9;Check(Allows(template,"liquid.water"),"matching input callback allows");
 Check(Allows(template,"liquid.other"),"unmatched slot does not invoke predicate");
 machine.identifier="other";Check(Allows(template),"different machine not restricted");machine.identifier="machine";
 Reject(()=>MachineContainerConditionApi.Register("test.mod",machine.identifier,template),"duplicate owner rule rejected");
 var view=MachineContainerConditionApi.Snapshot().Single();Check(view.Slots.SequenceEqual(new[]{"liquid.water","output"})&&!view.Faulted,"pure managed snapshot reports precise slots");
 var altered=new MachineLayoutDefinition{Title="different",Output=new(){Kind="container",ContainerCondition=output}};
 Reject(()=>MachineContainerConditionApi.Register("other.mod",machine.identifier,altered),"incompatible layout cannot install second rule");
 Check(!Allows(altered),"stored layout mismatch fails closed");
}
Check(MachineContainerConditionApi.Snapshot().Count==0&&Allows(template),"disposing last rule restores unrestricted result");
Reject(()=>JsonMachineLayout.Validate(new(){Output=new(){Kind="items",ContainerCondition=output}}),"item-grid output cannot declare container predicate");
Reset();var andTemplate=Template(_=>true);
using(var first=MachineContainerConditionApi.Register("first.mod","machine",andTemplate))using(var second=MachineContainerConditionApi.Register("second.mod","machine",Template(_=>false))) {
 Check(!Allows(andTemplate),"multiple authors combine with AND");second.Dispose();Check(Allows(andTemplate),"release only removes own restriction");
}
foreach(var mode in new[]{"throw","item-tag","owner-tag","value","identity","inventory","parent","state","recursion","epoch","stop"}) {
 Reset();item.unitValue=0;item.parentInventory=null;inventory.Owner=machine;inventory.Contents=0;item.state=new();machine.state=new();machine.identifier="machine";
 var local=mode;var nestedAllowed=true;MachineLayoutDefinition? t=null;
 t=Template(i=>{
  switch(local){
   case "throw":throw new InvalidOperationException("fixture");
   case "item-tag":i.state.Value++;break;
   case "owner-tag":machine.state.Value++;break;
   case "value":i.unitValue++;break;
   case "identity":i.unitCount++;break;
   case "inventory":inventory.Contents++;break;
   case "parent":i.parentInventory=inventory;break;
   case "state":i.state=new();break;
   case "recursion":nestedAllowed=Allows(t!);return nestedAllowed;
   case "epoch":GameSession.Identity=new(2);break;
   case "stop":MachineContainerConditionApi.Stop();break;
  }return true;
 });
 using var lease=MachineContainerConditionApi.Register("test.mod","machine",t);
 Check(!Allows(t),local+" fails closed");
 if(local=="recursion")Check(!nestedAllowed&&!MachineContainerConditionApi.Snapshot().Single().Faulted,"recursive call denied without poisoning valid callback");
 else if(local!="epoch"&&local!="stop") {var v=MachineContainerConditionApi.Snapshot().Single();Check(v.Faulted&&v.Error is not null&&!Allows(t),local+" faults rule until release");}
}
Reset();var count=0;var delayed=Template(_=>{count++;return false;});using(var lease=MachineContainerConditionApi.Register("test.mod","machine",delayed)) {
 GameSession.Loaded=false;Check(Allows(delayed)&&count==0,"load reconstruction does not call author predicate");GameSession.Loaded=true;Check(!Allows(delayed)&&count==1,"loaded run enforces registered condition");
 GameSession.OnThread=false;Check(!Allows(delayed)&&count==1,"off-thread condition evaluation rejected");GameSession.OnThread=true;
}
Reset();using(var empty=MachineContainerConditionApi.Register("test.mod","machine",Template()))Check(MachineContainerConditionApi.Snapshot().Count==0,"empty callbacks do not consume rule quota");
MachineContainerConditionApi.Stop();Check(MachineContainerConditionApi.Snapshot().Count==0,"stop releases all callbacks");
Console.WriteLine(JsonSerializer.Serialize(new{passed=checks.Count-failed,failed,checks,nativeRuntimeVerified=false,scope="real container callback registry/copy/disposal; native state and thread boundaries substituted"}));
if(failed>0)Environment.Exit(1);
