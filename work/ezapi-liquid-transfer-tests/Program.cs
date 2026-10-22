using EZapi;
using Il2Cpp;
using System.Text.Json;

var results=new List<object>();var failures=new List<string>();
void Test(string name,Action body){Reset();try{body();results.Add(new{name,passed=true});}catch(Exception e){failures.Add(name+": "+e);results.Add(new{name,passed=false,error=e.ToString()});}}
void Assert(bool ok,string error="assertion"){if(!ok)throw new Exception(error);}
void Throws(Action action){try{action();}catch(ArgumentException){return;}catch(InvalidOperationException){return;}throw new Exception("expected exception");}
void Reset(){WaterHelper.Reset();InventoryFreezeApi.Reset();DebugLiquidTransfer.Clear();GameSession.Store.Items.Clear();TagSystem.AliasClone=false;TagState.AliasTagClone=false;GameSession.Identity=new(1,"test",1);InventoryApi.IsItemDragActive=false;Liquid.Liquids=new(){new(),new(){identifier="salt",currentPart="salt_volume"}};}
GameItem Bottle(int water=6000,int salt=3000,int capacity=10000){var i=new GameItem();i.state.dict["capacity"].Int=capacity;WaterHelper.Set(i,"water",water);WaterHelper.Set(i,"salt",salt);return i;}
int Total(GameItem i)=>WaterHelper.GetTotalVolume(i);
MachineLiquidSnapshot Snap(params MachineLiquidPart[] parts)=>new(10000,parts);
MachineLiquidSnapshot Annotation(GameItem i)=>new(Liquids.Capture(i).CapacityParts,Liquids.Capture(i).Contents.Select(p=>new MachineLiquidPart(p.Id,p.Parts){Value=p.Parts/100m,QualityBasis=p.Parts/200m}).ToArray());
void Annotate(GameItem i){Assert(Liquids.SetProductionMetadata(i,Annotation(i)).Committed);}
void Restored(LiquidTransferResult r,GameItem a,GameItem b,int av=9000,int bv=0){Assert(r.Status=="Restored"&&!r.Indeterminate,r.ToString());Assert(Total(a)==av&&Total(b)==bv);Assert(a.unitValue==10&&b.unitValue==10);Assert(!InventoryFreezeApi.IsFrozen(a)&&!InventoryFreezeApi.IsFrozen(b));}

