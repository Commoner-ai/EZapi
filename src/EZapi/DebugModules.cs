using Il2Cpp;
using MelonLoader;
namespace EZapi;

/// <summary>手动临时实例验收。目录ID来自当前原生目录，不写玩家库存或存档，不自动进入生产。</summary>
internal static class DebugModules
{
    internal static object? LastReport { get; private set; }
    internal static void Clear() => LastReport = null;
    internal static string Run()
    {
        if (InventoryApi.IsItemDragActive) return "请先放下拖拽中的物品，再检查模块。";
        if (!VisibleStoreInventory.IsReady)
            return "请进入测试档店铺再检查模块。";
        const string owner = "ezapi.debug", moduleId = "ezapi.debug.module_probe", type = "ezapi.debug.probe_type";
        var checks = new List<object>(); var events = new List<string>(); var leases = new List<IDisposable>();
        GameItem? machine = null, module = null; int passed = 0, failed = 0;
        string? selected = null; string[] candidates = Array.Empty<string>();
        var directories = new List<object>();
        void Check(bool result, string name) { checks.Add(new { name, passed = result }); if (result) passed++; else failed++; }
        try {
            NativeAccess.RequireRun();
            var factoryIds = new List<string>();
            var directoryNames = DirectoryMaster.GetDictionaryClassNameList<GameItem>();
            if (directoryNames.Count > 256) throw new InvalidOperationException("EZAPI_MODULE_TEST_LIMIT: too many directories.");
            for (var d=0; d<directoryNames.Count; d++) {
                var directoryName=directoryNames[d];
                var nativeIds = DirectoryMaster.GetIdentifierList<GameItem>(directoryName);
                if (nativeIds.Count > 20000) throw new InvalidOperationException("EZAPI_MODULE_TEST_LIMIT: directory exceeds20000 IDs.");
                var ids=new List<string>();
                for (var i=0; i<nativeIds.Count; i++) {
                    ids.Add(nativeIds[i]);
                    if (nativeIds[i].Contains("blender", StringComparison.OrdinalIgnoreCase)) factoryIds.Add(nativeIds[i]);
                }
                directories.Add(new { name=directoryName, count=ids.Count, ids });
            }
            candidates = factoryIds.Distinct(StringComparer.Ordinal).OrderBy(id => id, StringComparer.Ordinal).ToArray();
            selected = candidates.FirstOrDefault(id => !id.Contains("broken", StringComparison.OrdinalIgnoreCase) && !id.Contains("ruined", StringComparison.OrdinalIgnoreCase));
            // Demo可能没有生产机器工厂。此时显式创建临时测试壳，仍调用官方SetupModuleBay；不伪称验证了原版生产机器。
            var synthetic = selected is null;
            selected ??= "ezapi.debug.module_machine_probe";
            var accepted = ModuleApi.RegisterAdmission(owner, new[] { new ModuleAdmission(selected, new[] { type }) });
            if (!accepted.Accepted) throw new InvalidOperationException(accepted.Reason);
            foreach (var phase in Enum.GetValues<ModulePhase>()) {
                var captured = phase;
                leases.Add(ModuleApi.Subscribe(owner, "probe." + phase, phase, context => {
                    if (context.Machine?.identifier == selected || context.Modules.Any(i => i.identifier == moduleId)) events.Add(captured.ToString());
                }));
            }
            int actions = 0, captures = 0, completed = 0;
            leases.Add(ModuleApi.SubscribeAction(owner, "probe.action", moduleId, context => { if (context.Machine.identifier == selected) actions++; }));
            leases.Add(ModuleApi.SubscribeLearning(owner, "probe.learning", item => {
                if (item.identifier == moduleId) captures++;
                return (item.identifier, item.uniqueId);
            }, (item, state) => { if (item.identifier == moduleId && state == (item.identifier, item.uniqueId)) completed++; }));
            Check(ModuleApi.GetRuntimeStatus().All(h => h.Ready), "four native hook groups ready");
            if (!ModuleApi.GetRuntimeStatus().All(h => h.Ready)) throw new InvalidOperationException("EZAPI_MODULE_TEST_HOOKS: native groups unavailable.");
            if (synthetic) {
                machine = ItemDirectory.CreateEmptyItem(selected);
                machine.SetShape(new GridShapeBuilder(1, 1).SetDataFill(1).Build());
                var layout = new GridPixelElement(2, 2, false);
                var grid = new GameGridInventory(4, 4);
                if (!layout.AttachPos(grid.Cast<PixelElement>(), 1, 1)) throw new InvalidOperationException("temporary grid attachment failed");
                var window = new PixelWindow(true, "EZapi临时模块检查");
                if (!window.Attach(layout.Cast<PixelElement>())) throw new InvalidOperationException("temporary window attachment failed");
                machine.SetContentWindow(window);
                MachineHelper.SetupModuleBay(grid, machine, null, new Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStringArray(Array.Empty<string>()));
            } else machine = DirectoryMaster.Item(selected, false);
            GeneralHelper.SetItemOwned(machine, true); // 仅临时未交付实例，模拟玩家机器准入。
            var inventory = ModuleApi.GetInventory(machine) ?? throw new InvalidOperationException("EZAPI_MODULE_TEST_INVENTORY: factory has no module inventory.");
            Check(inventory.Pointer != IntPtr.Zero, "official GetModuleInv returns factory inventory");
            module = ItemDirectory.CreateEmptyItem(moduleId);
            module.SetShape(new GridShapeBuilder(1, 1).SetDataFill(1).Build());
            ModuleHelper.InitModuleItem(module, type, 10, 5, 3, false, "", 1);
            GeneralHelper.SetItemOwned(module, true);
            checks.Add(new { name="slot inputs", moduleType=module.GetTagReadonly("MODULE_TYPE")?.valueString,
                moduleWidth=module.modifiedShape.width, moduleHeight=module.modifiedShape.height,
                inventoryWidth=inventory.inventoryShape.width, inventoryHeight=inventory.inventoryShape.height,
                mayHave=inventory.MayHaveValidInventorySlot(module), insertLocked=inventory.IsInsertLocked(),
                isModule=module.IsGameItemType("MODULE"), ownedMachine=GeneralHelper.IsItemOwned(machine), ownedModule=GeneralHelper.IsItemOwned(module) });
            var slot = inventory.TryFindOneValidInventorySlot(module, false);
            Check(slot?.IsValid() == true, "custom module type admitted to temporary native machine");
            if (slot?.IsValid() != true) throw new InvalidOperationException("EZAPI_MODULE_TEST_SLOT: module admission denied.");
            Check(slot.TryAcceptOnce(1) == 1, "native slot accepts one temporary module");
            Check(ModuleApi.FindInstalled(machine, moduleId, type).Count == 1, "installed module exact ID and type found");
            events.Clear(); ModuleApi.Recompute(machine);
            Check(new[] { "AfterTemp", "BeforeAggregate", "AfterAggregate" }.All(events.Contains), "native recompute dispatches temporary and aggregate phases");
            ModuleEffectHelper.ActivateOnUseAction(module, inventory, machine);
            Check(actions == 1, "native module action callback dispatched once");
            ModuleEffectHelper.InitLearningAlgo(module);
            ModuleEffectHelper.OnUsedLearningAlgo(module);
            Check(captures == 1 && completed == 1, "native learning captures and completes same invocation");
            events.Clear();
            ModuleEffectHelper.OnAddedModuleToGrid(module, machine, inventory.items);
            Check(events.Contains("BeforeAdded"), "native added-stage method dispatches BeforeAdded");
            events.Clear();
            ModuleEffectHelper.OnRemovedModuleFromGrid(module, machine, inventory.items);
            Check(events.Contains("AfterRemoved"), "native removed-stage method dispatches AfterRemoved");
            var rejected=ItemDirectory.CreateEmptyItem("ezapi.debug.rejected_module_probe");
            try {
                rejected.SetShape(new GridShapeBuilder(1, 1).SetDataFill(1).Build());
                ModuleHelper.InitModuleItem(rejected,"ezapi.debug.not_admitted",0,0,0,false,"",1);
                GeneralHelper.SetItemOwned(rejected,true);
                Check(inventory.TryFindOneValidInventorySlot(rejected,false)?.IsValid()!=true, "unregistered custom module type rejected by native bay");
            } finally { rejected.Destroy(); }
            Check(inventory.Expel(module), "temporary module removed by native inventory");
            Check(ModuleApi.FindInstalled(machine,moduleId,type).Count==0, "removed temporary module no longer found");
            foreach(var lease in leases)lease.Dispose();
            var actionCount=actions;var completionCount=completed;
            ModuleEffectHelper.ActivateOnUseAction(module,inventory,machine);
            ModuleEffectHelper.OnUsedLearningAlgo(module);
            Check(actions==actionCount && completed==completionCount, "disposed native action and learning subscriptions stay silent");
        } catch (Exception e) { failed++; checks.Add(new { name="execution", passed=false, error=e.ToString() }); MelonLogger.Error("[EZapi] Module probe: " + e); }
        finally {
            foreach (var lease in leases) lease.Dispose();
            ModuleApi.RemoveAdmission(owner);
            try { module?.Destroy(); } catch (Exception e) { failed++; checks.Add(new { name="destroy module", error=e.Message }); }
            try { machine?.Destroy(); } catch (Exception e) { failed++; checks.Add(new { name="destroy machine", error=e.Message }); }
        }
        LastReport = new { at=DateTimeOffset.Now, passed, failed, selectedFactory=selected, candidates, directories, checks, events,
            hooks=ModuleApi.GetRuntimeStatus(), syntheticMachine=selected=="ezapi.debug.module_machine_probe", scope="temporary unowned factory or explicit synthetic machine/module; real native slot/recompute/action/learning; no player inventory writes or save; not proof of productive machine work" };
        EZapiBootstrap.WriteDiagnostics();
        return $"模块临时实例检查：{passed}通过，{failed}失败，详情已导出。";
    }
}
