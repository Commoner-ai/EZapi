namespace EZapi;

/// <summary>仅记录真实生命周期和应用通知；不触发结算、加载、保存或物品生成。只保留托管文本。</summary>
internal static class DebugLifecycle
{
    private static readonly List<IDisposable> Leases=new();
    private static readonly Queue<object> Entries=new();
    internal static bool Recording=>Leases.Count>0;
    internal static string Toggle()
    {
        if(Recording){Stop();return "阶段记录已停止；点击“导出诊断”查看最近128条记录。";}
        Entries.Clear();
        try {
            foreach(var phase in Enum.GetValues<LifecyclePhase>()) {
                var captured=phase;
                Leases.Add(LifecycleApi.Subscribe("ezapi.debug","trace."+phase,phase,ctx=>
                    Add(new{at=DateTimeOffset.Now,kind="lifecycle",phase=captured.ToString(),run=ctx.Run})));
            }
            Leases.Add(ContentApi.Subscribe("ezapi.debug","trace.content",view=>Add(new{at=DateTimeOffset.Now,kind="content",view})));
            return "阶段记录已开启；正常读档、睡觉、外出回店后点击“导出诊断”。";
        }catch(Exception e){Stop();return "阶段记录未开启："+e.Message;}
    }
    private static void Add(object value){Entries.Enqueue(value);while(Entries.Count>128)Entries.Dequeue();}
    internal static object Report()=>new{recording=Recording,entries=Entries.ToArray()};
    internal static void Stop(){foreach(var lease in Leases)lease.Dispose();Leases.Clear();}
    internal static void Clear(){Stop();Entries.Clear();}
}
