namespace EZapi;

/// <summary>Explicit UI-only extensions through the same public API as a consumer Mod. No native factions/save writes.</summary>
internal static class DebugFactions
{
    private const string Owner="ezapi.debug.factions";
    private static readonly List<string> Ids=new();
    internal static string Run()
    {
        if(!DeveloperToolsSettings.Current.Enabled||!GameSession.TryResolve(null,out _))return "Faction probe: loaded store required";
        Clear();
        for(int i=0;i<24;i++){
            int index=i;string id=Owner+".probe"+i.ToString("00");
            var result=FactionUiApi.Register(Owner,new(id,new("扩展阵营测试 "+(i+1),"Extension faction "+(i+1))){Color="#71546D",Order=i},context=>
                new(index%2==0?35:-35){Perks=Enumerable.Range(0,28).Select(n=>new FactionPerkView(id+".perk"+n,
                    new("测试权益 "+(n+1),"Probe benefit "+(n+1)),
                    new(string.Concat(Enumerable.Repeat("只读展示测试：不解锁、不扣费、不修改阵营或存档。长说明使用原生字体换页，不增加底部滚动条。",n==0?24:1)),string.Concat(Enumerable.Repeat("Read-only display probe: no unlock, payment, native faction or save changes. Long descriptions use native text pages without a bottom scrollbar. ",n==0?24:1))),
                    n%2==0?60:-60,n%4==0){Flavor=new("来自可移除的开发者界面夹具。","From a removable developer presentation fixture."),Icon=n%2==0?"star":"sad"}).ToArray()});
            if(result.Status!=PresentationStatus.Queued){Clear();return result.Reason??"Faction probe registration failed";}Ids.Add(id);
        }
        var open=FactionUiApi.Open();return open.Status==PresentationStatus.Opened?"24 temporary factions / 28 benefits each; close window to clear":open.Reason??"Faction probe pending";
    }
    internal static void Clear()
    {foreach(var id in Ids)FactionUiApi.Catalog.Remove(Owner,id);Ids.Clear();FactionReputationBrowser.RefreshSoon();}
}
