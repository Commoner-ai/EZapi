using Il2Cpp;
using MelonLoader;
using UnityEngine;

namespace EZapi;

/// <summary>
/// 预览版的手动测试入口。OnGUI 只展示快照、收集点击；生成和诊断写入在主线程 Update 中各执行一次。
/// 不持有玩家、库存、窗口或物品引用；换场景时撤销尚未执行的请求。
/// </summary>
internal sealed class EZapiDebugOverlay
{
    private static string L(string chinese, string english) => NativeItemLocalization.IsEnglish ? english : chinese;
    private enum Request { None, UiPreview, WildStress, Persistence, Diagnostics, Spawn, SpawnEmptyLiquid, Verify, ReadRunData, IncrementRunData, ViewLoot, RollLoot, Localization, Resources, Inventory, FreezeFirst, ReleaseFreeze, SaveGraph, LifecycleTrace, Production, Batch, LiquidPreview, LiquidExecute, Modules, Factories, SpriteAssets, MachineLayouts, RecipeGuide, MachineTransactions, MachineRecipes, MachineMaterials, MachineRegistration, MachineAutomation, MachineNight, ManufacturingTerminal, TerminalFixture, ExternalPower, Power, NativeFurnace, SaveReadback, Reading, ReadingShow, ReadingClose, Dialogue, DialogueShow, DialogueClose, Supplier, SupplierStatus,Phone,PhoneShow,PhoneClear,PhoneDial,RunPurchase,Endpoint,EndpointPersist,EndpointDelete,Workshop,ItemInspection,ItemStock,NpcStock,Registration,SaveGraphRoundTrip }
    private static EZapiDebugOverlay? _current;
    private IReadOnlyList<DebugItemRow> _rows = Array.Empty<DebugItemRow>();
    private bool _open, _refresh = true;
    private double _nextRefresh;
    private Request _pending;
    private string _pendingId = "", _run = "", _pendingRun = "";
    private int _slot = -1, _pendingSlot;
    private GameRunIdentity? _pendingDataRun;
    private string _message = "";
    private bool? _messageEnglish;
    private string _location = "";
    private int _page;
    private GUIStyle? _label, _button, _title;
    private readonly List<(string Id, Rect Area, Action Click)> _buttons = new();
    private string? _pressed;
    private readonly DebugInputShield _shield = new();

    private void Button(string id, Rect area, string text, Action click)
    {
        GUI.Box(area, text, _button);
        _buttons.Add((id, area, click));
    }

    internal void Start() => _current = this;
    internal static object Report() => new
    {
        enabled = DeveloperToolsSettings.Current.Enabled, regressionFixtures = DeveloperToolsSettings.Current.RegressionFixtures,
        regressionSession = DebugRegressionSession.Report(),
        initialized = _current is not null, open = _current?._open ?? false,
        suppressedByWorkshop = WorkshopApi.IsVisible, suppressedByGuide = RecipeGuideApi.IsVisible
    };
    private static Rect ToggleRect => new(12, 12, 168, 34);
    private static Rect PanelRect => new(12, 56,
        Mathf.Min(700, Screen.width - 24), Mathf.Max(120, Mathf.Min(740, Screen.height - 68)));

    // 只屏蔽自己按钮/面板内的新库存鼠标动作。已有拖拽的释放、取消和原版键盘路径仍交还游戏。
    internal static bool PointerOver()
    {
        if(WorkshopBrowser.PointerOver())return true;
        if(RecipeGuideBrowser.PointerOver())return true;
        if (_current is null || !Application.isFocused) return false;
        var p = Input.mousePosition;
        var point = new Vector2(p.x, Screen.height - p.y);
        return ToggleRect.Contains(point) || (_current._open && PanelRect.Contains(point));
    }

