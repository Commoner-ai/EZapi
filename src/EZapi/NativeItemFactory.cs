using Il2Cpp;
using Il2CppInterop.Runtime;
namespace EZapi;

/// <summary>
/// 官方目录工厂：每次 CreateEmptyItem(ID) 创建独立实例，依次写公开字段/辅助方法。
/// 不注册目录、不碰 UI、不替换 tooltip；新物品和游戏读档重建共用此工厂。
/// </summary>
internal static class NativeItemFactory
{
    // 强引用保留转换后的委托；所有本框架实例共享无状态的“不堆叠”谓词。
    private static Il2CppSystem.Func<GameItem,GameItem,bool> NeverStack=>NativeDelegateBridge.Stack();
    internal static GameItem Create(ItemDefinition d)
    {
        // CreateEmptyItem 的参数是新物品 ID，不是模板；每次创建独立实例。
        var item=ItemDirectory.CreateEmptyItem(d.Id);
        try {
        var locale=NativeItemLocalization.CurrentLocaleCode;
        var name=ItemText.Name(d,locale);
        item.identifierName=name;
        item.SetName(name);
        var placement=d.DescriptionPlacement;
        if (placement=="native") {
            item.shortDescription=ItemText.Resolve(d,d.LocalizedShortDescription??d.LocalizedDescription,d.ShortDescription??d.Description,locale);
            item.longDescription=ItemText.Resolve(d,d.LocalizedLongDescription,d.LongDescription,locale);
            item.flavorText=ItemText.Resolve(d,d.LocalizedFlavorText,d.FlavorText,locale);
            item.customText=ItemText.Resolve(d,d.LocalizedCustomText,d.CustomText,locale);
        } else {
            item.shortDescription=""; item.longDescription=""; item.flavorText=""; item.customText="";
        }
        var shape=new GridShapeBuilder(d.Width??1,d.Height??1);
        if (d.ShapeRows is { Count: > 0 }) shape.SetData(string.Concat(d.ShapeRows),d.Width!.Value);
        else shape.SetDataFill(1);
        item.SetShape(shape.Build());
        item.SetUnitCount(d.InitialCount);
        if(d.Module is { } module)ModuleHelper.InitModuleItem(item,module.Type,module.Performance,module.Efficiency,module.Quality,
            module.IsShipModule,module.LocalizedAdditionalEffectDisplay is null?module.AdditionalEffectDisplay:"",module.Tier);
        if(d.Node)ModuleEffectHelper.InitNode(item);
        ApplyConsumable(item,d);
        NativeContainerAdapter.Initialize(item,d);
        item.SetValue(d.Value??0);
        // 官方实例级行为委托；无需修改所有游戏物品的 MayStack。
        item.mayStackItemFunc=NeverStack;
        // 新原生 helper 可能设置自己的默认标记；只显式覆盖内容作者提供的字段。
        // 旧普通物品仍使用原有默认值，避免改变已发布包的语义。
        var hasNativeConsumable=d.Food is not null||d.LiquidContainer is not null||d.Container is not null||d.Module is not null||d.Node;
        if(!hasNativeConsumable||d.ForceDisableUse.HasValue)item.forceDisableUse=d.ForceDisableUse??false;
        if(!hasNativeConsumable||d.ForceDisableActivate.HasValue)item.forceDisableActivate=d.ForceDisableActivate??false;
        if(!hasNativeConsumable||d.ActivateDefault.HasValue)item.activateDefault=d.ActivateDefault??false;
        if(!hasNativeConsumable||d.BonusAccuracy.HasValue)item.bonusAccuracy=d.BonusAccuracy??0;
        if(!hasNativeConsumable||d.TriggerOverwatch.HasValue)item.triggerOverwatch=d.TriggerOverwatch??false;
        if(!hasNativeConsumable||d.CanUseOutsideCombat.HasValue)item.canUseOutsideCombat=d.CanUseOutsideCombat??false;
        foreach(var type in d.Types) item.SetGameItemType(type);
        ApplyTags(item,d.Tags);
        if(d.ContrabandLevel>0) ContrabandHelper.InitContrabandItem(item,d.ContrabandLevel);
        if(d.UseCount is { } use) {
            UseCountHelper.InitUseCountItem(item,use.Maximum,use.Transferable,use.DestroyOnEmpty,use.AutoValue,use.BaseValue,use.ValuePerUse);
            if(use.Current is { } current) UseCountHelper.SetCurrentUseCount(item,current);
        }
        ApplySounds(item,d.Sounds);
        ItemActivation.Bind(item,d);
        item.SyncModifiedState();
        LegacySpriteAdapter.Bind(item,d);
        return item;
        } catch { item.Destroy(); throw; }
    }
    private static void ApplyConsumable(GameItem item,ItemDefinition d)
    {
        if(d.Food is { } food) {
            // 当前官方签名的第四参数是 isMeat；显式传参，避免旧构建遗留三参数调用。
            FoodItemHelper.InitFoodItem(item,food.Calories,food.Canned,food.Meat);
            if(food.Freshness is { } freshness)FoodItemHelper.InitFreshFood(item,freshness);
        }
        if(d.LiquidContainer is { } liquid) {
            // 050Rev5：旧 LiquidContainerHelper 仍有签名，但未建立新版容量/成分状态。
            // 当前 WaterHelper 负责完整原生行为、标签和容量，单位 ml；初始水质采用原生基础水。
            WaterHelper.InitLiquidContainerItem(item,liquid.Capacity,checked((int)(d.Value??0)),false,false,false,false,3);
            if(liquid.StartFull)WaterHelper.FillWithBaseWater(item);
        }
    }
    private static void ApplyTags(GameItem item,IReadOnlyList<ItemTagDefinition> tags)
    {
        if(tags.Count==0)return;
        item.state ??= new TagSystem();
        foreach(var tag in tags) {
            item.state.InitTagString(tag.Id);
            var state=item.state.GetTag(tag.Id);
            state.SetEnabled(tag.Enabled);
            if(tag.StringValue is { } s) state.SetString(s);
            if(tag.IntValue is { } i) state.SetInt(i);
            // 当前 TagState 没有 SetLong 方法；valueLong 是公开的原生包装属性。
            if(tag.LongValue is { } l) state.valueLong=l;
            if(tag.FloatValue is { } f) state.SetFloat(f);
            if(tag.DoubleValue is { } db) state.SetDouble(db);
            if(tag.BoolValue is { } b) state.SetBool(b);
        }
    }
    private static void ApplySounds(GameItem item,ItemSoundDefinition? s)
    {
        if(s is null)return;
        if(s.DragStart is not null)item.soundDragStart=s.DragStart;
        if(s.DragEnd is not null)item.soundDragEnd=s.DragEnd;
        if(s.ContainerOpen is not null)item.soundContainerOpen=s.ContainerOpen;
        if(s.ContainerClose is not null)item.soundContainerClose=s.ContainerClose;
        if(s.Select is not null)item.soundSelect=s.Select;
        if(s.ExamineOpen is not null)item.soundExamineOpen=s.ExamineOpen;
        if(s.Toggle is not null)item.soundToggle=s.Toggle;
        if(s.Activate is not null)item.soundActivate=s.Activate;
        if(s.Use is not null)item.soundUse=s.Use;
    }
}

