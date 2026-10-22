using EZapi;
using Il2Cpp;
namespace EZapi.Sample;

/// <summary>复制方法体后替换自己的工厂和ID；初始化时调用，默认示例不自动加入真实货架。</summary>
internal static class ItemStockExamples
{
    internal static ContentSubmissionResult Register(string ownerId,string itemId,Func<GameItem> factory)
        => ItemApi.RegisterItem(ownerId,itemId,factory,new NativeItemOptions {
            NightShop=NightShopStockPolicy.Unique,
            // 此谓词在当前游戏主线程补货时执行。可读当前解锁状态；不要写存档或切场景。
            IsNightShopAvailable=()=>RunData.IsAvailable
        });
}