    internal void Update()
    {
        if (!DeveloperToolsSettings.Current.Enabled) return;
        if(RecipeGuideApi.IsVisible||WorkshopApi.IsVisible){_pressed=null;_shield.Stop();return;}
        _shield.Update(ToggleRect, PanelRect, _open);
        if (!Application.isFocused) { _pressed = null; return; }
        var mouse = Input.mousePosition;
        var point = new Vector2(mouse.x, Screen.height - mouse.y);
        if (Input.GetMouseButtonDown(0))
            _pressed = _buttons.FirstOrDefault(button => button.Area.Contains(point)).Id;
        if (Input.GetMouseButtonUp(0))
        {
            var pressed = _pressed;
            _pressed = null;
            var button = _buttons.FirstOrDefault(button => button.Id == pressed && button.Area.Contains(point));
            if (pressed is not null && button.Click is not null) button.Click();
        }
        if (_open && (Time.realtimeSinceStartupAsDouble >= _nextRefresh || _refresh))
        {
            _nextRefresh = Time.realtimeSinceStartupAsDouble + 1;
            _refresh = false;
            _rows = ItemRegistry.DebugRows();
            var store = PlayerStore.instance; // 不调用会主动创建单例的 Instance getter。
            _slot = store?.saveSlotId ?? -1;
            _run = store?.runID ?? "";
            _location = VisibleStoreInventory.IsReady
                ? L($"目标：屏幕底部主库存；存档槽 {_slot}（每次一件）", $"Target: visible main inventory; slot {_slot} (one item per click)")
                : L("进入店铺并显示主库存后，才可以生成物品", "Enter the store with the main inventory visible before generating items.");
        }
        var request = _pending;
        _pending = Request.None; // 先消费，一次点击只有一次原生提交机会。
        if(DebugBatch.Running&&request!=Request.None){_message=L("批量检查运行中，请等待结果自动导出。", "Batch checks are running; results will be exported.");return;}
        if (request == Request.UiPreview) { _message = NativeUiPreviewApi.ReloadFile(); EZapiBootstrap.WriteDiagnostics(); }
        if (request == Request.WildStress) {_message=WildNetworkBrowser.DebugStress();_open=false;}
        if (request == Request.Diagnostics)
            _message = EZapiBootstrap.WriteDiagnostics() ? L("诊断已写入 UserData/EZapi/diagnostics.json", "Diagnostics written to UserData/EZapi/diagnostics.json") : L("诊断写入失败，请查看日志", "Diagnostic export failed; check the log.");
        if (request == Request.Spawn)
            DebugItemSpawner.TrySpawnDebug(_pendingId, _pendingSlot, _pendingRun, out _message);
        if (request == Request.SpawnEmptyLiquid)
            DebugItemSpawner.TrySpawnDebug(_pendingId, _pendingSlot, _pendingRun, out _message,emptyLiquid:true);
        if (request == Request.Verify) _message=ItemDiagnostics.Run();
        if (request == Request.Localization) _message=DebugLocalization.Run();
        if (request == Request.Resources) _message=DebugResources.Run(_pendingDataRun);
        if (request == Request.Inventory) _message=DebugInventory.Run(_pendingDataRun);
        if (request == Request.SaveGraph) _message=DebugInventory.ExportFirstGraph(_pendingDataRun);
        if (request == Request.LifecycleTrace) _message=DebugLifecycle.Toggle();
        if (request == Request.Production) _message=DebugProduction.Run();
        if (request == Request.SaveGraphRoundTrip) _message=DebugSaveGraph.Run();
        if (request == Request.Registration) _message=DebugRegistration.Run();
        if (request == Request.NpcStock) _message=DebugNpcStock.Run();
        if (request == Request.ItemStock) _message=DebugItemStock.Run();
        if (request == Request.ItemInspection) _message=DebugItemInspection.Run();
        if (request == Request.Factories) _message=DebugNativeItems.Run();
        if (request == Request.SpriteAssets) _message=DebugSpriteAssets.Run();
        if (request == Request.Modules) _message=DebugModules.Run();
        if (request == Request.MachineLayouts) _message=DebugMachineLayouts.Run();
        if (request == Request.MachineTransactions) _message=DebugMachineTransactions.Run();
        if (request == Request.MachineRecipes) _message=DebugMachineRecipes.Run();
        if (request == Request.MachineAutomation) _message=DebugMachineAutomation.Run();
        if (request == Request.ManufacturingTerminal) _message=DebugManufacturingTerminal.Run();
        if (request == Request.NativeFurnace) _message=DebugNativeFurnace.Run();
        if (request == Request.SaveReadback) _message=DebugSaveReadback.Run();
        if (request == Request.Dialogue) {_message=DebugDialogue.Run();_open=false;}
        if (request == Request.DialogueShow) {_message=DebugDialogue.Show();_open=false;}
        if (request == Request.Phone) {_message=DebugPhone.Run();_open=false;}
        if (request == Request.PhoneShow) {_message=DebugPhone.Show();_open=false;}
        if(request==Request.Endpoint)_message=DebugInventoryEndpoint.Run();
        if(request==Request.EndpointPersist)_message=DebugInventoryEndpoint.Persist();
        if(request==Request.EndpointDelete)_message=DebugInventoryEndpoint.Remove();
        if(request==Request.Workshop){_message=DebugWorkshop.Open();_open=false;EZapiBootstrap.WriteDiagnostics();}
        if (request == Request.RunPurchase) _message=DebugRunPurchase.Run();
        if (request == Request.PhoneClear) _message=DebugPhone.ClearContact();
        if (request == Request.PhoneDial) {_message=DebugPhone.DialForLifecycle();_open=false;}
        if (request == Request.DialogueClose) _message=DebugDialogue.Close();
        if (request == Request.Supplier) {_message=DebugSupplier.Queue();_open=false;}
        if (request == Request.SupplierStatus) _message=DebugSupplier.Status();
        if (request == Request.Reading) _message=DebugReading.Run();
        if (request == Request.ReadingShow) {_message=DebugReading.Show();_open=false;}
        if (request == Request.ReadingClose) _message=DebugReading.Close();
        if (request == Request.ExternalPower) _message=DebugExternalPower.Run();
        if (request == Request.Power) _message=DebugPower.Run();
        if (request == Request.TerminalFixture) _message=DebugManufacturingTerminal.Fixture();
        if (request == Request.Persistence) _message=DebugMachinePersistence.Run();
        if (request == Request.MachineNight) _message=DebugMachineNight.Run();
        if (request == Request.MachineRegistration) _message=DebugMachineRegistration.Run();
        if (request == Request.MachineMaterials) _message=DebugMachineMaterials.Run();
        if (request == Request.RecipeGuide) _message=RecipeGuideApi.Open()?L("图鉴已打开。", "Recipe guide opened."):L("进入店铺并放下拖拽物品后再打开图鉴。", "Enter the store and put down dragged items before opening the guide.");
        if (request == Request.Batch) _message=DebugBatch.Start(_pendingDataRun);
        if (request == Request.LiquidPreview) _message=DebugLiquidTransfer.Preview(_pendingDataRun);
        if (request == Request.LiquidExecute) _message=DebugLiquidTransfer.Execute(_pendingDataRun);
        if (request == Request.FreezeFirst) _message=DebugInventory.FreezeFirst(_pendingDataRun);
        if (request == Request.ReleaseFreeze) _message=DebugInventory.ReleaseTest();
        if (request is Request.ReadRunData or Request.IncrementRunData)
            _message=DebugRunData.Execute(request==Request.IncrementRunData,_pendingDataRun);
        if(request is Request.ViewLoot or Request.RollLoot)
            _message=DebugLoot.Execute(request==Request.RollLoot,_pendingDataRun);
    }

