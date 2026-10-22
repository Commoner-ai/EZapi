using EZapi;
using Il2Cpp;
var checks=new List<object>();int passed=0,failed=0;
void Check(bool value,string name){checks.Add(new{name,passed=value});if(value)passed++;else failed++;}
void Reject(Action action,string name){try{action();Check(false,name);}catch{Check(true,name);}}
GameItem Item(int n,string id="author.power.connector")=>new(){Pointer=(IntPtr)n,uniqueId=n,identifier=id};
var machine=Item(1,"author.power.machine");var connector=Item(2);int energy=100;Probe? probe=null;
Reject(()=>ExternalPowerApi.Register("author.power",connector.identifier,_=>energy,(_,_,_)=>new Probe(()=>energy,v=>energy=v,7)),"registration before start rejected");
ExternalPowerApi.Start();
Reject(()=>ExternalPowerApi.Register("author.power",connector.identifier,_=>100,(_,_,_)=>null!,(MachineMaterialContentsPolicy)77),"invalid furnace contents policy rejected before registration");
using(var policyLease=ExternalPowerApi.Register("author.power",connector.identifier,_=>100,(_,_,_)=>null!,MachineMaterialContentsPolicy.ConsumeGraph))Check(ExternalPowerApi.Find(connector)!.FurnaceMaterials==MachineMaterialContentsPolicy.ConsumeGraph,"explicit furnace whole-graph policy retained");
foreach(var owner in new[]{"","author.power\n","Author","author..power","author.power "})Reject(()=>ExternalPowerApi.Register(owner,connector.identifier,_=>100,(_,_,_)=>null!),"strict owner:"+owner);
Reject(()=>ExternalPowerApi.Register("author.power","foreign.connector",_=>100,(_,_,_)=>null!),"foreign namespace rejected");
Reject(()=>ExternalPowerApi.Register("author.power","author.power.some id",_=>100,(_,_,_)=>null!),"whitespace connector ID rejected");
Reject(()=>ExternalPowerApi.Register("author.power",connector.identifier,null!,(_,_,_)=>null!),"null energy rejected");
Reject(()=>ExternalPowerApi.Register("author.power",connector.identifier,_=>100,null!),"null plan rejected");
using(var provider=ExternalPowerApi.Register("author.power",connector.identifier,_=>energy,(_,_,amount)=>probe=new Probe(()=>energy,v=>energy=v,amount))) {
 Check(ExternalPowerApi.Find(connector)!.FurnaceMaterials==MachineMaterialContentsPolicy.Reject,"original four-argument registration retains reject policy");
 Check(ExternalPowerApi.IsConnector(connector)&&!ExternalPowerApi.IsConnector(Item(3,"author.power.other")),"connector exact identifier");
 var early=Item(300);early.identifier=null!;Check(!ExternalPowerApi.IsConnector(early),"native prebuilt source without ID bypasses connector lookup");
 Reject(()=>ExternalPowerApi.Register("author.power",connector.identifier,_=>energy,(_,_,_)=>null!),"active duplicate rejected");
 Check(ExternalPowerApi.GetEnergy(connector)==100&&ExternalPowerApi.IsConnected(connector),"connected query");
 energy=-1;Check(ExternalPowerApi.GetEnergy(connector)==0&&!ExternalPowerApi.IsConnected(connector),"negative energy is disconnected");
 energy=0;Check(ExternalPowerApi.GetEnergy(connector)==0&&ExternalPowerApi.IsConnected(connector),"zero energy is connected");energy=100;
 ExternalPowerRuntime.Ready=false;Check(!ExternalPowerApi.IsAvailable&&ExternalPowerApi.GetEnergy(connector)==0,"hook loss fail closed");ExternalPowerRuntime.Ready=true;
 using(var journal=ExternalPowerApi.Open(machine,connector,7)) {
  Check(journal.Validate(),"new plan validates");Reject(()=>ExternalPowerApi.Open(machine,connector,7),"second open journal rejected");
  Reject(()=>ExternalPowerApi.Register("author.power","author.power.other",_=>1,(_,_,_)=>null!),"registration while journal open rejected");
  journal.Debit();Check(energy==93&&journal.Verify()&&probe!.Debits==1,"one debit exact difference");
  Reject(journal.Debit,"second debit rejected");Check(journal.Restore()&&energy==100&&probe!.Restores==1,"successful provider restoration");
  Check(journal.Restore()&&probe!.Restores==1,"restoration is idempotent");
 }
 using(var journal=ExternalPowerApi.Open(machine,connector,0)){probe=null;Check(journal.Validate(),"zero plan validates");journal.Debit();Check(journal.Verify()&&probe is null&&energy==100,"zero avoids provider transaction");}
 foreach(var mode in new[]{"throw","verify","restore-false","restore-throw","ordinary","moved","run","source-change"}) {
  energy=100;using var journal=ExternalPowerApi.Open(machine,connector,7);probe!.Mode=mode;
  if(mode=="throw")Reject(journal.Debit,mode+": writing exception observed");else if(mode=="verify")Reject(journal.Debit,mode+": verify false observed");else journal.Debit();
  Check(energy==93,mode+": source actually written");
  if(mode=="moved")connector.parentInventory=new(){Pointer=(IntPtr)901};
  if(mode=="run")GameSession.Run=new(2);
  if(mode=="source-change")energy=92;
  var expected=mode is not ("restore-false" or "restore-throw" or "moved" or "run");
  Check(journal.Restore()==expected,mode+": expected restoration status");
  if(mode is "moved" or "run")Check(probe.Restores==0,mode+": no stale restore callback");
  else Check(probe.Restores==1,mode+": restore once including partial failures");
  connector.parentInventory=new();GameSession.Run=new(1);
 }
 energy=100;
 using(var journal=ExternalPowerApi.Open(machine,connector,7)){journal.Debit();provider.Dispose();Check(!journal.Verify(),"provider disposal invalidates in-flight verify");Check(journal.Restore()&&energy==100,"released provider retained for recovery");}
}
Check(!ExternalPowerApi.IsConnector(connector),"released registration removed from lookup");
using(var lease=ExternalPowerApi.Register("author.power",connector.identifier,_=>throw new InvalidOperationException("query"),(_,_,_)=>null!)){Check(ExternalPowerApi.GetEnergy(connector)==0&&!ExternalPowerApi.IsConnected(connector),"query exceptions isolated");}
using(var lease=ExternalPowerApi.Register("author.power",connector.identifier,item=>ExternalPowerApi.GetEnergy(item)+100,(_,_,_)=>null!)){Check(ExternalPowerApi.GetEnergy(connector)==0&&!ExternalPowerApi.IsConnected(connector),"recursive query cannot report fake connected energy");}
using(var lease=ExternalPowerApi.Register("author.power",connector.identifier,_=>100,(_,_,_)=>null!)){Reject(()=>ExternalPowerApi.Open(machine,connector,7),"null transaction rejected");}
using(var lease=ExternalPowerApi.Register("author.power",connector.identifier,_=>100,(_,_,_)=>throw new InvalidOperationException("plan"))){Reject(()=>ExternalPowerApi.Open(machine,connector,7),"plan exception releases open counter");}
using(var lease=ExternalPowerApi.Register("author.power",connector.identifier,_=>100,(_,_,amount)=>new Probe(()=>100,_=>{},amount))){using var journal=ExternalPowerApi.Open(machine,connector,7);Check(journal.Validate(),"new plan works after failed constructors");}
ExternalPowerApi.Stop();Check(!ExternalPowerApi.IsAvailable&&!ExternalPowerApi.IsConnector(connector),"stop clears registration and availability");
ExternalPowerApi.Start();Check(!ExternalPowerApi.IsConnector(connector),"restart does not revive old leases");
GameSession.OnThread=false;Reject(()=>ExternalPowerApi.Register("author.power",connector.identifier,_=>1,(_,_,_)=>null!),"wrong thread registration rejected");GameSession.OnThread=true;
var report=new{scope="real ExternalPower registry and power journal; native hooks/game identity are substitutes",passed,failed,cases=checks};
Directory.CreateDirectory("work");File.WriteAllText("work/ezapi-external-power-tests.json",System.Text.Json.JsonSerializer.Serialize(report));
Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(new{passed,failed}));
return failed==0?0:1;
sealed class Probe:IPowerTransaction {
 readonly Func<int> _read;readonly Action<int> _write;readonly int _before,_amount;internal string Mode="";internal int Debits,Restores;
 internal Probe(Func<int> read,Action<int> write,int amount)=>(_read,_write,_before,_amount)=(read,write,read(),amount);
 public bool Validate()=>_read()==_before;
 public void Debit(){Debits++;_write(_before-_amount);if(Mode=="throw")throw new InvalidOperationException("partial write");}
 public bool Verify()=>Mode!="verify"&&_read()==_before-_amount;
 public bool Restore(){Restores++;if(Mode=="restore-throw")throw new InvalidOperationException("restore");_write(_before);return Mode!="restore-false";}
 public void Fault(string reason){}
}
