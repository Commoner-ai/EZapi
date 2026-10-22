# 自适应外出地点按钮

`DepartureApi` 把用户搜打撤项目的 `RaidEntry` / `MapButtonLayout` 抽成框架服务。入口位于游戏原生“离开店铺”的地点地图，跟随游戏本身的离店条件。它不创建店铺HUD的强制出门按钮，也不绕过教学、开门或关门条件。

## 最短接入

在自己的 MelonMod `OnInitializeMelon` 内登记；EZapi优先初始化。`Queued` 表示声明被接受，地图尚未打开时不会创建对象。

```csharp
var result = DepartureApi.Register("author.raid", "author.raid.entry", "外出探索", () => MyRaid.Open());
// 自己的OnDeinitializeMelon内调用：
DepartureApi.Unregister("author.raid", "author.raid.entry");
```

`MyRaid.Open()` 是作者自己的目的地逻辑，不是EZapi接口。可编译完整示例见[DepartureExamples.cs](../src/EZapi.Sample/DepartureExamples.cs)。最简重载默认要求剩余外出次数大于0；不需要次数的菜单入口应使用完整声明并明确设置 `RequireVisit=false`。

无框架源码的独立消费方工程见 [DepartureMod.csproj](examples/departure-dll/DepartureMod.csproj)。在开发包根目录运行 `dotnet build docs/examples/departure-dll/DepartureMod.csproj -c Release -p:GameDir='<游戏目录>'`，只引用包内 `Mods/EZapi.dll`。生成的示例DLL不会自动安装；按需复制到游戏Mods。示例仅写日志，不进入目的地、不扣次数。

```csharp
DepartureApi.Register("author.raid", new DepartureButton("author.raid.entry", "Raid", context => MyRaid.Open())
{
    DisplayName = context => context.Language.Resolve(new LocalizedItemText("外出探索", "Raid")),
    Visible = context => true,
    CanEnter = context => MyRaid.CanOpen(),
    Order = 100,
    RequireVisit = true,
    ReserveLegacySlots = true
});
```

`Visible`、`CanEnter`、`DisplayName` 必须只读，不注册/注销、不切场景、不扣款、不发物品、不保存。回调每约0.25秒查询一次，点击前重新核对当前地图、存档代次、剩余次数和条件。注册/注销的重入返回 `Conflict`。`DisplayName` 可使用 `context.Language.Resolve(catalog,key,fallback)` 从CSV读取任何语言；没有这个委托时使用静态Label。不要跨回调缓存context作为以后写存档的凭据。

## 自适应与兼容边界

- 克隆当前版本原生地点模板，沿用字体、尺寸、缩放、锚点和旋转；清掉克隆上的原始点击监听及本地化组件，再绑定作者回调。原版控件不变。
- 使用实际世界坐标转换到同一父节点坐标，扫描地图中的可见Graphic和Selectable，占位包含图文和按钮，而非只检查Button子物体。
- 优先使用原搜打撤的左/中/右候选列，再有界扫描其余屏幕空位。按Order/Id统一排序EZapi入口，已分配位置也成为障碍；最多64个声明，扫描上限4096，不做无限搜索。
- 默认为UpperDistrict会动态占用的四行预留位置。所有可见EZapi入口都明确关闭ReserveLegacySlots时才取消预留；不移动该Mod的按钮。
- 服从屏幕和祖先Mask/RectMask2D的边界。非裁切的零尺寸布局父节点不被当成屏幕边界。地图坐标不等于固定1920×1080像素。
- 无空位的入口隐藏并标记 `EZAPI_DEPARTURE_NO_SPACE`，不覆盖他人按钮。其它已找到空位的入口可正常展示。
- 关闭地图、换地图对象/父节点、切场景或重新读档时，撤掉监听、隐藏并销毁本服务的对象。声明保留，下一次打开地图时重建；注销/框架关闭时删除声明。旧点击闭包先验证代次，不能作用到新存档。
- 本适配不安装Harmony，不改地图原始按钮事件，不依赖Forge/搜打撤DLL。对未知游戏程序集指纹，保留声明但不启用原生UI适配。

占位观察每0.25秒刷新。另一个Mod若在之后同帧强行移动按钮，不能保证该帧完全无重叠；框架只移动自己的入口。旋转/特殊模板、其它Mod组合仍需实机检查，不能将离线几何验证视为全面兼容认证。

## 状态、职责与JSON

`DepartureApi.IsAvailable` 表示适配门控可用；不表示地图打开。`DepartureApi.Snapshot()` 和诊断文件 `departureButtons` 给出每项Applied/Visible/Placed/Interactable/Error；Applied仅表示创建过，Placed才表示本次找到空位。

此接口只负责UI和点击分发。作者自行接回搜打撤/大厅的现有目的地控制器，维护进入/退出/物品归还/交易状态隔离，负责次数消耗与持久幂等。框架不自动调用原生垃圾场、CloseUI、OnGoingOutside、OnReturning或保存函数，也不在失败后自动重试点击业务。

本版提供C#行为注册和[AI可读JSON索引](departure-buttons.index.json)，不把JSON字符串当方法名执行，不新增无法实现回调的假内容包入口。纯JSON包不能直接定义任意旅行逻辑。

## 验收

离线运行 `dotnet run --project work/ezapi-departure-tests/DepartureTests.csproj -c Release`。该工程链接实际注册/回调与布局源码，检查外部占位、分辨率变化、满屏无空位、重复注册、所有权、重入和过期身份。

要在可丢弃档实机核对，可加载自己的示例调用者，或在开发工具设置显式启用 `enabled=true, regressionFixtures=true` 并重启；两个“外出入口测试A/B”只打开只读说明，不消耗次数或保存。按游戏规则开门、关门，从店铺最左端离店打开地图；检查两入口避让原版按钮。更换分辨率/语言，关闭重开地图、重新读档，确认无重复按钮；点击A/B应每次只到达一个对应回调。正常游戏不启用回归夹具。

当前实现已安排构建与离线验收；实机结果以版本/哈希对应的验证报告为准。