Test("default native value parts/25000",()=>Assert(LiquidCompositionMath.BaseValue(new[]{new MachineLiquidPart("water",25000)})==1));
Test("explicit total never multiplied by parts",()=>Assert(LiquidCompositionMath.BaseValue(new[]{new MachineLiquidPart("water",5000){Value=17}})==17));
Test("empty composition value",()=>Assert(LiquidCompositionMath.BaseValue(Array.Empty<MachineLiquidPart>())==0));
Test("duplicate IDs rejected",()=>Throws(()=>LiquidCompositionMath.BaseValue(new[]{new MachineLiquidPart("water",1),new MachineLiquidPart("water",2)})));
Test("empty positive metadata rejected",()=>Throws(()=>Snap(new MachineLiquidPart("water",0){Value=1})));
Test("control ID rejected",()=>Throws(()=>Snap(new MachineLiquidPart("w\na",1))));
Test("negative value rejected",()=>Throws(()=>Snap(new MachineLiquidPart("water",1){Value=-1})));
Test("consume preserves integer totals",()=>{var s=Snap(new MachineLiquidPart("a",1),new MachineLiquidPart("b",1),new MachineLiquidPart("c",1));var r=LiquidCompositionMath.Consume(s,2);Assert(r.TotalParts==1&&r.Contents[2].Parts==1);Assert(s.TotalParts==3);});
Test("consume named component",()=>{var r=LiquidCompositionMath.Consume(Snap(new MachineLiquidPart("water",3),new MachineLiquidPart("salt",2)),2,"salt");Assert(r.Contents[0].Parts==3&&r.Contents[1].Parts==0);});
Test("consume missing component",()=>Throws(()=>LiquidCompositionMath.Consume(Snap(new MachineLiquidPart("water",1)),1,"missing")));
Test("overconsume rejected",()=>Throws(()=>LiquidCompositionMath.Consume(Snap(new MachineLiquidPart("water",1)),2)));
Test("zero consumption metadata unchanged",()=>{var s=Snap(new MachineLiquidPart("water",100){Value=decimal.MaxValue});Assert(LiquidMetadata.Same(s,LiquidCompositionMath.Consume(s,0)));});
Test("scale large decimal without intermediate overflow",()=>{var r=LiquidCompositionMath.Consume(Snap(new MachineLiquidPart("water",100){Value=decimal.MaxValue}),50);Assert(r.Contents[0].Value==decimal.MaxValue*.5m);});
Test("consume full clears totals",()=>{var r=LiquidCompositionMath.Consume(Snap(new MachineLiquidPart("water",10){Value=3,QualityBasis=4}),10);Assert(r.TotalParts==0&&r.Contents[0].Value==0&&r.Contents[0].QualityBasis==0);});
Test("consumed metadata differences",()=>{var s=Snap(new MachineLiquidPart("water",100){Value=10,QualityBasis=30});var c=LiquidCompositionMath.Consumed(s,LiquidCompositionMath.Consume(s,25));Assert(c.Single().Value==2.5m&&c.Single().QualityBasis==7.5m);});
Test("consumed requires same capacity",()=>Throws(()=>LiquidCompositionMath.Consumed(Snap(new MachineLiquidPart("water",1)),new(999,new[]{new MachineLiquidPart("water",0)}))));
Test("consumed cannot increase",()=>Throws(()=>LiquidCompositionMath.Consumed(Snap(new MachineLiquidPart("water",1)),Snap(new MachineLiquidPart("water",2)))));
Test("metadata cannot vanish in consumption",()=>Throws(()=>LiquidCompositionMath.Consumed(Snap(new MachineLiquidPart("water",2){Value=2}),Snap(new MachineLiquidPart("water",1)))));
Test("metadata cannot change without consumption",()=>Throws(()=>LiquidCompositionMath.Consumed(Snap(new MachineLiquidPart("water",2){Value=2}),Snap(new MachineLiquidPart("water",2){Value=1}))));
Test("replace merges values and quality fallback",()=>{var r=LiquidCompositionMath.ReplaceComponent(new[]{new MachineLiquidPart("a",100){Value=4,QualityBasis=8},new MachineLiquidPart("b",100)},"a","b");Assert(r.Count==1&&r[0].Parts==200&&r[0].Value==4.004m&&r[0].QualityBasis==8.004m);});
Test("replace source absent",()=>Throws(()=>LiquidCompositionMath.ReplaceComponent(new[]{new MachineLiquidPart("a",1)},"missing","b")));
Test("replace identical IDs rejected",()=>Throws(()=>LiquidCompositionMath.ReplaceComponent(new[]{new MachineLiquidPart("a",1)},"a","a")));
Test("add requires catalog entry",()=>Throws(()=>LiquidCompositionMath.Add(Snap(new MachineLiquidPart("water",1)),new[]{new MachineLiquidPart("salt",1)})));
Test("add respects capacity",()=>Throws(()=>LiquidCompositionMath.Add(new(1,new[]{new MachineLiquidPart("water",1)}),new[]{new MachineLiquidPart("water",1)})));
Test("add annotated totals",()=>{var s=LiquidCompositionMath.Add(Snap(new MachineLiquidPart("water",100){Value=4}),new[]{new MachineLiquidPart("water",100){Value=5}});Assert(s.TotalParts==200&&s.Contents[0].Value==9);});
Test("bounded pour preserves all components",()=>{var a=Bottle();var b=Bottle(0,0);var r=LiquidTransferApi.Pour(a,b,3000);Assert(r.Poured&&r.PartsMoved==3000&&Total(a)==6000&&Total(b)==3000);Assert(WaterHelper.Amount(b,"water_volume")==2000&&WaterHelper.Amount(b,"salt_volume")==1000);Assert(WaterHelper.Transfers==0&&WaterHelper.Removes==1);});
Test("bounded respects target free capacity",()=>{var a=Bottle();var b=Bottle(9000,0);var r=LiquidTransferApi.Pour(a,b,3000);Assert(r.Poured&&r.PartsMoved==1000&&Total(b)==10000);});
Test("bounded respects source total",()=>{var a=Bottle(3,0);var b=Bottle(0,0);Assert(LiquidTransferApi.Pour(a,b,9000).PartsMoved==3);});
Test("native overload invokes native exactly once",()=>{var a=Bottle();var b=Bottle(0,0);var r=LiquidTransferApi.Pour(a,b);Assert(r.Poured&&r.PartsMoved==9000&&WaterHelper.Transfers==1);});
Test("native thrown after proven transfer reports readback",()=>{var a=Bottle();var b=Bottle(0,0);WaterHelper.TransferHook=(s,t)=>{WaterHelper.RemoveDefault(s,9000);WaterHelper.Set(t,"water",6000);WaterHelper.Set(t,"salt",3000);throw new Exception("late");};var r=LiquidTransferApi.Pour(a,b);Assert(r.Status=="PouredReadback"&&r.Poured&&r.Error!.Contains("late"));});
Test("native no movement reports uncertain side effects",()=>{WaterHelper.TransferHook=(a,b)=>{};Assert(LiquidTransferApi.Pour(Bottle(),Bottle(0,0)).Indeterminate);});
Test("native partial loss never claims success",()=>{WaterHelper.TransferHook=(a,b)=>WaterHelper.RemoveDefault(a,100);var r=LiquidTransferApi.Pour(Bottle(),Bottle(0,0));Assert(r.Indeterminate&&!r.Poured);});
Test("native filtered composition conversion uncertain",()=>{WaterHelper.TransferHook=(a,b)=>{WaterHelper.RemoveDefault(a,9000);WaterHelper.Set(b,"water",9000);};Assert(LiquidTransferApi.Pour(Bottle(),Bottle(0,0)).Indeterminate);});
Test("empty source no native calls",()=>{Assert(LiquidTransferApi.Pour(Bottle(0,0),Bottle(0,0)).Status=="EmptyOrFull");Assert(WaterHelper.Transfers==0);});
Test("full target no native calls",()=>Assert(LiquidTransferApi.Pour(Bottle(),Bottle(10000,0)).Status=="EmptyOrFull"));
Test("same item rejected",()=>{var a=Bottle();Assert(LiquidTransferApi.Pour(a,a).Status=="Rejected");});
Test("non positive bound rejected",()=>Throws(()=>LiquidTransferApi.Pour(Bottle(),Bottle(),0)));
Test("not owned rejected",()=>{var a=Bottle();a.Owned=false;Assert(LiquidTransferApi.Pour(a,Bottle(0,0),1).Status=="Rejected");});
Test("source remove lock rejected",()=>{var a=Bottle();a.parentInventory!.RemoveLocked=true;Assert(LiquidTransferApi.Pour(a,Bottle(0,0)).Status=="Rejected");});
Test("target insert lock rejected",()=>{var b=Bottle(0,0);b.parentInventory!.InsertLocked=true;Assert(LiquidTransferApi.Pour(Bottle(),b).Status=="Rejected");});
Test("drag active rejected",()=>{InventoryApi.IsItemDragActive=true;Assert(LiquidTransferApi.Pour(Bottle(),Bottle(0,0)).Status=="Rejected");});
Test("stacks rejected",()=>{var a=Bottle();a.unitCount=2;Assert(LiquidTransferApi.Pour(a,Bottle(0,0)).Status=="Rejected");});
Test("unplaced target rejected",()=>{var b=Bottle();b.parentInventory=null;Assert(LiquidTransferApi.Pour(Bottle(),b).Status=="Rejected");});
Test("bounded source filter rejected",()=>{var a=Bottle();a.Filter=new();Assert(LiquidTransferApi.Pour(a,Bottle(0,0),1).Status=="Rejected"&&WaterHelper.Removes==0);});
Test("bounded target filter rejected",()=>{var b=Bottle(0,0);b.Filter=new();Assert(LiquidTransferApi.Pour(Bottle(),b,1).Status=="Rejected");});
Test("freeze hooks missing bounded unavailable",()=>{InventoryFreezeApi.IsAvailable=false;Assert(LiquidTransferApi.Pour(Bottle(),Bottle(0,0),1).Status=="Unavailable");});
Test("freeze reservation refused",()=>{InventoryFreezeApi.Refuse=true;Assert(LiquidTransferApi.Pour(Bottle(),Bottle(0,0),1).Status=="Unavailable");});
Test("external freeze rejected",()=>{var a=Bottle();using var l=InventoryFreezeApi.TryAcquire(new[]{a});Assert(LiquidTransferApi.Pour(a,Bottle(0,0)).Status=="Rejected");});
Test("aliased individual tag backup rejected before writing",()=>{TagState.AliasTagClone=true;Assert(LiquidTransferApi.Pour(Bottle(),Bottle(0,0),1).Status=="Rejected"&&WaterHelper.Removes==0);});
Test("invalid native allocation rejected",()=>{WaterHelper.SplitHook=(a,n)=>new[]{n+1,0};Assert(LiquidTransferApi.Pour(Bottle(),Bottle(0,0),1).Status=="Rejected"&&WaterHelper.Removes==0);});
Test("allocation wrong length rejected",()=>{WaterHelper.SplitHook=(a,n)=>new[]{n};Assert(LiquidTransferApi.Pour(Bottle(),Bottle(0,0),1).Status=="Rejected");});
Test("predicate false never writes",()=>{var r=LiquidTransferApi.Pour(Bottle(),Bottle(0,0),1,()=>false);Assert(r.Status=="Rejected"&&!r.Indeterminate&&WaterHelper.Removes==0);});
Test("predicate throws never writes",()=>{var r=LiquidTransferApi.Pour(Bottle(),Bottle(0,0),1,()=>throw new Exception("no"));Assert(r.Status=="Rejected"&&r.Error!.Contains("no")&&WaterHelper.Removes==0);});
Test("predicate mutation cannot be overwritten",()=>{var a=Bottle();var r=LiquidTransferApi.Pour(a,Bottle(0,0),1,()=>{WaterHelper.Set(a,"water",1);return true;});Assert(r.Indeterminate&&Total(a)==3001&&WaterHelper.Removes==0);});
Test("predicate can disable freeze before write",()=>{var r=LiquidTransferApi.Pour(Bottle(),Bottle(0,0),1,()=>{InventoryFreezeApi.IsAvailable=false;return true;});Assert(r.Status=="Rejected"&&WaterHelper.Removes==0);});
Test("partial source removal compensates",()=>{var a=Bottle();var b=Bottle(0,0);WaterHelper.RemoveHook=(s,n)=>{WaterHelper.RemoveDefault(s,1);throw new Exception("remove");};Restored(LiquidTransferApi.Pour(a,b,3000),a,b);});
Test("target partial add compensates both",()=>{var a=Bottle();var b=Bottle(0,0);WaterHelper.AddHook=(s,id,n)=>{WaterHelper.Set(s,id,n);throw new Exception("add");};Restored(LiquidTransferApi.Pour(a,b,3000),a,b);});
Test("source callback mutates target compensated too",()=>{var a=Bottle();var b=Bottle(0,0);WaterHelper.RemoveHook=(s,n)=>{WaterHelper.RemoveDefault(s,n);WaterHelper.Set(b,"salt",7);};Restored(LiquidTransferApi.Pour(a,b,3000),a,b);});
Test("predicate revoked after source write compensates",()=>{var a=Bottle();var b=Bottle(0,0);var calls=0;Restored(LiquidTransferApi.Pour(a,b,3000,()=>++calls<3),a,b);});
Test("predicate revoked after target write compensates",()=>{var a=Bottle();var b=Bottle(0,0);var calls=0;Restored(LiquidTransferApi.Pour(a,b,3000,()=>++calls<4),a,b);});
Test("run changed after write no old run restoration",()=>{var a=Bottle();var b=Bottle(0,0);WaterHelper.RemoveHook=(s,n)=>{WaterHelper.RemoveDefault(s,n);GameSession.Identity=new(1,"new",2);};var r=LiquidTransferApi.Pour(a,b,3000);Assert(r.Indeterminate&&r.Status=="RecoveryFailed"&&Total(a)==6000);});
Test("dict replacement refuses blind restore",()=>{var a=Bottle();var b=Bottle(0,0);WaterHelper.RemoveHook=(s,n)=>{WaterHelper.RemoveDefault(s,n);s.state=s.state.Clone();};Assert(LiquidTransferApi.Pour(a,b,3000).Indeterminate&&Total(a)==6000);});
Test("ownership lost refuses restore",()=>{var a=Bottle();var b=Bottle(0,0);WaterHelper.RemoveHook=(s,n)=>{WaterHelper.RemoveDefault(s,n);s.Owned=false;};Assert(LiquidTransferApi.Pour(a,b,3000).Indeterminate&&Total(a)==6000);});
Test("target restore throws still restores source",()=>{var a=Bottle();var b=Bottle(0,0);WaterHelper.AddHook=(s,id,n)=>{b.Sync=()=>throw new Exception("restore");throw new Exception("add");};var r=LiquidTransferApi.Pour(a,b,3000);Assert(r.Indeterminate&&Total(a)==9000&&a.unitValue==10);});
Test("swapped nested pour reentrant",()=>{var a=Bottle();var b=Bottle(0,0);LiquidTransferResult? nested=null;WaterHelper.RemoveHook=(s,n)=>{nested=LiquidTransferApi.Pour(b,a,1);WaterHelper.RemoveDefault(s,n);};Assert(LiquidTransferApi.Pour(a,b,3000).Poured&&nested!.Status=="Reentrant");});
Test("metadata writer saves explicit totals",()=>{var a=Bottle();Annotate(a);var r=Liquids.CaptureProduction(a);Assert(r.Contents.Sum(p=>p.Value)==90&&r.Contents.Sum(p=>p.QualityBasis)==45);Assert(a.state.dict.ContainsKey(LiquidMetadata.Tag));});
Test("metadata cannot change native quantity",()=>{var a=Bottle();var r=Liquids.SetProductionMetadata(a,Snap(new MachineLiquidPart("water",1),new MachineLiquidPart("salt",0)));Assert(r.Status==TransactionStatus.Rejected&&!a.state.dict.ContainsKey(LiquidMetadata.Tag));});
Test("bounded metadata conserved with existing target",()=>{var a=Bottle();var b=Bottle(500,0);Annotate(a);Annotate(b);var value=LiquidCompositionMath.BaseValue(Liquids.CaptureProduction(a).Contents)+LiquidCompositionMath.BaseValue(Liquids.CaptureProduction(b).Contents);Assert(LiquidTransferApi.Pour(a,b,3000).Poured);Assert(LiquidCompositionMath.BaseValue(Liquids.CaptureProduction(a).Contents)+LiquidCompositionMath.BaseValue(Liquids.CaptureProduction(b).Contents)==value);});
Test("native metadata conserved",()=>{var a=Bottle();var b=Bottle(0,0);Annotate(a);Assert(LiquidTransferApi.Pour(a,b).Poured);Assert(LiquidCompositionMath.BaseValue(Liquids.CaptureProduction(b).Contents)==90&&Liquids.CaptureProduction(a).Contents.Sum(p=>p.Value)==0);});
Test("metadata survives independent native state clone",()=>{var a=Bottle();Annotate(a);var expected=Liquids.CaptureProduction(a);a.state=a.state.Clone();Assert(LiquidMetadata.Same(expected,Liquids.CaptureProduction(a)));});
Test("metadata stale state never auto erased",()=>{var a=Bottle();Annotate(a);var old=a.state.dict[LiquidMetadata.Tag].valueString;WaterHelper.Set(a,"water",1);Throws(()=>Liquids.CaptureProduction(a));Assert(a.state.dict[LiquidMetadata.Tag].valueString==old);});
Test("metadata corrupt prevents native transfer",()=>{var a=Bottle();a.state.dict[LiquidMetadata.Tag]=new TagState("",""){valueString="{}"};Assert(LiquidTransferApi.Pour(a,Bottle(0,0)).Status=="Rejected"&&WaterHelper.Transfers==0);});
Test("metadata duplicate fields rejected",()=>{var a=Bottle();Annotate(a);var tag=a.state.dict[LiquidMetadata.Tag];tag.valueString=tag.valueString.Replace("\"Version\":1","\"Version\":1,\"Version\":1");Throws(()=>Liquids.CaptureProduction(a));});
Test("metadata unknown component fields rejected",()=>{var a=Bottle();Annotate(a);a.state.dict[LiquidMetadata.Tag].valueString=a.state.dict[LiquidMetadata.Tag].valueString.Replace("\"LiquidId\":", "\"typo\":1,\"LiquidId\":");Throws(()=>Liquids.CaptureProduction(a));});
Test("metadata sync failure restores originals",()=>{var a=Bottle();var once=true;a.Sync=()=>{if(once){once=false;throw new Exception("sync");}};var r=Liquids.SetProductionMetadata(a,Annotation(a));Assert(r.Status==TransactionStatus.Restored&&!a.state.dict.ContainsKey(LiquidMetadata.Tag)&&a.unitValue==10);});
Test("metadata cleared explicitly without quantity writes",()=>{var a=Bottle();Annotate(a);Assert(Liquids.SetProductionMetadata(a,MachineLiquidSnapshot.From(Liquids.Capture(a))).Committed&&!a.state.dict.ContainsKey(LiquidMetadata.Tag)&&Total(a)==9000);});
Test("metadata callback nested pour rejected",()=>{var a=Bottle();var b=Bottle(0,0);LiquidTransferResult? nested=null;a.Sync=()=>nested=LiquidTransferApi.Pour(a,b);Annotate(a);Assert(nested!.Status=="Reentrant");});
Test("off thread native call rejected",()=>{var a=Bottle();var b=Bottle(0,0);var rejected=Task.Run(()=>{try{LiquidTransferApi.Pour(a,b);return false;}catch(InvalidOperationException){return true;}}).Result;Assert(rejected);});
Test("missing run rejected",()=>{GameSession.Identity=null;Throws(()=>LiquidTransferApi.Pour(Bottle(),Bottle()));});

