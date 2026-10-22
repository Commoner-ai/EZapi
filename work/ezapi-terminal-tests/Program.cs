using EZapi;
using System.Text.Json;
var checks=new List<object>();int failed=0;
void Check(bool ok,string name){checks.Add(new{name,passed=ok});if(!ok){failed++;Console.Error.WriteLine("FAIL: "+name);}}
MachineRecipe Recipe(string owner,string name="recipe")=>new(owner+"."+name,new[]{new MachineIngredient("INPUT")},new("OUTPUT"));
void Reset(){MachineRegistrationApi.Stop();RecipeGuideApi.Stop();NativeFactoryRegistry.Items.Clear();MachineRecipeApi.Catalog.Clear();MachineRecipeApi.Commits=0;MachineRecipeApi.LastMarkup=0;NativeFactoryRegistry.Reject=false;InventoryAdmissionApi.Reject=false;MachineRecipeApi.Reject=null;MachineRecipeApi.CanEdit=true;ItemRegistry.Error=null;ContentApi.Observer=null;RecipeGuideApi.Start();MachineRegistrationApi.Start();}
void Register(string stage="initial"){var result=MachineRegistrationApi.RegisterMachine(ManufacturingTerminal.OwnerId,new(ManufacturingTerminal.ItemId,new(){Battery=new(new(2,2))},Array.Empty<MachineRecipe>()){Power=new(25),ProductionMarkupPercent=15});Check(result.Accepted,"fresh shared terminal registered: "+stage);}
Reset();Check(!ManufacturingTerminal.Snapshot().Registered,"snapshot reports unregistered terminal");
Check(!MachineRegistrationApi.ConfigureManufacturingTerminalMarkup("content.mod",25).Accepted,"unavailable terminal rejects configuration");
Check(ManufacturingTerminal.Snapshot().MarkupOwner is null,"unavailable configuration does not claim owner");
Register();var baseline=ManufacturingTerminal.Snapshot();
foreach(var owner in new[]{""," ","content.mod\n","content.mod ","Content.Mod","content..mod","x."}) {
 Check(!MachineRegistrationApi.ConfigureManufacturingTerminalMarkup(owner,15).Accepted,"invalid owner rejected: "+JsonSerializer.Serialize(owner));
 Check(ManufacturingTerminal.Snapshot()==baseline,"invalid owner preserves complete settings: "+JsonSerializer.Serialize(owner));
}
foreach(var percent in new[]{-1,int.MinValue,1001,int.MaxValue}){
 Check(!MachineRegistrationApi.ConfigureManufacturingTerminalMarkup("content.mod",percent).Accepted,"out of bounds percent rejected: "+percent);
 Check(ManufacturingTerminal.Snapshot()==baseline,"invalid percent cannot claim settings: "+percent);
}
var first=new[]{Recipe("content.mod")};var second=new[]{Recipe("addon.mod")};
Check(MachineRegistrationApi.RegisterAdditionalRecipes("content.mod",ManufacturingTerminal.ItemId,first).Accepted,"first contribution accepted");
Check(MachineRegistrationApi.RegisterAdditionalRecipes("addon.mod",ManufacturingTerminal.ItemId,second).Accepted,"second contribution accepted");
var before=MachineRegistrationApi.RegisteredRecipes(ManufacturingTerminal.ItemId).ToArray();var guides=MachineRegistrationApi.GuideSnapshot().Recipes.ToArray();
var admission=InventoryAdmissionApi.Active;
var commits=MachineRecipeApi.Commits;
Check(MachineRegistrationApi.ConfigureManufacturingTerminalMarkup("content.mod",15).Accepted,"default value claims configuration");
Check(ManufacturingTerminal.Snapshot().MarkupOwner=="content.mod","default submission claims owner");
Check(MachineRecipeApi.Commits==commits,"identical config does not rewrite executable catalog");
foreach(var percent in new[]{0,15,25,1000}) {
 Check(!MachineRegistrationApi.ConfigureManufacturingTerminalMarkup("addon.mod",percent).Accepted,"different author cannot change percent: "+percent);
 Check(ManufacturingTerminal.Snapshot().ProductionMarkupPercent==15,"conflict preserves actual percent: "+percent);
}
foreach(var percent in new[]{0,25,1000,15}) {
 Check(MachineRegistrationApi.ConfigureManufacturingTerminalMarkup("content.mod",percent).Accepted,"same author update accepted: "+percent);
 Check(ManufacturingTerminal.Snapshot().ProductionMarkupPercent==percent&&MachineRecipeApi.LastMarkup==percent,"updated config reaches actual executable catalog: "+percent);
 Check(MachineRegistrationApi.RegisteredRecipes(ManufacturingTerminal.ItemId).Select(r=>r.OwnerId).SequenceEqual(before.Select(r=>r.OwnerId)),"update preserves contributor identity/order: "+percent);
 Check(MachineRecipeApi.Catalog[ManufacturingTerminal.ItemId].Select(r=>r.Recipe.RecipeId).SequenceEqual(before.Select(r=>r.Recipe.RecipeId)),"update preserves actual executable recipe identities: "+percent);
 Check(MachineRegistrationApi.GuideSnapshot().Recipes.SequenceEqual(guides),"update preserves exact automatic guides: "+percent);
 Check(InventoryAdmissionApi.Active==admission,"update does not replace or leak input admission: "+percent);
 Check(MachineRecipeApi.LastPower?.Cost==25,"update preserves machine power rule: "+percent);
}
Check(MachineRegistrationApi.RegisterAdditionalRecipes("third.mod",ManufacturingTerminal.ItemId,new[]{Recipe("third.mod")}).Accepted,"contribution after markup update accepted");
Check(MachineRecipeApi.LastMarkup==15&&ManufacturingTerminal.Snapshot().RecipeCount==3,"additional recipes retain configured markup");
var state=ManufacturingTerminal.Snapshot();MachineRecipeApi.CanEdit=false;
Check(!MachineRegistrationApi.ConfigureManufacturingTerminalMarkup("content.mod",25).Accepted,"live processing rejects configuration");
Check(ManufacturingTerminal.Snapshot()==state,"reentrant rejection preserves config");MachineRecipeApi.CanEdit=true;
Check(Task.Run(()=>{try{MachineRegistrationApi.ConfigureManufacturingTerminalMarkup("content.mod",25);return false;}catch(InvalidOperationException){return true;}}).GetAwaiter().GetResult(),"wrong thread rejected");
MachineRegistrationApi.Stop();Check(!MachineRegistrationApi.ConfigureManufacturingTerminalMarkup("content.mod",25).Accepted,"stopped config rejects");
Check(!ManufacturingTerminal.Snapshot().Registered&&ManufacturingTerminal.Snapshot().MarkupOwner is null,"stop clears declaration and configuration owner");
Reset();Register("new-generation");Check(MachineRegistrationApi.ConfigureManufacturingTerminalMarkup("addon.mod",0).Accepted,"new generation allows different first owner");
Check(ManufacturingTerminal.Snapshot().MarkupOwner=="addon.mod"&&MachineRecipeApi.LastMarkup==0,"new owner and zero apply after restart");
Reset();var report=new{passed=checks.Count-failed,failed,cases=checks,nativeRuntimeVerified=false,scope="real terminal configuration, unified registration, automatic guide and generation contracts; explicit native/executable-catalog stubs; real energy/product price verified separately in game"};
File.WriteAllText("work/ezapi-terminal-tests.json",JsonSerializer.Serialize(report,new JsonSerializerOptions{WriteIndented=true}));Console.WriteLine(JsonSerializer.Serialize(report));if(failed>0)Environment.Exit(1);
