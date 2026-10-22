using Il2Cpp;
namespace EZapi;

/// <summary>
/// 本构建缺少 ModHelper 的掉落便捷方法，集中适配官方公开 LootRegistry。
/// 只通过 Register/Add/Remove/Set/Clear 修改 journal；不写 tables/groups/journal 字段，不改拾荒生成方法。
/// </summary>
internal static class NativeLootAdapter
{
    internal static void Refresh()=>LootRegistry.RebuildIfDirty();
    internal static bool Has(string id,bool group)
    {
        var lists=group?LootRegistry.groups:LootRegistry.tables;
        return lists is not null&&lists.ContainsKey(id);
    }
    internal static void Execute(string owner,LootOperation operation)
    {
        var id=operation.ListId;var entry=operation.EntryId!;var weight=operation.Weight;
        if(operation.Kind=="register") {
            var entries=new Il2CppSystem.Collections.Generic.List<LootEntry>();
            foreach(var row in operation.Entries!)entries.Add(new LootEntry(row.Id,row.Weight,owner));
            var enumerable=entries.Cast<Il2CppSystem.Collections.Generic.IEnumerable<LootEntry>>();
            if(operation.IsGroup)LootRegistry.RegisterGroup(owner,id,enumerable);
            else LootRegistry.RegisterTable(owner,id,enumerable);
        } else if(operation.IsGroup) {
            switch(operation.Kind) {
                case "clear":LootRegistry.ClearGroup(owner,id);break;
                case "add":LootRegistry.AddGroupEntry(owner,id,entry,weight);break;
                case "remove":LootRegistry.RemoveGroupEntry(owner,id,entry);break;
                case "setWeight":LootRegistry.SetGroupWeight(owner,id,entry,weight);break;
                default:throw new InvalidOperationException("Unknown loot operation.");
            }
        } else {
            switch(operation.Kind) {
                case "clear":LootRegistry.ClearTable(owner,id);break;
                case "add":LootRegistry.AddEntry(owner,id,entry,weight);break;
                case "remove":LootRegistry.RemoveEntry(owner,id,entry);break;
                case "setWeight":LootRegistry.SetWeight(owner,id,entry,weight);break;
                default:throw new InvalidOperationException("Unknown loot operation.");
            }
        }
    }
    internal static LootListSnapshot Snapshot(string id,bool group)
    {
        Refresh();
        var lists=group?LootRegistry.groups:LootRegistry.tables;
        if(lists is null||!lists.ContainsKey(id))return new(id,group,false,0,Array.Empty<LootEntrySnapshot>());
        var values=new List<(string Id,float Weight,string? Source,string? Modified)>();
        foreach(var row in lists[id])values.Add((row.id,row.weight,row.sourceModId,row.modifiedByModId));
        var total=values.Where(r=>float.IsFinite(r.Weight)&&r.Weight>0).Sum(r=>(double)r.Weight);
        return new(id,group,true,total,values.Select(r=>new LootEntrySnapshot(r.Id,r.Weight,r.Source,r.Modified,
            total>0&&float.IsFinite(r.Weight)&&r.Weight>0?r.Weight/total:0)).ToArray());
    }
    internal static string Roll(string id,bool group)=>group?LootRegistry.RollGroup(id):LootRegistry.Roll(id);
}