Test("saved metadata order follows native catalog for rounding",()=>{var a=Bottle(1,1);var b=Bottle(0,0);var reverse=new MachineLiquidSnapshot(10000,Annotation(a).Contents.Reverse().ToArray());Assert(Liquids.SetProductionMetadata(a,reverse).Committed);Assert(Liquids.CaptureProduction(a).Contents[0].LiquidId=="water");Assert(LiquidTransferApi.Pour(a,b,1).Poured);});
Test("non liquid container cannot reach transfer",()=>{var a=Bottle();a.Container=false;Assert(LiquidTransferApi.Pour(a,Bottle(0,0),1).Status=="Rejected"&&WaterHelper.Removes==0);});
Test("metadata sync failure after source removal compensated",()=>{var a=Bottle();var b=Bottle(0,0);Annotate(a);var snapshot=Liquids.CaptureProduction(a);var once=true;a.Sync=()=>{if(once){once=false;throw new Exception("meta sync");}};Restored(LiquidTransferApi.Pour(a,b,3000),a,b);Assert(LiquidMetadata.Same(snapshot,Liquids.CaptureProduction(a)));});
Test("native error before movement is indeterminate",()=>{WaterHelper.TransferHook=(a,b)=>throw new Exception("native error");var r=LiquidTransferApi.Pour(Bottle(),Bottle(0,0));Assert(r.Indeterminate&&!r.Poured);});
Test("lease hook switches run before mutation",()=>{InventoryFreezeApi.AcquireHook=()=>GameSession.Identity=new(1,"other",2);var r=LiquidTransferApi.Pour(Bottle(),Bottle(0,0),100);Assert(r.Status=="Rejected"&&WaterHelper.Removes==0);});
Test("debug preview does not write; execute moves maximum1ml",()=>{var a=Bottle();var b=Bottle(0,0);GameSession.Store.Items.AddRange(new[]{a,b});Assert(DebugLiquidTransfer.Preview(GameSession.Identity).Contains("#"));Assert(Total(a)==9000&&WaterHelper.Removes==0);Assert(DebugLiquidTransfer.Execute(GameSession.Identity).Contains("Poured")&&Total(b)==1000);});
Test("debug execution without preview refuses",()=>Assert(DebugLiquidTransfer.Execute(GameSession.Identity).Contains("失效")));
Test("debug preview consumed once",()=>{GameSession.Store.Items.AddRange(new[]{Bottle(),Bottle(0,0)});DebugLiquidTransfer.Preview(GameSession.Identity);DebugLiquidTransfer.Execute(GameSession.Identity);Assert(DebugLiquidTransfer.Execute(GameSession.Identity).Contains("失效")&&WaterHelper.Removes==1);});
Test("debug stale contents refuse write",()=>{var a=Bottle();GameSession.Store.Items.AddRange(new[]{a,Bottle(0,0)});DebugLiquidTransfer.Preview(GameSession.Identity);WaterHelper.Set(a,"water",5999);Assert(DebugLiquidTransfer.Execute(GameSession.Identity).Contains("变化")&&WaterHelper.Removes==0);});
Test("debug scene clears preview",()=>{GameSession.Store.Items.AddRange(new[]{Bottle(),Bottle(0,0)});DebugLiquidTransfer.Preview(GameSession.Identity);DebugLiquidTransfer.Clear();Assert(DebugLiquidTransfer.Execute(GameSession.Identity).Contains("失效"));});
Test("debug changed run refuses preview execution",()=>{GameSession.Store.Items.AddRange(new[]{Bottle(),Bottle(0,0)});DebugLiquidTransfer.Preview(GameSession.Identity);GameSession.Identity=new(1,"next",2);Assert(DebugLiquidTransfer.Execute(GameSession.Identity).Contains("失效")&&WaterHelper.Removes==0);});
Test("debug ignores duplicate pointers and filter",()=>{var a=Bottle();a.Filter=new();var b=Bottle();var c=Bottle(0,0);GameSession.Store.Items.AddRange(new[]{a,b,b,c});Assert(DebugLiquidTransfer.Preview(GameSession.Identity).Contains("#"));Assert(DebugLiquidTransfer.Execute(GameSession.Identity).Contains("Poured")&&Total(a)==9000&&Total(b)==8000);});
Test("debug drag prevents preview",()=>{GameSession.Store.Items.AddRange(new[]{Bottle(),Bottle(0,0)});InventoryApi.IsItemDragActive=true;Assert(DebugLiquidTransfer.Preview(GameSession.Identity).Contains("拖动"));});
Test("debug missing target requires two bottles",()=>{GameSession.Store.Items.Add(Bottle());Assert(DebugLiquidTransfer.Preview(GameSession.Identity).Contains("两件"));});
Test("explicit metadata refresh replaces stale annotation",()=>{var a=Bottle();Annotate(a);WaterHelper.Set(a,"water",5999);Assert(Liquids.SetProductionMetadata(a,Annotation(a)).Committed);Assert(Liquids.CaptureProduction(a).TotalParts==8999);});
Test("explicit metadata refresh replaces corrupt annotation",()=>{var a=Bottle();a.state.dict[LiquidMetadata.Tag]=new TagState("",""){valueString="broken"};Assert(Liquids.SetProductionMetadata(a,Annotation(a)).Committed);Assert(Liquids.CaptureProduction(a).TotalParts==9000);});
Test("failed explicit refresh restores corrupt original verbatim",()=>{var a=Bottle();a.state.dict[LiquidMetadata.Tag]=new TagState("",""){valueString="broken"};var once=true;a.Sync=()=>{if(once){once=false;throw new Exception("sync");}};Assert(Liquids.SetProductionMetadata(a,Annotation(a)).Status==TransactionStatus.Restored);Assert(a.state.dict[LiquidMetadata.Tag].valueString=="broken"&&Total(a)==9000);});
Test("shallow system clone cannot corrupt rollback of in-place liquid tags",()=>{
 var a=Bottle();var b=Bottle(0,0);TagSystem.AliasClone=true;
 WaterHelper.RemoveHook=(item,n)=>{foreach(var liquid in Liquid.Liquids!){var tag=item.state.dict.GetValueOrDefault(liquid.currentPart);if(tag is not null)tag.Int=0;}throw new Exception("partial in-place native mutation");};
 var r=LiquidTransferApi.Pour(a,b,1000);Assert(r.Status=="Restored"&&!r.Indeterminate&&Total(a)==9000&&Total(b)==0);
});
// Real NativeTagBackup equality/deep restore across the full current native tag value surface.
Test("tag journal compares and restores every scalar field",()=>{
 var state=new TagSystem();var tag=new TagState("",""){Int=42,Enabled=true,valueString="visible",internalValueString="raw",valueFloat=1.25f,valueDouble=2.5,valueLong=1234567890123,valueBool=true,identifier="tag",identifierName="label"};state.dict["tag"]=tag;
 var backup=NativeTagBackup.Capture(state);Assert(backup.Matches(state));
 Action<TagState>[] mutations={t=>t.Enabled=false,t=>t.Int++,t=>t.valueString="other",t=>t.internalValueString="other",t=>t.valueFloat++,t=>t.valueDouble++,t=>t.valueLong++,t=>t.valueBool=false,t=>t.identifier="other",t=>t.identifierName="other"};
 foreach(var mutate in mutations){mutate(state.dict["tag"]);Assert(!backup.Matches(state));backup.Restore(state);Assert(backup.Matches(state));}
});
Test("tag journal rejects extra and missing tags",()=>{var state=new TagSystem();state.dict["a"]=new("","");var backup=NativeTagBackup.Capture(state);state.dict["b"]=new("","");Assert(!backup.Matches(state));backup.Restore(state);state.dict.Clear();Assert(!backup.Matches(state));backup.Restore(state);Assert(backup.Matches(state));});
Test("tag journal equality handles cloned NaN values",()=>{var state=new TagSystem();state.dict["a"]=new("",""){valueDouble=double.NaN,valueFloat=float.NaN};var backup=NativeTagBackup.Capture(state);Assert(backup.Matches(state));backup.Restore(state);Assert(backup.Matches(state));});
var report=new{suite="liquid-transfer",passed=results.Count-failures.Count,failed=failures.Count,nativeRuntimeVerified=false,cases=results};
Directory.CreateDirectory("work");File.WriteAllText("work/ezapi-liquid-transfer-tests.json",JsonSerializer.Serialize(report,new JsonSerializerOptions{WriteIndented=true}));
Console.WriteLine(JsonSerializer.Serialize(new{report.passed,report.failed,failures}));return failures.Count==0?0:1;





