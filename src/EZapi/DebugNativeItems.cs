using Il2Cpp;
using MelonLoader;
namespace EZapi;

/// <summary>明确的开发测试物品；只登记工厂，点击按钮才创建并销毁临时实例，不交付或保存。</summary>
internal static class DebugNativeItems
{
    private const string Owner="ezapi.debug";
    private static bool _rejectNext;
    internal static object? LastReport { get; private set; }
    internal static void Clear()=>LastReport=null;
    internal static void Register()
    {
        foreach(var suffix in new[]{"factory_item","factory_amenity","factory_node","factory_module"}) {
            var d=Definition(suffix,node:suffix=="factory_node",module:suffix=="factory_module");
            LegacySpriteAdapter.Commit(d,LegacySpriteAdapter.Prepare(d,null));
        }
        var options=new NativeItemOptions("EZapi工厂检查","工厂固定短说明","工厂风味文本","EZapi开发检查");
        void Submit(ContentSubmissionResult result){if(!result.Accepted)throw new InvalidOperationException(result.Reason);}
        Submit(ItemApi.RegisterItem(Owner,Owner+".factory_item",()=>Create("factory_item"),options));
        Submit(ItemApi.RegisterAmenity(Owner,Owner+".factory_amenity",()=>Create("factory_amenity"),options));
        Submit(ItemApi.RegisterNode(Owner,Owner+".factory_node",()=>Create("factory_node",node:true),options));
        Submit(ItemApi.RegisterModules(Owner,new[]{new NativeModuleRegistration(Owner+".factory_module",()=>Create("factory_module",module:true),options)}));
        // 真正走严格JSON→整批声明→官方目录→官方helper，不把直接helper调用当作JSON验证。
        using var stream=new MemoryStream(System.Text.Encoding.UTF8.GetBytes("        [{\"id\":\"ezapi.debug.json_module\",\"name\":\"EZapi JSON模块\",\"module\":{\"type\":\"ezapi.debug.probe_type\",\"performance\":10,\"efficiency\":5,\"quality\":3,\"isShipModule\":false,\"tier\":2},\"tooltip\":{\"sourceName\":\"EZapi开发检查\"}},\n         {\"id\":\"ezapi.debug.json_node\",\"name\":\"EZapi JSON节点\",\"node\":true,\"tooltip\":{\"sourceName\":\"EZapi开发检查\"}}]"));
        var results=JsonItems.Register(stream);
        if(results.Any(r=>!r.Accepted))throw new InvalidOperationException(results.First(r=>!r.Accepted).Message);
    }
    private static ItemDefinition Definition(string suffix,bool node=false,bool module=false)=>new() {
        Id=Owner+"."+suffix,Name="EZapi检查原名",Node=node,
        Module=module?new(){Type="ezapi.debug.probe_type",Performance=10,Efficiency=5,Quality=3,IsShipModule=false,Tier=2}:null
    };
    private static GameItem Create(string suffix,bool node=false,bool module=false)
    {
        if(suffix=="factory_item"&&_rejectNext){_rejectNext=false;return null!;}
        return NativeItemFactory.Create(Definition(suffix,node,module));
    }
    internal static string Run()
    {
        if(InventoryApi.IsItemDragActive)return "请先放下拖拽物品。";
        var checks=new List<object>();int passed=0,failed=0;
        void Check(bool ok,string name){checks.Add(new{name,passed=ok});if(ok)passed++;else failed++;}
        try {
            NativeAccess.RequireRun();
            var beforeFailures=ItemApi.FactorySnapshot().ToDictionary(v=>v.ItemId,v=>v.Failures,StringComparer.Ordinal);
            GameItem? placeholder=null;bool rejected=false;
            try {
                _rejectNext=true;
                placeholder=ItemApi.Create(Owner+".factory_item",false);
            }catch(InvalidOperationException){rejected=true;}
            finally{_rejectNext=false;placeholder?.Destroy();}
            Check(rejected,"failed native factory placeholder is rejected by public Create");
            foreach(var suffix in new[]{"factory_item","factory_amenity","factory_node","factory_module","json_module","json_node"}) {
                GameItem? first=null,second=null;
                try {
                    var id=Owner+"."+suffix;
                    Check(ItemApi.Snapshot().Any(v=>v.ContentId==id&&v.Status==NativeApplicationStatus.Applied),suffix+": directory Applied");
                    Check(ItemApi.Exists(id),suffix+": native directory lookup");
                    first=ItemApi.Create(id,false);second=ItemApi.Create(id,false);
                    Check(first.identifier==id&&second.identifier==id&&first.Pointer!=second.Pointer&&first.uniqueId!=second.uniqueId&&first.state.Pointer!=second.state.Pointer,suffix+": independent factory instances");
                    Check(first.parentInventory is null&&second.parentInventory is null&&!GeneralHelper.IsItemOwned(first),suffix+": detached unowned instances");
                    if(suffix.EndsWith("module",StringComparison.Ordinal)) {
                        Check(first.IsGameItemType("MODULE")&&first.GetTagReadonly("MODULE_TYPE")?.valueString=="ezapi.debug.probe_type",suffix+": official module initialization");
                    }
                    if(suffix.EndsWith("node",StringComparison.Ordinal))Check(first.IsGameItemType("NODE"),suffix+": official node initialization");
                    if(suffix.EndsWith("module",StringComparison.Ordinal)||suffix.EndsWith("node",StringComparison.Ordinal)) {
                        var control=ItemDirectory.CreateEmptyItem(id);
                        try {
                            control.SetShape(new GridShapeBuilder(1,1).SetDataFill(1).Build());
                            if(suffix.EndsWith("module",StringComparison.Ordinal))ModuleHelper.InitModuleItem(control,"ezapi.debug.probe_type",10,5,3,false,"",2);
                            else ModuleEffectHelper.InitNode(control);
                            control.SyncModifiedState();
                            bool same=control.state.dict.Count>0;
                            foreach(var pair in control.state.dict) {
                                if(!first.state.dict.ContainsKey(pair.Key)){same=false;continue;}
                                var a=pair.Value;var b=first.state.dict[pair.Key];
                                same&=a.IsEnabled()==b.IsEnabled()&&a.GetString()==b.GetString()&&a.GetInt()==b.GetInt()&&a.valueLong==b.valueLong&&
                                    a.GetFloat()==b.GetFloat()&&a.GetDouble()==b.GetDouble()&&a.GetBool()==b.GetBool();
                            }
                            Check(same,suffix+": all official initializer tags match direct control");
                            Check(first.forceDisableUse==control.forceDisableUse&&first.forceDisableActivate==control.forceDisableActivate&&first.activateDefault==control.activateDefault&&first.canUseOutsideCombat==control.canUseOutsideCombat,
                                suffix+": official helper flags preserved");
                        }finally{control.Destroy();}
                    }
                    if(suffix.StartsWith("factory",StringComparison.Ordinal))Check(first.name=="EZapi工厂检查"&&first.shortDescription=="工厂固定短说明"&&first.flavorText=="工厂风味文本",suffix+": fixed text options");
                    var text=ItemInspection.BuildTooltip(first).nakedText;
                    Check(text.Contains("来源：EZapi开发检查",StringComparison.Ordinal),suffix+": own source tooltip");
                }finally{first?.Destroy();second?.Destroy();}
            }
            Check(ItemApi.FactorySnapshot().Where(v=>new[]{Owner+".factory_item",Owner+".factory_amenity",Owner+".factory_node",Owner+".factory_module"}.Contains(v.ItemId,StringComparer.Ordinal)).All(v=>v.Created>=2&&v.Failures==beforeFailures[v.ItemId]+(v.ItemId.EndsWith(".factory_item",StringComparison.Ordinal)?1:0)&&v.LastError is null&&!v.Creating),
                "factory recovery clears last error and preserves expected failure count");
        }catch(Exception ex){Check(false,"execution: "+ex);MelonLogger.Error("[EZapi] Native factory probe: "+ex);}
        LastReport=new {at=DateTimeOffset.Now,passed,failed,checks,factories=ItemApi.FactorySnapshot(),
            scope="six temporary registration paths; no delivery, production, nightly save or persistent reconstruction"};
        EZapiBootstrap.WriteDiagnostics();
        return $"物品工厂：{passed}通过，{failed}失败，临时实例已销毁。";
    }
}
