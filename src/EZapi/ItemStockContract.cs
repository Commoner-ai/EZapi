namespace EZapi;

/// <summary>None不供应；Repeatable每次补货可供应；Unique仅在当前玩家未持有时供应，并非永久购买次数。</summary>
public enum NightShopStockPolicy { None, Repeatable, Unique }

/// <summary>当前补货周期的托管诊断；不持有商店库存或物品引用。</summary>
public sealed record ItemStockView(string ItemId, NightShopStockPolicy Policy, long Added, long Skipped, string? LastError);

internal static class ItemStockRules
{
    internal static bool Eligible(NightShopStockPolicy policy,bool owned,bool alreadyStocked,Func<bool>? available)
    {
        if(!Enum.IsDefined(typeof(NightShopStockPolicy),policy))throw new ArgumentException("EZAPI_STOCK_POLICY: unknown policy.");
        // 固定条件不满足时，不调用作者谓词；一次补货每个候选最多调用一次。
        return policy!=NightShopStockPolicy.None && !alreadyStocked &&
            (policy!=NightShopStockPolicy.Unique || !owned) && (available?.Invoke() ?? true);
    }
}
