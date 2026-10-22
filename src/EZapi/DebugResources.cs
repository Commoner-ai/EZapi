using Il2Cpp;
namespace EZapi;

/// <summary>玩家点击后按需只读当前周目的物品/资源；结果仅保存字符串和数值，不保存原生引用。</summary>
internal static class DebugResources
{
    internal static object? LastReport { get; private set; }
    internal static void Clear() => LastReport = null;
    internal static string Run(GameRunIdentity? expected)
    {
        if (expected is null || !NativeAccess.SameRun(expected)) return "场景或周目已变化，请在店铺重新点击。";
        var player = GameSession.Resolve(expected);
        var items = player.FindAllItem(true);
        if (items is null || items.Count > 4096) return "物品列表未就绪或超过单次4096件的读取上限。";
        var records = new List<object>();
        var seen = new HashSet<IntPtr>();
        var liquids = 0;
        var powered = 0;
        for (var i = 0; i < items.Count; i++) {
            var item = items[i];
            if (item is null || item.Pointer == IntPtr.Zero || !seen.Add(item.Pointer) || item.unitCount <= 0) continue;
            try {
                // 最多生成64条详情，避免调试按钮对大型周目造成长帧；不调用会创建物品的接口。
                if (records.Count >= 64) break;
                var isLiquid = Liquids.IsContainer(item);
                var energy = Power.GetEnergy(item);
                if (!isLiquid && energy <= 0) continue;
                if (isLiquid) liquids++;
                if (energy > 0) powered++;
                records.Add(new {id=item.identifier,name=item.name,units=item.unitCount,
                    currentUnitValue=ItemInspection.CurrentUnitValue(item),
                    energy,liquid=isLiquid ? Liquids.Capture(item) : null,productionLiquid=isLiquid ? Liquids.CaptureProduction(item) : null});
            } catch (Exception e) { records.Add(new {id=item.identifier,error=e.GetType().Name+": "+e.Message}); }
        }
        LastReport = new {at=DateTimeOffset.Now,run=expected,totalItems=items.Count,liquidContainers=liquids,
            poweredItems=powered,details=records,bounded=true,scope="current run FindAllItem roots; read only; no debit, transfer, creation or save"};
        EZapiBootstrap.WriteDiagnostics();
        return $"只读资源：{liquids} 个液体容器，{powered} 件有电量物品；最多64条详情已写入诊断。";
    }
}