    internal void SceneChanged()
    {
        _pressed = null;
        _pending = Request.None;
        _run = ""; _slot = -1;
        _pendingDataRun=null;
        _refresh = true;
    }

    internal void Draw()
    {
        _buttons.Clear();
        if (!DeveloperToolsSettings.Current.Enabled) return;
        if(RecipeGuideApi.IsVisible||WorkshopApi.IsVisible)return;
        if (_label is null)
        {
            _label = new GUIStyle(GUI.skin.label) { fontSize = 15, wordWrap = true };
            _button = new GUIStyle(GUI.skin.button) { fontSize = 15 };
            _title = new GUIStyle(_label) { fontSize = 18, fontStyle = FontStyle.Bold };
        }
        Button("toggle", ToggleRect, _open ? L("关闭 EZapi 调试", "Close EZapi tools") : L("EZapi 调试", "EZapi tools"), () =>
        {
            _open = !_open;
            _refresh = true;
            MelonLogger.Msg(_open ? "[EZapi] Debug panel opened." : "[EZapi] Debug panel closed.");
            EZapiBootstrap.WriteDiagnostics();
        });
        if (!_open) return;
        if (_messageEnglish != NativeItemLocalization.IsEnglish) { _message = ""; _messageEnglish = NativeItemLocalization.IsEnglish; _refresh = true; }
        if (_message.Length == 0) _message = L("示例物品尚未接入商店或掉落。进入店铺后，点击下方按钮生成一件。", "Enter the store, then explicitly generate an item. No automatic delivery or save.");
        var panel = PanelRect;
        GUI.Box(panel, "");
        var x = panel.x + 12;
        var width = panel.width - 24;
        GUI.Label(new Rect(x, panel.y + 10, width, 26), L($"EZapi 调试 · 已登记 {_rows.Count} 件物品", $"EZapi tools · {_rows.Count} registered items"), _title);
        GUI.Label(new Rect(x, panel.y + 40, 370, 25), _location, _label);
        if (!DeveloperToolsSettings.Current.RegressionFixtures)
        {
            DrawDeveloperPanel(x, width, panel);
            return;
        }
        Button("guide-locale-check",new Rect(x+380,panel.y+40,180,24),L("多界面中英检查", "Bilingual UI batch"),()=>_pending=Request.Localization);
        Button("diagnostics", new Rect(x, panel.y + 70, 138, 30), L("导出诊断", "Export diagnostics"), () => _pending = Request.Diagnostics);
        Button("verify", new Rect(x + 296, panel.y + 70, 180, 30), L("校验原生属性", "Check native fields"), () => _pending = Request.Verify);
        Button("batch", new Rect(x + 486, panel.y + 70, 164, 30), DebugBatch.Running?L("批量检查中…", "Checking batch..."):L("一次批量检查", "Run batch checks"), () => {_pendingDataRun=GameSession.Identity;_pending=Request.Batch;});
        Button("copy", new Rect(x + 148, panel.y + 70, 138, 30), L("复制全部 ID", "Copy all IDs"), () =>
        {
            GUIUtility.systemCopyBuffer = string.Join("\n", _rows.Select(row => row.Id));
            _message = L("物品 ID 已复制", "Item IDs copied");
        });
        Button("rundata-read",new Rect(x,panel.y+106,138,30),L("读取测试数", "Read test counter"),()=> {
            _pendingDataRun=GameSession.Identity;_pending=Request.ReadRunData;
        });
        Button("rundata-increment",new Rect(x+148,panel.y+106,138,30),L("测试数 +1", "Test counter +1"),()=> {
            _pendingDataRun=GameSession.Identity;_pending=Request.IncrementRunData;
        });
        Button("freeze-first",new Rect(x+296,panel.y+106,180,30),L("冻结主库存首件5秒", "Freeze first item 5s"),()=> {
            _pendingDataRun=GameSession.Identity;_pending=Request.FreezeFirst;
        });
        Button("release-freeze",new Rect(x+486,panel.y+106,164,30),L("解除测试冻结", "Release test freeze"),()=>_pending=Request.ReleaseFreeze);
        Button("loot-view",new Rect(x,panel.y+144,138,30),L("查看示例掉落", "Inspect sample loot"),()=> {
            _pendingDataRun=GameSession.Identity;_pending=Request.ViewLoot;
        });
        Button("loot-roll",new Rect(x+148,panel.y+144,138,30),L("试抽 ID（不发放）", "Roll ID (no delivery)"),()=> {
            _pendingDataRun=GameSession.Identity;_pending=Request.RollLoot;
        });
        Button("resources",new Rect(x+296,panel.y+144,180,30),L("读取物品/资源", "Inspect items/resources"),()=> {
            _pendingDataRun=GameSession.Identity;_pending=Request.Resources;
        });
        Button("inventory",new Rect(x+486,panel.y+144,164,30),L("读取库存", "Inspect inventory"),()=> {
            _pendingDataRun=GameSession.Identity;_pending=Request.Inventory;
        });
        Button("save-graph",new Rect(x,panel.y+182,180,30),L("导出首件物品图", "Export item graph"),()=> {
            _pendingDataRun=GameSession.Identity;_pending=Request.SaveGraph;
        });
        Button("lifecycle-trace",new Rect(x+190,panel.y+182,180,30),DebugLifecycle.Recording?L("停止阶段记录", "Stop lifecycle trace"):L("开启阶段记录", "Start lifecycle trace"),()=>_pending=Request.LifecycleTrace);
        Button("production",new Rect(x+380,panel.y+182,180,30),L("查看生产/效果", "Inspect production"),()=>_pending=Request.Production);
        Button("modules",new Rect(x+570,panel.y+182,80,30),L("模块检查", "Modules"),()=>_pending=Request.Modules);
        GUI.Label(new Rect(x, panel.y + 216, 370, 48), _message, _label);
        Button("dialogue-test",new Rect(x,panel.y+410,180,28),L("原生对话检查", "Dialogue checks"),()=>_pending=Request.Dialogue);
        Button("dialogue-show",new Rect(x+190,panel.y+410,120,28),L("看对话", "Show dialogue"),()=>_pending=Request.DialogueShow);
        Button("dialogue-close",new Rect(x+320,panel.y+410,120,28),L("关对话", "Close dialogue"),()=>_pending=Request.DialogueClose);
        Button("run-purchase",new Rect(x+450,panel.y+410,180,28),L("购买事务检查", "Purchase checks"),()=>_pending=Request.RunPurchase);
        Button("reading-test",new Rect(x+380,panel.y+216,180,28),L("原生阅读检查", "Reading checks"),()=>_pending=Request.Reading);
        Button("power-test",new Rect(x+190,panel.y+216,180,28),L("电力接口检查", "Power checks"),()=>_pending=Request.Power);
        Button("reading-show",new Rect(x+570,panel.y+216,80,28),L("看纸条", "Read note"),()=>_pending=Request.ReadingShow);
        Button("reading-close",new Rect(x+570,panel.y+246,80,22),L("关纸条", "Close note"),()=>_pending=Request.ReadingClose);
        Button("phone-test",new Rect(x,panel.y+450,180,28),L("原生电话检查", "Phone checks"),()=>_pending=Request.Phone);
        Button("phone-show",new Rect(x+190,panel.y+450,120,28),L("看通讯录", "Show contacts"),()=>_pending=Request.PhoneShow);
        Button("phone-clear",new Rect(x+320,panel.y+450,120,28),L("移除联系人", "Remove contact"),()=>_pending=Request.PhoneClear);
        Button("phone-dial",new Rect(x+450,panel.y+450,180,28),L("生命周期拨号", "Lifecycle dial"),()=>_pending=Request.PhoneDial);
        Button("endpoint-test",new Rect(x,panel.y+490,180,28),L("库存端点检查", "Inventory endpoints"),()=>_pending=Request.Endpoint);
        Button("endpoint-persist",new Rect(x+190,panel.y+490,180,28),L("端点保存夹具", "Endpoint save fixture"),()=>_pending=Request.EndpointPersist);
        Button("endpoint-delete",new Rect(x+380,panel.y+490,180,28),L("移除端点夹具", "Remove endpoint fixture"),()=>_pending=Request.EndpointDelete);
        Button("workshop-open",new Rect(x+570,panel.y+490,80,28),L("工坊预览", "Workshop"),()=>_pending=Request.Workshop);
        Button("supplier-queue",new Rect(x,panel.y+530,176,28),L("排入测试供应商", "Queue test supplier"),()=>_pending=Request.Supplier);
        Button("supplier-status",new Rect(x+190,panel.y+530,138,28),L("读取供应商状态", "Supplier status"),()=>_pending=Request.SupplierStatus);
        Button("item-inspection",new Rect(x+570,panel.y+530,80,30),L("单件估值", "Unit value"),()=>_pending=Request.ItemInspection);
        Button("savegraph-roundtrip",new Rect(x+570,panel.y+678,80,28),L("物品图恢复", "Restore"),()=>_pending=Request.SaveGraphRoundTrip);
        Button("registration",new Rect(x+570,panel.y+642,80,28),L("通用注册", "Register"),()=>_pending=Request.Registration);
        Button("npc-stock",new Rect(x+570,panel.y+606,80,28),L("NPC货源", "NPC stock"),()=>_pending=Request.NpcStock);
        Button("item-stock",new Rect(x+570,panel.y+570,80,28),L("补货检查", "Shop stock"),()=>_pending=Request.ItemStock);
        Button("sprite-assets",new Rect(x+380,panel.y+530,180,30),L("内嵌图集检查", "Embedded sprites"),()=>_pending=Request.SpriteAssets);
        var view = new Rect(x, panel.y + 570, width, Mathf.Max(30, panel.height - 582));
        const float rowHeight = 184;
        var pageSize = Mathf.Max(1, (int)(view.height / rowHeight));
        var pages = Mathf.Max(1, (_rows.Count + pageSize - 1) / pageSize);
        _page = Mathf.Clamp(_page, 0, pages - 1);
        Button("previous", new Rect(x, panel.y + 268, 90, 28), L("上一页", "Previous"), () => _page = Mathf.Max(0, _page - 1));
        Button("factories",new Rect(x,panel.y+302,180,28),L("物品工厂检查", "Item factories"),()=>_pending=Request.Factories);
        Button("machine-layouts",new Rect(x+190,panel.y+302,180,28),L("机器槽位检查", "Machine slots"),()=>_pending=Request.MachineLayouts);
        Button("recipe-guide",new Rect(x+380,panel.y+302,180,28),L("打开配方图鉴", "Recipe guide"),()=>_pending=Request.RecipeGuide);
        Button("machine-transactions",new Rect(x+570,panel.y+302,80,28),L("加工检查", "Process"),()=>_pending=Request.MachineTransactions);
        Button("machine-registration",new Rect(x+380,panel.y+338,180,28),L("机器注册检查", "Machine registration"),()=>_pending=Request.MachineRegistration);
        Button("machine-automation",new Rect(x+570,panel.y+338,80,28),L("自动化", "Automation"),()=>_pending=Request.MachineAutomation);
        Button("machine-persistence",new Rect(x+380,panel.y+10,180,28),L("复杂跨夜保存夹具", "Overnight save fixture"),()=>_pending=Request.Persistence);
        Button("machine-night",new Rect(x+570,panel.y+10,80,28),L("夜间夹具", "Night fixture"),()=>_pending=Request.MachineNight);
        Button("save-readback",new Rect(x+570,panel.y+40,80,24),L("存档回读", "Read save"),()=>_pending=Request.SaveReadback);
        Button("terminal-test",new Rect(x,panel.y+374,180,28),L("共享终端检查", "Shared terminal"),()=>_pending=Request.ManufacturingTerminal);
        Button("native-furnace",new Rect(x+570,panel.y+374,80,28),L("原版熔炉", "Furnace"),()=>_pending=Request.NativeFurnace);
        Button("external-power",new Rect(x+380,panel.y+374,180,28),L("外部电力检查", "External power"),()=>_pending=Request.ExternalPower);
        Button("terminal-fixture",new Rect(x+190,panel.y+374,180,28),L("终端保存夹具", "Terminal save fixture"),()=>_pending=Request.TerminalFixture);
        Button("machine-materials",new Rect(x+190,panel.y+338,180,28),L("材料保管检查", "Material escrow"),()=>_pending=Request.MachineMaterials);
        Button("machine-recipes",new Rect(x,panel.y+338,180,28),L("配方批次检查", "Recipe batches"),()=>_pending=Request.MachineRecipes);
        GUI.Label(new Rect(x + 102, panel.y + 270, 150, 25), $"{_page + 1} / {pages}", _label);
        Button("next", new Rect(x + 186, panel.y + 268, 90, 28), L("下一页", "Next"), () => _page = Mathf.Min(pages - 1, _page + 1));
        Button("liquid-preview",new Rect(x+296,panel.y+268,180,28),L("倒液预览", "Preview pouring"),()=>{_pendingDataRun=GameSession.Identity;_pending=Request.LiquidPreview;});
        Button("liquid-execute",new Rect(x+486,panel.y+268,164,28),L("执行预览≤1ml", "Apply preview <=1 ml"),()=>{_pendingDataRun=GameSession.Identity;_pending=Request.LiquidExecute;});
        DrawItemRows(x, width, view, pageSize);
    }

