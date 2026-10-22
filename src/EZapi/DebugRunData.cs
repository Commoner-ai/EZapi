namespace EZapi;

/// <summary>只有玩家点击调试按钮才暂存测试数；不要求游戏保存，不触碰其它 Mod 的数据。</summary>
internal static class DebugRunData
{
    internal static object? LastReport { get; private set; }
    internal static string Execute(bool increment,GameRunIdentity? expected)
    {
        try {
            if(expected is null||!RunData.TryOpen("ezapi.debug",out var data)||data!.Identity!=expected)
                return "当前读档/场景身份尚未就绪或已变化，请等待进入店铺后重试。";
            var hadValue=data.Contains("manualCounter");
            var count=data.GetInt("manualCounter");
            if(increment) { count=checked(count+1);data.SetInt("manualCounter",count); }
            LastReport=new {run=data.Identity,key="manualCounter",count,hadValue,
                operation=increment?"StagedInMemory":"ReadFromCurrentRunMemory",saveRequested=false};
            EZapiBootstrap.WriteDiagnostics();
            return $"测试数={count}，槽 {data.Identity.SaveSlotId}；"+
                (increment?"已暂存到当前周目内存，尚未证明已保存。":"已读取当前周目内存（没有写入）。");
        } catch(Exception e) {
            MelonLoader.MelonLogger.Error("[EZapi] EZAPI_RUN_DEBUG_FAILED: "+e);
            return "周目数据调试失败："+e.Message;
        }
    }
    internal static void Clear()=>LastReport=null;
}
