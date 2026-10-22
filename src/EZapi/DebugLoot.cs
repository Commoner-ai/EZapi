namespace EZapi;

/// <summary>手动查看/试抽示例表；不创建 GameItem，不发放，不保存。试抽会推进原生 RNG。</summary>
internal static class DebugLoot
{
    internal static object? LastReport { get; private set; }
    internal static string Execute(bool roll,GameRunIdentity? expected)
    {
        try {
            if(expected is null||!GameSession.TryResolve(expected.Epoch,out _)||GameSession.Identity!=expected)
                return "当前周目身份已变化或尚未就绪，请进入店铺后重新点击。";
            var snapshot=LootTables.GetSnapshot("ezapi.sample.medical_demo");
            string? itemId=null;
            var success=!roll||LootTables.TryRoll(snapshot.Id,false,out itemId);
            LastReport=new {operation=roll?"RollIdOnly":"SnapshotOnly",snapshot,result=itemId,
                randomStateAdvanced=roll,spawned=false,saveRequested=false};
            EZapiBootstrap.WriteDiagnostics();
            if(!snapshot.Found)return "示例掉落表尚不存在；请查看诊断 loot 的状态和错误。";
            if(roll)return success?"试抽结果："+itemId+"（未发放；已推进游戏随机数）":"该表没有可抽取的正权重条目。";
            return "示例表："+string.Join("；",snapshot.Entries.Select(e=>$"{e.Id} = {e.Weight} ({e.Probability:P0})"));
        } catch(Exception e) {
            MelonLoader.MelonLogger.Error("[EZapi] EZAPI_LOOT_DEBUG_FAILED: "+e);
            return "掉落调试失败："+e.Message;
        }
    }
    internal static void Clear()=>LastReport=null;
}