    // Public developer mode offers inspection and explicit one-item generation.
    // Regression-only tests, locale switching and persistent fixtures are omitted.
    private void DrawDeveloperPanel(float x, float width, Rect panel)
    {
        Button("diagnostics", new Rect(x, panel.y + 70, 138, 30), L("导出诊断", "Export diagnostics"), () => _pending = Request.Diagnostics);
        Button("copy", new Rect(x+148, panel.y+70, 138, 30), L("复制全部 ID", "Copy all IDs"), () => GUIUtility.systemCopyBuffer=string.Join("\n",_rows.Select(r=>r.Id)));
        Button("inventory", new Rect(x+296,panel.y+70,164,30), L("读取库存", "Inspect inventory"), () => {_pendingDataRun=GameSession.Identity;_pending=Request.Inventory;});
        Button("resources", new Rect(x+470,panel.y+70,180,30), L("读取物品/资源", "Inspect items/resources"), () => {_pendingDataRun=GameSession.Identity;_pending=Request.Resources;});
        Button("item-inspection",new Rect(x,panel.y+106,138,30),L("单件估值", "Unit value"),()=>_pending=Request.ItemInspection);
        Button("recipe-guide",new Rect(x+148,panel.y+106,180,30),L("打开配方图鉴", "Recipe guide"),()=>_pending=Request.RecipeGuide);
        Button("wild-stress",new Rect(x+338,panel.y+106,196,30),L("人脉滚动压力检查", "Network scroll probe"),()=>_pending=Request.WildStress);
        Button("ui-preview",new Rect(x+544,panel.y+106,106,30),L("UI 预览", "UI preview"),()=>_pending=Request.UiPreview);
        GUI.Label(new Rect(x,panel.y+144,width,48),_message,_label);
        var view=new Rect(x,panel.y+230,width,Mathf.Max(30,panel.height-242));
        var pageSize=Mathf.Max(1,(int)(view.height/184));
        var pages=Mathf.Max(1,(_rows.Count+pageSize-1)/pageSize);
        _page=Mathf.Clamp(_page,0,pages-1);
        Button("previous",new Rect(x,panel.y+194,90,28),L("上一页", "Previous"),()=>_page=Mathf.Max(0,_page-1));
        GUI.Label(new Rect(x+102,panel.y+196,150,25),$"{_page+1} / {pages}",_label);
        Button("next",new Rect(x+186,panel.y+194,90,28),L("下一页", "Next"),()=>_page=Mathf.Min(pages-1,_page+1));
        DrawItemRows(x,width,view,pageSize);
    }

