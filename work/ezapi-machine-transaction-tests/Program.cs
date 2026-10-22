using EZapi;using Il2Cpp;using System.Text.Json;
var cases=new List<string>();
void Check(bool ok,string name){if(!ok)throw new Exception(name);cases.Add(name);}
void Bad(Action a,string name){try{a();throw new Exception("accepted "+name);}catch(ArgumentException){cases.Add(name);}}
GameItem machine=new(){identifier="test.machine"};
MachineBatchContext Context(int count=1)=>new(machine,Array.Empty<MachineItemTake>(),new Dictionary<string,GameItem>(),count);
var one=new[]{new MachineProductDefinition("test.product")};
MachineTransactionResult Run(Tx tx,IReadOnlyList<MachineProductDefinition>? outputs=null,Action<MachineBatchContext,IReadOnlyList<GameItem>>? done=null,MachineBatchContext? context=null)=>MachineTransactionApi.Run("test.owner",context??Context(),outputs??one,tx,done);
void Reset(){machine=new(){identifier="test.machine"};GameSession.Identity=new(1,"test",GameSession.Identity.Epoch+1);Items.Factory=null;Items.Made.Clear();InventoryApi.IsItemDragActive=false;MachineTransactionApi.Stop();MachineTransactionApi.Start();}
var pre=new Tx();Check(Run(pre).Status==MachineTransactionStatus.Rejected&&pre.Validations==0,"stopped rejects before callbacks or factories");
Reset();var tx=new Tx();var result=Run(tx);Check(result.Committed&&tx.Validations==2&&tx.Applies==1&&tx.Verifications==1&&tx.Restores==0,"successful batch validates twice and applies/verifies once");
Check(result.Created.Count==1&&result.Created[0].InstanceId==Items.Made.Single().uniqueId&&!result.MayRetry,"committed result has managed receipt and cannot retry");
Check(result.Created is not MachineProductReceipt[],"receipts do not expose mutable array");
Reset();tx=new(){Check=_=>false};result=Run(tx);Check(result.Status==MachineTransactionStatus.Rejected&&Items.Made.Count==0&&tx.Applies==0&&result.MayRetry,"initial resource shortage does not create products or apply");
Reset();tx=new(){Check=n=>n==1};result=Run(tx);Check(result.Status==MachineTransactionStatus.Rejected&&Items.Made.Single().Destroyed&&tx.Restores==0,"resource shortage after preparation cleans only new product without Apply");
Reset();tx=new(){Check=_=>throw new InvalidOperationException("validation")};result=Run(tx);Check(result.Status==MachineTransactionStatus.Rejected&&result.Error!.Contains("validation")&&tx.Applies==0,"validation exception is no-write rejection");
Reset();Items.Factory=_=>throw new InvalidOperationException("factory");tx=new();result=Run(tx);Check(result.Status==MachineTransactionStatus.Rejected&&tx.Applies==0&&result.Error!.Contains("factory"),"factory exception does not consume resources");
Reset();tx=new();result=Run(tx,new[]{new MachineProductDefinition("a",Prepare:(_,_)=>throw new Exception("prepare"))});Check(result.Status==MachineTransactionStatus.Rejected&&Items.Made.Single().Destroyed&&tx.Applies==0,"prepare exception cleans allocated product");
Reset();tx=new();result=Run(tx,new[]{new MachineProductDefinition("a",Prepare:(_,i)=>i.unitValue=777)});Check(result.Committed&&Items.Made.Single().unitValue==777,"product preparation can configure value");
Reset();tx=new();result=Run(tx,new[]{new MachineProductDefinition("a",Prepare:(_,i)=>i.parentInventory=new())});Check(result.Status==MachineTransactionStatus.RecoveryFailed&&!Items.Made.Single().Destroyed&&tx.Faults==1&&tx.Applies==0,"early product delivery is preserved and faults instead of deleting attached item");
Reset();tx=new(){Write=p=>{p[0].parentInventory=new();throw new Exception("partial");}};result=Run(tx);Check(result.Status==MachineTransactionStatus.Restored&&tx.Restores==1&&Items.Made.Single().Destroyed&&result.MayRetry,"partial Apply throw restores resources and cleans product");
Reset();tx=new(){VerifyResult=false};result=Run(tx);Check(result.Status==MachineTransactionStatus.Restored&&tx.Restores==1,"false verification invokes one compensation");
Reset();tx=new(){Readback=()=>throw new Exception("verify")};result=Run(tx);Check(result.Status==MachineTransactionStatus.Restored&&result.Error!.Contains("verify"),"verification exception compensates");
Reset();tx=new(){Write=_=>{}};result=Run(tx);Check(result.Status==MachineTransactionStatus.Restored&&Items.Made.Single().Destroyed,"transaction claiming success without product delivery is rejected");
Reset();tx=new(){VerifyResult=false,Recovery=()=>false};result=Run(tx);Check(result.Status==MachineTransactionStatus.RecoveryFailed&&tx.Faults==1&&!result.MayRetry&&!Items.Made.Single().Destroyed,"failed Restore preserves attached product and disables retry");
Reset();tx=new(){VerifyResult=false,Recovery=()=>throw new Exception("restore")};result=Run(tx);Check(result.Status==MachineTransactionStatus.RecoveryFailed&&result.RecoveryFailures.Any(s=>s.Contains("restore"))&&tx.Faults==1,"Restore exception faults once and still audits products");
Reset();tx=new(){Write=_=>throw new Exception("apply"),Recovery=()=>false};result=Run(tx);Check(result.Status==MachineTransactionStatus.RecoveryFailed&&Items.Made.Single().Destroyed,"failed resource Restore still cleans detached product");
Reset();tx=new(){VerifyResult=false,Recovery=()=>false,FaultAction=()=>throw new Exception("fault")};result=Run(tx);Check(result.Status==MachineTransactionStatus.RecoveryFailed&&tx.Faults==1&&result.RecoveryFailures.Any(s=>s.Contains("fault")),"Fault exception retained without losing uncertain result");
Reset();tx=new(){Write=p=>{p[0].parentInventory=new();GameSession.Identity=GameSession.Identity with {Epoch=GameSession.Identity.Epoch+1};}};result=Run(tx);Check(result.Status==MachineTransactionStatus.RecoveryFailed&&tx.Restores==0&&!Items.Made.Single().Destroyed,"run change after write refuses old recovery and product destruction");
Reset();tx=new(){Write=p=>{p[0].parentInventory=new();machine.uniqueId++;}};result=Run(tx);Check(result.Status==MachineTransactionStatus.RecoveryFailed&&tx.Restores==0,"machine identity change refuses resource recovery");
Reset();tx=new(){Write=p=>{p[0].parentInventory=new();MachineTransactionApi.Stop();MachineTransactionApi.Start();}};result=Run(tx);Check(result.Status==MachineTransactionStatus.RecoveryFailed&&tx.Restores==0,"stop and restart inside callback cannot revive previous transaction scope");
Reset();MachineTransactionResult? nested=null;tx=new(){Write=p=>{nested=Run(new Tx());p[0].parentInventory=new();}};result=Run(tx);Check(result.Committed&&nested?.Status==MachineTransactionStatus.Reentrant&&Items.Made.Count==1,"nested batch rejected without a second factory call");
Reset();int notifications=0;tx=new();result=Run(tx,done:(_,_)=>{notifications++;throw new Exception("notification");});Check(result.Committed&&!result.MayRetry&&notifications==1&&tx.Restores==0&&result.CompletionError!.Contains("notification"),"postcommit notification exception never refunds or retries committed batch");
Reset();int work=0;tx=new(){VerifyResult=false};result=Run(tx,done:(_,_)=>work++);Check(!result.Committed&&work==0,"failed batch never emits successful work notification");
Reset();tx=new();result=Run(tx,done:(_,_)=>work++);Check(result.Committed&&work==1,"successful batch emits exactly one notification");
Reset();InventoryApi.IsItemDragActive=true;tx=new();result=Run(tx);Check(result.Status==MachineTransactionStatus.Rejected&&tx.Validations==0,"drag rejected before resource callbacks");
Reset();machine.Owned=false;tx=new();result=Run(tx);Check(result.Status==MachineTransactionStatus.Rejected&&tx.Validations==0,"unowned machine rejected");
Reset();machine.unitCount=2;tx=new();Check(Run(tx).Status==MachineTransactionStatus.Rejected,"stacked machine rejected");
Reset();tx=new();result=Run(tx,Array.Empty<MachineProductDefinition>());Check(result.Committed&&Items.Made.Count==0&&tx.Applies==1,"custom liquid-only transaction can commit without generated item products");
Reset();tx=new();result=Run(tx,new[]{new MachineProductDefinition("a",3)},context:Context(3));Check(result.Committed&&Items.Made.Count==3&&result.Created.Select(p=>p.InstanceId).Distinct().Count()==3,"multiple output units are independent single items");
foreach(var count in new[]{0,-1,65,int.MaxValue}){Reset();Bad(()=>Run(new(),new[]{new MachineProductDefinition("a",count)}),"invalid product count "+count);}
foreach(var id in new[]{""," ","x\n",new string('a',161)}){Reset();Bad(()=>Run(new(),new[]{new MachineProductDefinition(id)}),"invalid product ID "+JsonSerializer.Serialize(id));}
Reset();Bad(()=>Run(new(),new[]{new MachineProductDefinition("a",2)}),"count mismatch rejects before resource operations");
Reset();Bad(()=>Run(new(),Enumerable.Repeat(new MachineProductDefinition("a"),65).ToArray(),context:Context(65)),"product record quota enforced");
Reset();Bad(()=>Run(new(),new[]{new MachineProductDefinition("a",40),new MachineProductDefinition("b",40)},context:Context(80)),"aggregate product quota enforced");
Reset();Bad(()=>MachineTransactionApi.Run("bad.owner\n",Context(),one,new Tx()),"owner trailing newline rejected");
foreach(var owner in new[]{"","BAD","a..b","../x",new string('a',161)}) {
 Reset();Bad(()=>MachineTransactionApi.Run(owner,Context(),one,new Tx()),"invalid owner through real executor "+JsonSerializer.Serialize(owner));
}
Reset();GameItem? first=null;Items.Factory=id=>first??=new(){identifier=id};tx=new();result=Run(tx,new[]{new MachineProductDefinition("a",2)},context:Context(2));Check(result.Status==MachineTransactionStatus.Rejected&&first!.Destroys==1&&tx.Applies==0,"reused factory instance cleaned at most once");
Reset();Items.Factory=_=>machine;tx=new();result=Run(tx);Check(result.Status==MachineTransactionStatus.RecoveryFailed&&!machine.Destroyed&&tx.Faults==1,"borrowed machine returned by factory preserved");
Reset();var resource=new GameItem();Items.Factory=_=>resource;tx=new();result=Run(tx,context:new(machine,new[]{new MachineItemTake(resource,1)},new Dictionary<string,GameItem>(),1));Check(result.Status==MachineTransactionStatus.RecoveryFailed&&!resource.Destroyed,"borrowed input resource returned by factory preserved");
Reset();Items.Factory=id=>new(){identifier=id,OnDestroy=()=>throw new Exception("destroy")};tx=new(){Check=n=>n==1};result=Run(tx);Check(result.Status==MachineTransactionStatus.RecoveryFailed&&tx.Faults==1&&tx.Restores==0,"cleanup failure changes rejection to nonretryable fault");
Reset();tx=new(){VerifyResult=false,Recovery=()=>{machine.parentInventory=new();return true;}};result=Run(tx);Check(result.Status==MachineTransactionStatus.RecoveryFailed,"Restore claiming success after changing machine location is rejected");
Reset();tx=new();var threadError=Task.Run(()=>{try{Run(tx);return false;}catch(InvalidOperationException){return true;}}).GetAwaiter().GetResult();Check(threadError&&tx.Validations==0&&Items.Made.Count==0,"worker call rejected before callbacks or allocations");
Reset();Check(Run(new()).Committed,"fresh batch remains usable after all failure paths");
var report=new{passed=cases.Count,failed=0,cases,nativeRuntimeVerified=false,scope="real machine transaction executor and production DTOs; explicit native allocator, item and run stubs; no native resource behavior"};
File.WriteAllText("work/ezapi-machine-transaction-tests.json",JsonSerializer.Serialize(report,new JsonSerializerOptions{WriteIndented=true}));Console.WriteLine(JsonSerializer.Serialize(new{report.passed,report.failed}));
sealed class Tx:IMachineBatchTransaction {
 public int Validations,Applies,Verifications,Restores,Faults;public bool VerifyResult=true;public Func<int,bool>? Check;public Action<IReadOnlyList<GameItem>>? Write;public Func<bool>? Readback,Recovery;public Action? FaultAction;private IReadOnlyList<GameItem> _products=Array.Empty<GameItem>();
 public bool Validate(){Validations++;return Check?.Invoke(Validations)??true;}
 public void Apply(IReadOnlyList<GameItem> p){Applies++;_products=p;if(Write is not null)Write(p);else foreach(var i in p)i.parentInventory=new();}
 public bool Verify(){Verifications++;return Readback?.Invoke()??VerifyResult;}
 public bool Restore(){Restores++;if(Recovery is not null)return Recovery();foreach(var i in _products)i.parentInventory=null;return true;}
 public void Fault(string reason){Faults++;FaultAction?.Invoke();}
}
