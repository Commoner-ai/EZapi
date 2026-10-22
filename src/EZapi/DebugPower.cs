using Il2Cpp;
using MelonLoader;

namespace EZapi;

/// <summary>仅在按钮点击时创建临时机器/电源；验证实际原生电量差额与堆叠读回，不保存。</summary>
internal static class DebugPower
{
    internal static object? LastReport { get; private set; }
    internal static void Clear() => LastReport = null;

    internal static string Run()
    {
        if (InventoryApi.IsItemDragActive) return "请先放下拖拽物品。";
        if (!VisibleStoreInventory.IsReady) return "请进入测试档店铺。";

        var checks = new List<object>();
        var details = new List<object>();
        var passed = 0;
        var failed = 0;
        void Check(bool ok, string name) { checks.Add(new { name, passed = ok }); if (ok) passed++; else failed++; }

        try
        {
            NativeAccess.RequireRun();
            foreach (var fixture in new[] { (Name: "single", Id: "ezapi.debug.factory_item", Units: 1), (Name: "stack", Id: "ezapi.debug.material_stack", Units: 5) })
            {
                var made = new List<GameItem>();
                GameItem? source = null;
                const int initialEnergy = 100;
                try
                {
                    var machine = ItemApi.Create("ezapi.debug.machine_layout_probe", true);
                    made.Add(machine);
                    Check(MachineLayoutApi.TryGetInventory(machine, out var slots), fixture.Name + ": temporary native machine layout");
                    Check(slots.Battery is not null, fixture.Name + ": battery inventory exists");

                    source = ItemApi.Create(fixture.Id, true);
                    made.Add(source);
                    PowerHelper.InitPowerSourceItem(source, initialEnergy, initialEnergy);
                    PowerHelper.SetPowerSourceAt(source, initialEnergy);
                    if (fixture.Units > 1) source.SetUnitCount(fixture.Units);
                    Put(slots.Battery!, source, fixture.Units);

                    var pointer = source.Pointer;
                    var instanceId = source.uniqueId;
                    var parent = source.parentInventory!.Pointer;
                    var energy = Power.GetEnergy(source);
                    var nativeCost = MachineryHelper.GetMachinePowerUsage(machine);
                    Check(Power.GetMachineCost(machine) == nativeCost, fixture.Name + ": machine cost matches official helper");
                    Check(energy == initialEnergy && energy == PowerHelper.GetAvailableEnergyFromItem(source, false), fixture.Name + ": total native energy readback");
                    Check(source.unitCount == fixture.Units && source.parentInventory!.Pointer == slots.Battery!.Pointer, fixture.Name + ": fixture entered battery slot with exact stack count");

                    var debit = Power.TryDebit(source, 7);
                    var debitReadback = Power.GetEnergy(source);
                    Check(debit.Status == EnergyDebitStatus.Debited && debit.Before == energy && debit.After == energy - 7 && debitReadback == energy - 7,
                        fixture.Name + ": TryDebit commits exact native difference");
                    Check(source.Pointer == pointer && source.uniqueId == instanceId && source.unitCount == fixture.Units && source.parentInventory?.Pointer == parent,
                        fixture.Name + ": debit preserves item identity, inventory and stack count");

                    if (source.Pointer == pointer && source.uniqueId == instanceId && source.parentInventory?.Pointer == parent)
                    {
                        PowerHelper.SetPowerSourceAt(source, energy);
                        Check(Power.GetEnergy(source) == energy, fixture.Name + ": temporary debit restored and read back");
                    }
                    else Check(false, fixture.Name + ": cannot safely restore changed fixture identity");

                    PowerHelper.SetPowerSourceAt(source, 0);
                    var insufficient = Power.TryDebit(source, 7);
                    Check(insufficient.Status == EnergyDebitStatus.Insufficient && Power.GetEnergy(source) == 0,
                        fixture.Name + ": insufficient energy has no native write");
                    PowerHelper.SetPowerSourceAt(source, energy);

                    var zero = Power.TryDebit(source, 0);
                    Check(zero.Status == EnergyDebitStatus.Debited && Power.GetEnergy(source) == energy,
                        fixture.Name + ": zero debit leaves native energy unchanged");

                    details.Add(new { fixture = fixture.Name, units = source.unitCount, itemId = source.identifier, instanceId, energyBefore = energy, energyAfterDebit = debitReadback,
                        debitStatus = debit.Status.ToString(), insufficientStatus = insufficient.Status.ToString(), zeroStatus = zero.Status.ToString(), parentInventory = parent.ToInt64() });
                }
                catch (Exception e)
                {
                    Check(false, fixture.Name + ": execution " + e.GetType().Name + ": " + e.Message);
                    MelonLogger.Error("[EZapi] Power probe " + fixture.Name + ": " + e);
                }
                finally
                {
                    if (source is not null && source.Pointer != IntPtr.Zero && source.parentInventory is not null)
                        try { PowerHelper.SetPowerSourceAt(source, initialEnergy); } catch { }
                    foreach (var item in made.AsEnumerable().Reverse())
                    {
                        try
                        {
                            if (item.parentInventory is { } parent && !parent.Expel(item)) Check(false, fixture.Name + ": temporary item expel failed");
                            if (item.parentInventory is null) item.Destroy();
                        }
                        catch (Exception e) { Check(false, fixture.Name + ": temporary cleanup " + e.Message); }
                    }
                }
            }
        }
        catch (Exception e)
        {
            Check(false, "execution: " + e);
            MelonLogger.Error("[EZapi] Power probe: " + e);
        }

        LastReport = new { at = DateTimeOffset.Now, passed, failed, checks, details, scope = "native PowerHelper and temporary single/stack source fixtures; restored before destruction; no save" };
        EZapiBootstrap.WriteDiagnostics();
        return $"电力接口检查：{passed}通过，{failed}失败，已导出。";
    }

    private static void Put(GameInventory inventory, GameItem item, int count)
    {
        var marker = inventory.TryInventorySlot(item, count, item.modifiedShape, null) ?? inventory.TryFindOneValidInventorySlot(item);
        if (marker?.IsValid() != true || marker.TryAcceptOnce(count) != count || item.parentInventory?.Pointer != inventory.Pointer)
            throw new InvalidOperationException("temporary source was not accepted by battery inventory");
    }
}
