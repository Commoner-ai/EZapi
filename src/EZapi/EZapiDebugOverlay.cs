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
    private enum Request { None, WildStress, Persistence, Diagnostics, Spawn, SpawnEmptyLiquid, Verify, ReadRunData, IncrementRunData, ViewLoot, RollLoot, Localization, Resources, Inventory, FreezeFirst, ReleaseFreeze, SaveGraph, LifecycleTrace, Production, Batch, LiquidPreview, LiquidExecute, Modules, Factories, SpriteAssets, MachineLayouts, RecipeGuide, MachineTransactions, MachineRecipes, MachineMaterials, MachineRegistration, MachineAutomation, MachineNight, ManufacturingTerminal, TerminalFixture, ExternalPower, Power, NativeFurnace, SaveReadback, Reading, ReadingShow, ReadingClose, Dialogue, DialogueShow, DialogueClose, Supplier, SupplierStatus,Phone,PhoneShow,PhoneClear,PhoneDial,RunPurchase,Endpoint,EndpointPersist,EndpointDelete,Workshop,ItemInspection,ItemStock,NpcStock,Registration,SaveGraphRoundTrip }
    private static EZapiDebugOverlay? _current;
    private IReadOnlyList<DebugItemRow> _rows = Array.Empty<DebugItemRow>();
    private bool _open, _refresh = true;
    private double _nextRefresh;
    private Request _pending;
    private string _pendingId = "", _run = "", _pendingRun = "";
    private int _slot = -1, _pendingSlot;
    private GameRunIdentity? _pendingDataRun;
    private string _message = "示例物品尚未接入商店或掉落。进入店铺后，点击下方按钮生成一件。";
    private string _location = "未进入店铺";
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
                ? $"目标：屏幕底部主库存；存档槽 {_slot}（每次一件）"
                : "进入店铺并显示主库存后，才可以生成物品";
        }
        var request = _pending;
        _pending = Request.None; // 先消费，一次点击只有一次原生提交机会。
        if(DebugBatch.Running&&request!=Request.None){_message="批量检查运行中，请等待结果自动导出。";return;}
        if (request == Request.WildStress) {_message=WildNetworkBrowser.DebugStress();_open=false;}
        if (request == Request.Diagnostics)
            _message = EZapiBootstrap.WriteDiagnostics() ? "诊断已写入 UserData/EZapi/diagnostics.json" : "诊断写入失败，请查看日志";
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
        if (request == Request.RecipeGuide) _message=RecipeGuideApi.Open()?"图鉴已打开。":"进入店铺并放下拖拽物品后再打开图鉴。";
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
        Button("toggle", ToggleRect, _open ? "关闭 EZapi 调试" : "EZapi 调试", () =>
        {
            _open = !_open;
            _refresh = true;
            MelonLogger.Msg(_open ? "[EZapi] Debug panel opened." : "[EZapi] Debug panel closed.");
            EZapiBootstrap.WriteDiagnostics();
        });
        if (!_open) return;
        var panel = PanelRect;
        GUI.Box(panel, "");
        var x = panel.x + 12;
        var width = panel.width - 24;
        GUI.Label(new Rect(x, panel.y + 10, width, 26), $"EZapi 调试 · 已登记 {_rows.Count} 件物品", _title);
        GUI.Label(new Rect(x, panel.y + 40, 370, 25), _location, _label);
        if (!DeveloperToolsSettings.Current.RegressionFixtures)
        {
            DrawDeveloperPanel(x, width, panel);
            return;
        }
        Button("guide-locale-check",new Rect(x+380,panel.y+40,180,24),"多界面中英检查",()=>_pending=Request.Localization);
        Button("diagnostics", new Rect(x, panel.y + 70, 138, 30), "导出诊断", () => _pending = Request.Diagnostics);
        Button("verify", new Rect(x + 296, panel.y + 70, 180, 30), "校验原生属性", () => _pending = Request.Verify);
        Button("batch", new Rect(x + 486, panel.y + 70, 164, 30), DebugBatch.Running?"批量检查中…":"一次批量检查", () => {_pendingDataRun=GameSession.Identity;_pending=Request.Batch;});
        Button("copy", new Rect(x + 148, panel.y + 70, 138, 30), "复制全部 ID", () =>
        {
            GUIUtility.systemCopyBuffer = string.Join("\n", _rows.Select(row => row.Id));
            _message = "物品 ID 已复制";
        });
        Button("rundata-read",new Rect(x,panel.y+106,138,30),"读取测试数",()=> {
            _pendingDataRun=GameSession.Identity;_pending=Request.ReadRunData;
        });
        Button("rundata-increment",new Rect(x+148,panel.y+106,138,30),"测试数 +1",()=> {
            _pendingDataRun=GameSession.Identity;_pending=Request.IncrementRunData;
        });
        Button("freeze-first",new Rect(x+296,panel.y+106,180,30),"冻结主库存首件5秒",()=> {
            _pendingDataRun=GameSession.Identity;_pending=Request.FreezeFirst;
        });
        Button("release-freeze",new Rect(x+486,panel.y+106,164,30),"解除测试冻结",()=>_pending=Request.ReleaseFreeze);
        Button("loot-view",new Rect(x,panel.y+144,138,30),"查看示例掉落",()=> {
            _pendingDataRun=GameSession.Identity;_pending=Request.ViewLoot;
        });
        Button("loot-roll",new Rect(x+148,panel.y+144,138,30),"试抽 ID（不发放）",()=> {
            _pendingDataRun=GameSession.Identity;_pending=Request.RollLoot;
        });
        Button("resources",new Rect(x+296,panel.y+144,180,30),"读取物品/资源",()=> {
            _pendingDataRun=GameSession.Identity;_pending=Request.Resources;
        });
        Button("inventory",new Rect(x+486,panel.y+144,164,30),"读取库存",()=> {
            _pendingDataRun=GameSession.Identity;_pending=Request.Inventory;
        });
        Button("save-graph",new Rect(x,panel.y+182,180,30),"导出首件物品图",()=> {
            _pendingDataRun=GameSession.Identity;_pending=Request.SaveGraph;
        });
        Button("lifecycle-trace",new Rect(x+190,panel.y+182,180,30),DebugLifecycle.Recording?"停止阶段记录":"开启阶段记录",()=>_pending=Request.LifecycleTrace);
        Button("production",new Rect(x+380,panel.y+182,180,30),"查看生产/效果",()=>_pending=Request.Production);
        Button("modules",new Rect(x+570,panel.y+182,80,30),"模块检查",()=>_pending=Request.Modules);
        GUI.Label(new Rect(x, panel.y + 216, 370, 48), _message, _label);
        Button("dialogue-test",new Rect(x,panel.y+410,180,28),"原生对话检查",()=>_pending=Request.Dialogue);
        Button("dialogue-show",new Rect(x+190,panel.y+410,120,28),"看对话",()=>_pending=Request.DialogueShow);
        Button("dialogue-close",new Rect(x+320,panel.y+410,120,28),"关对话",()=>_pending=Request.DialogueClose);
        Button("run-purchase",new Rect(x+450,panel.y+410,180,28),"购买事务检查",()=>_pending=Request.RunPurchase);
        Button("reading-test",new Rect(x+380,panel.y+216,180,28),"原生阅读检查",()=>_pending=Request.Reading);
        Button("power-test",new Rect(x+190,panel.y+216,180,28),"电力接口检查",()=>_pending=Request.Power);
        Button("reading-show",new Rect(x+570,panel.y+216,80,28),"看纸条",()=>_pending=Request.ReadingShow);
        Button("reading-close",new Rect(x+570,panel.y+246,80,22),"关纸条",()=>_pending=Request.ReadingClose);
        Button("phone-test",new Rect(x,panel.y+450,180,28),"原生电话检查",()=>_pending=Request.Phone);
        Button("phone-show",new Rect(x+190,panel.y+450,120,28),"看通讯录",()=>_pending=Request.PhoneShow);
        Button("phone-clear",new Rect(x+320,panel.y+450,120,28),"移除联系人",()=>_pending=Request.PhoneClear);
        Button("phone-dial",new Rect(x+450,panel.y+450,180,28),"生命周期拨号",()=>_pending=Request.PhoneDial);
        Button("endpoint-test",new Rect(x,panel.y+490,180,28),"库存端点检查",()=>_pending=Request.Endpoint);
        Button("endpoint-persist",new Rect(x+190,panel.y+490,180,28),"端点保存夹具",()=>_pending=Request.EndpointPersist);
        Button("endpoint-delete",new Rect(x+380,panel.y+490,180,28),"移除端点夹具",()=>_pending=Request.EndpointDelete);
        Button("workshop-open",new Rect(x+570,panel.y+490,80,28),"工坊预览",()=>_pending=Request.Workshop);
        Button("supplier-queue",new Rect(x,panel.y+530,176,28),"排入测试供应商",()=>_pending=Request.Supplier);
        Button("supplier-status",new Rect(x+190,panel.y+530,138,28),"读取供应商状态",()=>_pending=Request.SupplierStatus);
        Button("item-inspection",new Rect(x+570,panel.y+530,80,30),"单件估值",()=>_pending=Request.ItemInspection);
        Button("savegraph-roundtrip",new Rect(x+570,panel.y+678,80,28),"物品图恢复",()=>_pending=Request.SaveGraphRoundTrip);
        Button("registration",new Rect(x+570,panel.y+642,80,28),"通用注册",()=>_pending=Request.Registration);
        Button("npc-stock",new Rect(x+570,panel.y+606,80,28),"NPC货源",()=>_pending=Request.NpcStock);
        Button("item-stock",new Rect(x+570,panel.y+570,80,28),"补货检查",()=>_pending=Request.ItemStock);
        Button("sprite-assets",new Rect(x+380,panel.y+530,180,30),"内嵌图集检查",()=>_pending=Request.SpriteAssets);
        var view = new Rect(x, panel.y + 570, width, Mathf.Max(30, panel.height - 582));
        const float rowHeight = 184;
        var pageSize = Mathf.Max(1, (int)(view.height / rowHeight));
        var pages = Mathf.Max(1, (_rows.Count + pageSize - 1) / pageSize);
        _page = Mathf.Clamp(_page, 0, pages - 1);
        Button("previous", new Rect(x, panel.y + 268, 90, 28), "上一页", () => _page = Mathf.Max(0, _page - 1));
        Button("factories",new Rect(x,panel.y+302,180,28),"物品工厂检查",()=>_pending=Request.Factories);
        Button("machine-layouts",new Rect(x+190,panel.y+302,180,28),"机器槽位检查",()=>_pending=Request.MachineLayouts);
        Button("recipe-guide",new Rect(x+380,panel.y+302,180,28),"打开配方图鉴",()=>_pending=Request.RecipeGuide);
        Button("machine-transactions",new Rect(x+570,panel.y+302,80,28),"加工检查",()=>_pending=Request.MachineTransactions);
        Button("machine-registration",new Rect(x+380,panel.y+338,180,28),"机器注册检查",()=>_pending=Request.MachineRegistration);
        Button("machine-automation",new Rect(x+570,panel.y+338,80,28),"自动化",()=>_pending=Request.MachineAutomation);
        Button("machine-persistence",new Rect(x+380,panel.y+10,180,28),"复杂跨夜保存夹具",()=>_pending=Request.Persistence);
        Button("machine-night",new Rect(x+570,panel.y+10,80,28),"夜间夹具",()=>_pending=Request.MachineNight);
        Button("save-readback",new Rect(x+570,panel.y+40,80,24),"存档回读",()=>_pending=Request.SaveReadback);
        Button("terminal-test",new Rect(x,panel.y+374,180,28),"共享终端检查",()=>_pending=Request.ManufacturingTerminal);
        Button("native-furnace",new Rect(x+570,panel.y+374,80,28),"原版熔炉",()=>_pending=Request.NativeFurnace);
        Button("external-power",new Rect(x+380,panel.y+374,180,28),"外部电力检查",()=>_pending=Request.ExternalPower);
        Button("terminal-fixture",new Rect(x+190,panel.y+374,180,28),"终端保存夹具",()=>_pending=Request.TerminalFixture);
        Button("machine-materials",new Rect(x+190,panel.y+338,180,28),"材料保管检查",()=>_pending=Request.MachineMaterials);
        Button("machine-recipes",new Rect(x,panel.y+338,180,28),"配方批次检查",()=>_pending=Request.MachineRecipes);
        GUI.Label(new Rect(x + 102, panel.y + 270, 150, 25), $"{_page + 1} / {pages}", _label);
        Button("next", new Rect(x + 186, panel.y + 268, 90, 28), "下一页", () => _page = Mathf.Min(pages - 1, _page + 1));
        Button("liquid-preview",new Rect(x+296,panel.y+268,180,28),"倒液预览",()=>{_pendingDataRun=GameSession.Identity;_pending=Request.LiquidPreview;});
        Button("liquid-execute",new Rect(x+486,panel.y+268,164,28),"执行预览≤1ml",()=>{_pendingDataRun=GameSession.Identity;_pending=Request.LiquidExecute;});
        DrawItemRows(x, width, view, pageSize);
    }

    // Public developer mode offers inspection and explicit one-item generation.
    // Regression-only tests, locale switching and persistent fixtures are omitted.
    private void DrawDeveloperPanel(float x, float width, Rect panel)
    {
        Button("diagnostics", new Rect(x, panel.y + 70, 138, 30), "导出诊断", () => _pending = Request.Diagnostics);
        Button("copy", new Rect(x+148, panel.y+70, 138, 30), "复制全部 ID", () => GUIUtility.systemCopyBuffer=string.Join("\n",_rows.Select(r=>r.Id)));
        Button("inventory", new Rect(x+296,panel.y+70,164,30), "读取库存", () => {_pendingDataRun=GameSession.Identity;_pending=Request.Inventory;});
        Button("resources", new Rect(x+470,panel.y+70,180,30), "读取物品/资源", () => {_pendingDataRun=GameSession.Identity;_pending=Request.Resources;});
        Button("item-inspection",new Rect(x,panel.y+106,138,30),"单件估值",()=>_pending=Request.ItemInspection);
        Button("recipe-guide",new Rect(x+148,panel.y+106,180,30),"打开配方图鉴",()=>_pending=Request.RecipeGuide);
        Button("wild-stress",new Rect(x+338,panel.y+106,196,30),"人脉滚动压力检查",()=>_pending=Request.WildStress);
        GUI.Label(new Rect(x,panel.y+144,width,48),_message,_label);
        var view=new Rect(x,panel.y+230,width,Mathf.Max(30,panel.height-242));
        var pageSize=Mathf.Max(1,(int)(view.height/184));
        var pages=Mathf.Max(1,(_rows.Count+pageSize-1)/pageSize);
        _page=Mathf.Clamp(_page,0,pages-1);
        Button("previous",new Rect(x,panel.y+194,90,28),"上一页",()=>_page=Mathf.Max(0,_page-1));
        GUI.Label(new Rect(x+102,panel.y+196,150,25),$"{_page+1} / {pages}",_label);
        Button("next",new Rect(x+186,panel.y+194,90,28),"下一页",()=>_page=Mathf.Min(pages-1,_page+1));
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
            GUI.Label(new Rect(x, y + 26, width, 64), $"ID: {row.Id}\nPNG: {row.SpritePath ?? "默认占位图"}", _label);
            GUI.Label(new Rect(x, y + 92, width, 24), row.Summary, _label);
            Button("spawn:" + row.Id, new Rect(x, y + 122, 176, 32), "生成到店铺主库存", () =>
            {
                _pendingId = row.Id;
                _pendingSlot = _slot; _pendingRun = _run;
                _pending = Request.Spawn;
                _message = "正在验证目录与库存…";
            });
            GUI.Label(new Rect(x + 188, y + 124, width - 188, 40), "请使用测试档；每次 1 件，不自动保存", _label);
            if(ItemRegistry.TryGet(row.Id,out var definition)&&definition.LiquidContainer is not null)
                Button("spawn-empty:"+row.Id,new Rect(x,y+158,176,28),"生成新空瓶（测试）",()=> {
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

