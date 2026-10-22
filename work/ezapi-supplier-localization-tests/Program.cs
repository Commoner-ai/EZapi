using EZapi;using Il2Cpp;using System.Reflection;using System.Text.Json;
var cases=new List<object>();var failed=0;var purchases=0;var captures=0;var dismisses=0;DialogueContext? purchaseContext=null,captureContext=null;
void Check(bool ok,string name){cases.Add(new{name,passed=ok});if(!ok){failed++;Console.Error.WriteLine(name);}}
void Fresh(){
 SupplierApi.Stop();SupplierApi.Start();DialogUIManager.Instance=new();DirectoryMaster.Created.Clear();DirectoryMaster.OnCreate=null;MelonLoader.MelonLogger.Warnings.Clear();
 NativeItemLocalization.Locale="zh";NativeItemLocalization.Reads=0;GameSession.OnThread=true;GameSession.Identity=new(1,"run",1);InventoryApi.IsItemDragActive=false;
 foreach(var p in new (string,object?)[]{("_graph",null),("_fault",false),("_busy",false),("_permit",null),("_selectDepth",0)})typeof(DialogueApi).GetField(p.Item1,BindingFlags.NonPublic|BindingFlags.Static)!.SetValue(null,p.Item2);
 DialogueApi.Start();DialogueApi.DirectoryReady();purchases=captures=dismisses=0;purchaseContext=captureContext=null;
}
DialogUIManager M()=>DialogUIManager.Instance!;
void Select(int i){M().SelectChoice(i);M().currentDialog=M().currentDialog?.nextDialogue;}
SupplierDefinition Definition(bool localized=true,bool throwPurchase=false,bool failRefresh=false)=>new("author.supplier","原访客",c=>{
 captures++;captureContext=c;if(failRefresh&&captures>1)throw new Exception("capture failure");
 return new SupplierPage("原正文",new[]{new SupplierOffer("author.offer","原选项","原预览",p=>{purchases++;purchaseContext=p;if(throwPurchase)throw new Exception("purchase failure");return "Reply";}){
  LocalizedLabel=localized?new("商品","Product"):null,LocalizedPreview=localized?new("预览","Preview"):null,LocalizedConfirmLabel=localized?new("确认购买","Accept purchase"):null
 }}){LocalizedBody=localized?new("今日报价","Today's offers"):null};
},_=>dismisses++){LocalizedSpeaker=localized?new("供应商","Supplier"):null,LocalizedFailureText=localized?new("购买失败","Purchase failed"):null};
PresentationResult Queue(SupplierDefinition definition)=>SupplierApi.Queue("author","author.visit","run",1,"author.actor","author.sprite",definition);
Fresh();var definition=Definition(throwPurchase:true);Check(Queue(definition).Status==PresentationStatus.Queued,"real supplier captures and queues menu");
Check(captureContext?.LocaleCode=="zh"&&captures==1,"capture context uses capture language without freezing future graph locale");NativeItemLocalization.Locale="en";
var client=ClientVisitApi.Factory!();Check(client.displayName=="Supplier"&&client.clientIntent==StoreClient.ClientIntent.DIALOGUE&&client.isRegularSellDisable,"supplier client factory resolves actual creation language and keeps dialogue-only intent");
var beforeReads=NativeItemLocalization.Reads;ClientVisitApi.Arrive();Check(M().currentDialog?.title=="Supplier"&&M().currentDialog?.plainText=="Today's offers"&&NativeItemLocalization.Reads==beforeReads+1,"real supplier script reaches English native root once");
Check(M().currentDialog?.choices[0].Item2.plainText=="Product"&&M().currentDialog?.choices[1].Item2.plainText=="Leave","offer and leave text flow through supplier native graph");
NativeItemLocalization.Locale="zh";Select(0);Check(M().currentDialog?.plainText=="Preview"&&M().currentDialog?.choices[0].Item2.plainText=="Accept purchase"&&M().currentDialog?.choices[1].Item2.plainText=="Back","offer-specific confirmation and preview use graph locale");
var stale=M().currentDialog!.choices[0].Item3!;Select(0);stale.Invoke();
Check(purchases==1&&M().currentDialog?.plainText=="Purchase failed"&&NativeItemLocalization.Reads==beforeReads+1&&purchaseContext?.LocaleCode=="en","purchase failure and context keep opening locale after switch without replay");
Check(M().currentDialog?.choices[0].Item2.plainText=="Continue trading"&&M().currentDialog?.choices[1].Item2.plainText=="Leave","result page carries supplier-specific English defaults");
Check(MelonLoader.MelonLogger.Warnings.Count==1,"purchase failure diagnosed once by dialogue execution boundary");
Select(0);SupplierApi.Maintain();
Check(captures==2&&M().currentDialog?.plainText=="今日报价"&&M().currentDialog?.title=="供应商","deferred continue captures fresh menu and new locale");
Check(M().currentDialog?.choices[1].Item2.plainText=="离开"&&definition.Speaker=="原访客"&&definition.ConfirmLabel=="购买","refresh preserves original declaration and Chinese defaults");
Select(1);SupplierApi.Maintain();SupplierApi.Maintain();Check(dismisses==1,"localized leave still dismisses business once");DialogueApi.Close("author","author.visit");
Fresh();Queue(Definition(throwPurchase:true,failRefresh:true));NativeItemLocalization.Locale="en";ClientVisitApi.Arrive();Select(0);Select(0);Select(0);SupplierApi.Maintain();
Check(captures==2&&M().currentDialog?.plainText=="Purchase failed"&&M().currentDialog?.title=="Supplier","capture failure opens localized failure body and speaker");
Check(M().currentDialog?.choices.Count==1&&M().currentDialog?.choices[0].Item2.plainText=="Leave","capture failure offers localized safe leave only");DialogueApi.Close("author","author.visit");
Fresh();Queue(Definition(localized:false,throwPurchase:true));NativeItemLocalization.Locale="en";ClientVisitApi.Arrive();Select(0);
Check(M().currentDialog?.choices[0].Item2.plainText=="Buy","plain supplier defaults are English without overriding author prose");Select(0);
Check(M().currentDialog?.plainText=="The supplier action did not complete. Check the log.","plain supplier failure default is English");DialogueApi.Close("author","author.visit");
Fresh();Queue(Definition()with{AllowMenuRefresh=false,ConfirmLabel="Custom buy",BackLabel="Custom back",LeaveLabel="Custom leave",FailureText="Custom fail",LocalizedFailureText=null});NativeItemLocalization.Locale="en";ClientVisitApi.Arrive();Select(0);Select(0);
Check(M().currentDialog?.choices.Count==1&&M().currentDialog?.choices[0].Item2.plainText=="Custom leave","custom literals preserved and refresh disabled remains effective");DialogueApi.Close("author","author.visit");
Console.WriteLine(JsonSerializer.Serialize(new{suite="supplier-localization-runtime",passed=cases.Count-failed,failed,cases,nativeRuntimeVerified=false,scope="linked production SupplierApi/Contract, DialogueApi/graph/plan/execution; visit scheduler and game wrappers substituted; physical game evidence separate"},new JsonSerializerOptions{WriteIndented=true}));return failed==0?0:1;
