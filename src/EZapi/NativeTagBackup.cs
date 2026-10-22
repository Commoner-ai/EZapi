using Il2Cpp;
namespace EZapi;

/// <summary>
/// 当前同步事务独占物品的标签备份。逐TagState.Clone并验证独立指针，不能把TagSystem.Clone当深拷贝保证。
/// 保留原state/dict身份，只回填标签；调用者须先核对周目、物品、父库存和状态未被替换。
/// </summary>
internal sealed class NativeTagBackup
{
    private readonly IReadOnlyList<KeyValuePair<string,TagState>> _tags;
    private NativeTagBackup(IReadOnlyList<KeyValuePair<string,TagState>> tags)=>_tags=tags;
    internal static NativeTagBackup Capture(TagSystem state)
    {
        var dict=state.dict??throw new InvalidOperationException("EZAPI_TAG_BACKUP: native dictionary missing.");
        if(dict.Count>4096)throw new InvalidOperationException("EZAPI_TAG_BACKUP: max4096 tags.");
        var result=new List<KeyValuePair<string,TagState>>();var pointers=new HashSet<IntPtr>();
        foreach(var pair in dict) {
            if(pair.Value is null||pair.Value.Pointer==IntPtr.Zero)throw new InvalidOperationException("EZAPI_TAG_BACKUP: dead tag.");
            var clone=pair.Value.Clone();
            if(clone is null||clone.Pointer==IntPtr.Zero||clone.Pointer==pair.Value.Pointer||!pointers.Add(clone.Pointer))
                throw new InvalidOperationException("EZAPI_TAG_BACKUP: independent tag clone required.");
            result.Add(new(pair.Key,clone));
        }
        return new(result.AsReadOnly());
    }
    // 比较标签值而不是包装对象/字典指针；浮点NaN使用Equals保持稳定。
    internal bool Matches(TagSystem state)
    {
        var dict=state.dict;
        return dict is not null&&dict.Count==_tags.Count&&_tags.All(p=>dict.ContainsKey(p.Key)&&Same(p.Value,dict[p.Key]));
    }
    private static bool Same(TagState a,TagState b)=>b is not null&&a.valueEnabled==b.valueEnabled&&
        a.internalValueString==b.internalValueString&&a.valueInt==b.valueInt&&a.valueFloat.Equals(b.valueFloat)&&
        a.valueLong==b.valueLong&&a.valueDouble.Equals(b.valueDouble)&&a.valueBool==b.valueBool&&
        a.identifier==b.identifier&&a.identifierName==b.identifierName&&a.valueString==b.valueString;
    internal void Restore(TagSystem state)
    {
        var dict=state.dict??throw new InvalidOperationException("EZAPI_TAG_BACKUP: native dictionary missing.");
        // 备份本身不能再次成为活物品的可变标签。先完成所有克隆，再清空目标，失败不先丢失原标签。
        var copies=_tags.Select(p=>new KeyValuePair<string,TagState>(p.Key,p.Value.Clone())).ToArray();
        if(copies.Any(p=>p.Value is null||p.Value.Pointer==IntPtr.Zero)||copies.Select(p=>p.Value.Pointer).Distinct().Count()!=copies.Length||
            copies.Where((p,i)=>p.Value.Pointer==_tags[i].Value.Pointer).Any())throw new InvalidOperationException("EZAPI_TAG_BACKUP: restore clone is aliased.");
        dict.Clear();foreach(var pair in copies)dict.Add(pair.Key,pair.Value);
    }
}
