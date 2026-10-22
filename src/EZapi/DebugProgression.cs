using Il2Cpp;
using System.Text.Json;
namespace EZapi;

/// <summary>从当前周目现有升级/服务抽取只读样本；不创建目录、不改付款、解锁、冷却或存档。</summary>
internal static class DebugProgression
{
    internal static object? LastReport{get;private set;}
    internal static void Run(Action<bool,string> check)
    {
        var rows=new List<object>();int passed=0,failed=0;
        void Check(bool ok,string name){check(ok,"progression: "+name);if(ok)passed++;else failed++;}
        var run=NativeAccess.RequireRun();var store=GameSession.Resolve(run);var cash=store.GetCash();
        var upgrades=new List<NetworkUpgrade>();
        if(store.networkUpgrade is { } catalog)foreach(var entry in catalog) {
            if(upgrades.Count>=128)throw new InvalidOperationException("progression debug catalog limit");
            if(entry.Value is {Pointer:var p}&&p!=IntPtr.Zero)upgrades.Add(entry.Value);
        }
        var services=new List<StoreService>();
        if(store.storeService is { } catalogServices)foreach(var item in catalogServices) {
            if(services.Count>=128)throw new InvalidOperationException("progression debug service limit");
            if(item is {Pointer:var p}&&p!=IntPtr.Zero)services.Add(item);
        }
        object Raw()=>new {
            cash=store.GetCash(),
            upgrades=upgrades.Select(x=>new{x.id,x.cost,x.state,x.isRepeatable,x.cooldownCurrent,x.cooldownDuration}).ToArray(),
            services=services.Select(x=>new{x.id,x.cost,x.unlocked,x.introduced,x.isUsing,x.currentCooldown,x.totalCooldown}).ToArray()
        };
        var before=JsonSerializer.Serialize(Raw());
        Check(ProgressionApi.IsAvailable&&upgrades.Count>0&&services.Count>0,"current native progression available");
        foreach(var item in upgrades.OrderBy(x=>x.id,StringComparer.Ordinal).Take(3)) {
            var read=ProgressionApi.ReadUpgrade(item.id);rows.Add(new{kind="upgrade",read});
            Check(read.Status==ProgressionReadStatus.Ready&&read.Run==run&&read.Value!.Id==item.id&&read.Value.Cost==item.GetCost()&&read.Value.Unlocked==item.IsUnlocked()&&read.Value.Ready==item.IsReady(),"actual upgrade predicates "+item.id);
            Check(read.Value is not null&&read.Value.PrerequisitesMet==item.IsHavePrerequisite()&&read.Value.DemoLocked==NetworkUpgrade.IsLockedInDemo(item.id)&&read.Value.CooldownRemaining==item.cooldownCurrent,"actual prerequisite/demo/cooldown "+item.id);
        }
        foreach(var item in services.OrderBy(x=>x.id,StringComparer.Ordinal).Take(3)) {
            var read=ProgressionApi.ReadService(item.id);rows.Add(new{kind="service",read});
            Check(read.Status==ProgressionReadStatus.Ready&&read.Run==run&&read.Value!.Cost==item.cost&&read.Value.Unlocked==item.unlocked&&read.Value.Introduced==item.introduced&&read.Value.Using==item.isUsing,"actual service independent fields "+item.id);
            Check(read.Value is not null&&read.Value.Active==item.IsActive()&&read.Value.OnCooldown==item.IsOnCooldown(),"actual service effective predicates "+item.id);
        }
        var tree=new RequirementDefinition{Children=new[]{new RequirementDefinition{Kind="credits-at-least",Amount=cash,Message=new("余额足够","Enough credits")},new RequirementDefinition{Kind="day-at-least",Amount=0}}};
        var result=RequirementApi.Evaluate(tree);rows.Add(new{kind="requirements",result});
        Check(result.Satisfied&&result.Run==run&&result.Children.Count==2,"current-run day/cash composition");
        var parsed=Requirements.Parse(new MemoryStream(System.Text.Encoding.UTF8.GetBytes("{\"kind\":\"credits-at-least\",\"amount\":0,\"message\":{\"chinese\":\"余额\",\"english\":\"Credits\"}}")));
        Check(RequirementApi.Evaluate(parsed).Satisfied,"strict JSON flows through actual current-run facts");
        var known=upgrades[0];var leaf=new RequirementDefinition{Kind="upgrade-unlocked",TargetId=known.id};
        Check(RequirementApi.Evaluate(leaf).Satisfied==known.IsUnlocked(),"requirement uses actual upgrade state");
        var inverted=RequirementApi.Evaluate(new(){Kind="not",Children=new[]{leaf}});
        Check(inverted.Status==(known.IsUnlocked()?RequirementStatus.Unsatisfied:RequirementStatus.Satisfied),"not inverts known state without unlocking");
        Check(Requirements.Evaluate(parsed,new TextFacts(),"en").Message=="Credits"&&Requirements.Evaluate(parsed,new TextFacts(),"zh").Message=="余额","typed requirement message resolves both languages");
        Check(before==JsonSerializer.Serialize(Raw())&&store.GetCash()==cash&&NativeAccess.SameRun(run),"all observed native progression fields and cash unchanged");
        LastReport=new{at=DateTimeOffset.Now,passed,failed,run,rows,upgradeCount=upgrades.Count,serviceCount=services.Count,nativeStateUnchanged=before==JsonSerializer.Serialize(Raw()),scope="readonly existing native upgrade/service queries and current-run requirements; no purchase/unlock, real language switch, calendar mutation or save"};
    }
    private sealed class TextFacts:IRequirementFacts {public RequirementFact Read(string kind,string? id,long? amount)=>new(RequirementStatus.Satisfied);}
}
