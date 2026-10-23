# 原版与 Mod 全物品目录

适用：商店选货、图鉴、订货系统，以及“基础价值超过某金额的全部物品”。不需要自己反射游戏目录或在每张卡片上创建物品。版本与当前 DLL 验收见 [release-status.json](release-status.json)。

| 需求 | 入口 | 会执行工厂吗 |
| --- | --- | --- |
| 只列 EZapi 声明与应用状态 | `ItemApi.Snapshot()` / `FactorySnapshot()` | 不会 |
| 列出原版及其他 Mod 的真实物品 ID | `ItemCatalogApi.CaptureIdentifiers()` | 不会 |
| 读取全目录的原生名称、基础价值、原图路径 | `ItemCatalogApi.BeginScan()`，之后逐帧 `Step()` | 会，每 ID 一次临时创建 |
| 只查指定 ID 子集 | `BeginScan(ids)` | Step 时执行 |

## 最短调用

在**已载入周目的游戏主线程**开始，不能在初始化或 OnGUI 中执行工厂扫描：

```csharp
ItemCatalogScan? scan;

void Begin()
{
    scan?.Dispose();
    scan = ItemCatalogApi.BeginScan();
}

void Update()
{
    if (scan is null) return;
    var progress = scan.Step(); // 每次最多 4 个工厂，工厂之间检查 4ms 预算
    if (progress.Status == ItemCatalogStatus.Reading) return;
    if (progress.Status == ItemCatalogStatus.Complete)
    {
        var pool = scan.Snapshot().Where(x => x.BaseValue > 150).ToArray();
        // DisplayName 已来自本次游戏语言。BaseValue 为 long，不是成交价。
        // SpriteAtlasPath/SpritePath 是原图索引，不表示图片可读或已通过黑图检查。
        foreach (var failure in scan.Failures) Log(failure.Id + ": " + failure.Reason);
    }
    scan.Dispose();
    scan = null;
}
```

完整可编译调用者见 [Catalog.cs](examples/item-catalog-dll/Catalog.cs) 与 [工程](examples/item-catalog-dll/Catalog.csproj)。没有后台线程或隐式更新器；作者在自己的 Update 调用 Step，换场景时 Dispose 并重建。

在解压的开发包根目录运行 `dotnet build docs/examples/item-catalog-dll/Catalog.csproj -c Release`，默认引用包内唯一的 `Mods/EZapi.dll`，不需要游戏程序集。源码工作区构建或复制工程到其他位置时，加 `-p:EZapiDll=你的EZapi.dll完整路径`；也可通过 `-p:GameDir=游戏目录` 引用实装 DLL。该示例是可复用的目录读取辅助类，接入者仍须从自己的 Mod 调用 Begin/Tick/Dispose。

## 完整性、语言与资源

- 全目录由当前游戏的 `DirectoryMaster.GetIdentifierList<GameItem>()` 获取，包括原版、EZapi 和原生目录中的其他 Mod。ID 复制、去重、按 ordinal 排序。最多 65,536 个原始目录项，超限明确报错，绝不截取前 N 项后假装完整。动态改变目录后需重新开始；不是持续监听器。
- `BeginScan()` 仅捕获 ID；Step 才通过 `ItemApi.Create(id, owned:false)` 读取每个工厂。临时游离物品在读后 Destroy，不送库存、不扣费、不请求保存。**其他 Mod 的工厂会执行自己的代码**；必须按工厂契约返回新游离实例，不能在构造时发奖励、保存、改场景或返回现有库存物品。框架不能撤销任意第三方工厂副作用。
- Step 的预算是工厂调用数与调用间的时间检查，不能中断一个已开始的原生工厂。参数范围为 1..64 项和大于 0、不超过 50ms；默认 4 项/4ms。不要在同一帧循环 Step 到结束。
- `Complete` 表示本次 ID 集合全部尝试完毕，**并非每个工厂都成功**。Snapshot 仅含成功条目；Failures 逐项保留失败 ID/错误类型，不能静默忽略。扫描中、取消、过期或全局清理失败时 Snapshot 拒绝返回，防止把半份货池用于每日抽取。
- 名称沿用原生 GetDisplayName 和本次游戏 locale；切到英语后重新扫描获得英文名。扫描绑定 SaveSlotId/RunId/Epoch 和 locale，同槽重载也失效。已取出的纯托管快照不会自动换语言，作者须在新语言/周目重建页面和货池，不能长期缓存成固定中文。
- 游戏语言代码变更不表示语言表已就绪。开始扫描会复用原生 UI 的异步语言就绪检查；切换途中以 `EZAPI_CATALOG_LOCALE` 拒绝开始，等待后重新 Begin。接口不切换游戏语言，也不调用同步 WaitForCompletion 强行加载语言表。
- BaseValue 来自原生 GetBaseValue，单位为信用点，类型为 long，不含未来成交、热度、当前事件等最终交易结算。价格策略与限价由调用 Mod 决定。
- 全目录不是“可直接出售的成品清单”。`BaseValue > 150` 只是查询示例，不能代替货源准入规则；箱子、奖励包、任务物等可能需要额外的原生生成、填充或上锁流程。扫描不执行这些玩法流程，也不保证普通目录工厂返回的模板适合直接发货。需要模仿小偷或供应商货池时，应按对应原生货池和生成流程取货。
- 图像只返回原生图集/切片路径；它不解码、不导出原版图片、不检查像素、黑图、透明图或 GPU 支持。需要原图的调用者可在主线程使用原生 RenderHandler.LoadFromAtlasRaw，并为自己的货源规则单独验证与缓存。路径非空不是有效美术证据。
- 扫描对象只持有字符串和托管 DTO；跨载入不持有 GameItem/Sprite/库存引用。临时物品清理异常会中止整个扫描，避免重复积累未清理对象。

## 调试与错误

启用普通开发工具后，调试面板有“导出原版与 Mod 全目录”。点击才开始分帧扫描，结果导出到游戏 `UserData/EZapi/item-catalog.json`；诊断摘要在 `debugUi.itemCatalog`。该入口会执行物品工厂，先用测试存档；不会自动保存。

`EZAPI_CATALOG_NOT_READY/BUILD/LOCALE` 表示周目、构建或真实语言未就绪；`ID/LIMIT/BUDGET` 是输入/目录边界；`REENTRY` 是工厂中递归操作；`STALE` 要求重建扫描；`FACTORY` 只影响对应条目；`CLEANUP` 停止整个扫描。见 [机器可读索引](item-catalog.index.json) 和 [错误索引](errors.index.json)。离线边界测试不能证明原生工厂与所有 Mod 组合安全，验收范围以当前版本状态为准。
