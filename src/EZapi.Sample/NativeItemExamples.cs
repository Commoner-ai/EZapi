using Il2Cpp;
using EZapi;
namespace EZapi.Sample;

/// <summary>复制到自己的Mod初始化入口调用。此示例DLL默认不安装，且不会自动发放或保存物品。</summary>
public static class NativeItemExamples
{
    public static ContentSubmissionResult RegisterModule()
    {
        const string owner="author.tools",id="author.tools.sensor";
        return ItemApi.RegisterModule(owner,id,()=> {
            var item=ItemDirectory.CreateEmptyItem(id);
            try {
                item.SetShape(new GridShapeBuilder(1,1).SetDataFill(1).Build());
                ModuleHelper.InitModuleItem(item,"MODULE_TYPE_UNIVERSAL",10,5,3,false,"",2);
                item.SetValue(100);
                // 不缓存实例，不依赖玩家/店铺UI；读档重建也会调用这个工厂。
                return item;
            }catch{item.Destroy();throw;}
        },new NativeItemOptions(Name:"检测模块",ShortDescription:"通用模块"));
    }
    public static ContentSubmissionResult RegisterNode()
    {
        const string id="author.tools.node";
        return ItemApi.RegisterNode("author.tools",id,()=> {
            var item=ItemDirectory.CreateEmptyItem(id);
            try {
                item.SetShape(new GridShapeBuilder(1,1).SetDataFill(1).Build());
                ModuleEffectHelper.InitNode(item);
                return item;
            }catch{item.Destroy();throw;}
        },new NativeItemOptions(Name:"效果节点",SourceName:"工具示例"));
    }
}