    private void DrawItemRows(float x, float width, Rect view, int pageSize)
    {
        const float rowHeight=184;
        for (var i = _page * pageSize; i < Mathf.Min(_rows.Count, (_page + 1) * pageSize); i++)
        {
            var y = view.y + (i % pageSize) * rowHeight;
            var row = _rows[i];
            GUI.Label(new Rect(x, y, width, 24), $"{row.Name}  [{row.State}]", _label);
            GUI.Label(new Rect(x, y + 26, width, 64), $"ID: {row.Id}\nPNG: {row.SpritePath ?? L("默认占位图", "Default placeholder")}", _label);
            GUI.Label(new Rect(x, y + 92, width, 24), row.Summary, _label);
            Button("spawn:" + row.Id, new Rect(x, y + 122, 176, 32), L("生成到店铺主库存", "Generate item"), () =>
            {
                _pendingId = row.Id;
                _pendingSlot = _slot; _pendingRun = _run;
                _pending = Request.Spawn;
                _message = L("正在验证目录与库存…", "Checking item directory and inventory...");
            });
            GUI.Label(new Rect(x + 188, y + 124, width - 188, 40), L("请使用测试档；每次 1 件，不自动保存", "Use a test save; one item per click, no automatic save."), _label);
            if(ItemRegistry.TryGet(row.Id,out var definition)&&definition.LiquidContainer is not null)
                Button("spawn-empty:"+row.Id,new Rect(x,y+158,176,28),L("生成新空瓶（测试）", "Empty bottle"),()=> {
                    _pendingId=row.Id;_pendingSlot=_slot;_pendingRun=_run;_pending=Request.SpawnEmptyLiquid;
                });
        }
    }

    internal void Stop()
    {
        _shield.Stop();
        if (_current == this) _current = null;
        SceneChanged();
        _rows = Array.Empty<DebugItemRow>();
        _label = null; _button = null; _title = null;
    }
}

