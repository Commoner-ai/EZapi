# 嵌入式 Sprite 图集与肖像（0.1.47）

`SpriteAssets` 让 C# Mod 把 PNG 作为程序集资源交给 EZapi 预检，再由 EZapi 安装到自己命名空间下的 Unity 图集。当前游戏构建已核实 `RenderHandler.atlasCache` 与 `GameItem.SetSprite`；EZapi 不安装全局图片解析补丁。

## 准备资源

在 Mod 的 `.csproj` 把 PNG 嵌入程序集，并设置稳定资源名：

```xml
<EmbeddedResource Include="Textures/Items/field_kit.png"
                  LogicalName="MyMod.Textures.Items.field_kit.png" />
```

`ResourceName` 必须与 `LogicalName` 完全一致。定义 `Width` / `Height` 是物品占用的格子宽高，不是 PNG 像素尺寸；形状仍要由物品定义声明。Pivot 使用 0 到 1 的纹理归一化坐标。

## 声明、安装和绑定

仓库内有可编译的调用例子：[SpriteAssetExamples.cs](../src/EZapi.Sample/SpriteAssetExamples.cs)。基本流程如下：

```csharp
var atlas = SpriteAssets.CreateEmbeddedAtlas(
    "mymod", "items", typeof(MyMod).Assembly,
    new[] { new SpriteDefinition("field-kit", "MyMod.Textures.Items.field_kit.png", 1, 1)
        { PivotX = 0.5f, PivotY = 0.5f } },
    SpriteFilter.Point);

// 在游戏主线程且游戏图片缓存已就绪后调用；未就绪时可在下一次官方游戏事件重试。
if (!atlas.TryInstall(
    onInstalled: () => LoggerInstance.Msg("item atlas ready"),
    onFailure: error => LoggerInstance.Error(error.ToString())))
    return;

var icon = atlas.GetSprite("field-kit"); // 未安装或 key 不存在时返回 null
atlas.Assign(liveItem, "field-kit");      // 使用 GameItem.SetSprite 并核对回读
```

`CreateEmbeddedAtlas` 只读取资源并预检，不创建 Unity 对象或改原生缓存。每个图集最多 128 张 PNG、每张最多 4 MiB、合计最多 16 MiB；每边最多 2048 像素。PNG 签名、IHDR、占格尺寸、Pivot、筛选模式、重复 key 和缺失资源均在原生图集改变前检查。图集 ID 为 `EZapi.Assets/<ownerId>/<atlasKey>`；遇到已有同名图集会拒绝整个安装，不会覆盖别的 Mod。

`TryInstall` 成功后重复调用是幂等的；原生缓存尚未就绪时结果可重试，永久资源错误或命名冲突会令该句柄 `Failed`。`onInstalled` 只在首次成功安装时调用。`allowStaging: true` 可让 `Assign` 在安装前先写入物品的图集/精灵 ID，但游戏开始绘制该物品前仍必须成功安装图集。EZapi 卸载时统一释放这些 Unity 资源；不要把 Sprite 或 GameItem 缓存到周目、场景或保存数据中。

## 兼容边界

官方指南允许模组客户使用自定义精灵图。本机构建没有指南较新版的统一资源便捷方法；0.1.47 单点适配其公开的 `SpriteDict.Instance.spriteDictionary` 和实例 `GetSprite(string)`，不安装全局解析补丁、不调用 `InitializeDictionary` 或替换字典。签名证据见 `verification/portrait-native-contract-0.1.47.txt`。

```csharp
// 先成功安装 atlas；在当前肖像字典就绪后的主线程事件中调用。
string spriteName = atlas.RegisterPortrait("field-kit");
// 将 spriteName 交给 ClientVisitApi / SupplierApi 的同名参数。
```

只需提供图集内的图片键。EZapi 自动生成 79 字符的稳定命名空间键，核对字典和原生 `GetSprite` 均返回本张 Sprite。同一图片重复注册幂等；未登记过的已有键即使图片相同也拒绝，其他 Mod 替换后的键不覆盖、不删除。字典未就绪会抛出带 `EZAPI_PORTRAIT_NOT_READY` 的 InvalidOperationException，可以等下次合适事件有限重试，图集本身不会因此永久失败。

若换场景重建了肖像字典，使用前重新调用 `RegisterPortrait`；不要把上次注册成功当成新场景已准备好。EZapi 不保留场景字典包装器。停止时只移除当前字典中仍匹配的自有映射，之后使旧图集句柄失效并释放图片；重启须重新声明图集。返回键可按稳定资源身份重建，但 Sprite 对象不可保存进周目数据。

0.1.47 肖像专项离线测试覆盖 31 项；原生字典/Unity 解码在该专项中是替身。另已在仅加载 EZapi 0.1.47 的新游戏进程完成图集与肖像原生检查 9/9，包含 SpriteDict.GetSprite 指针读回与重复登记。界面显示游戏版本为 050REV5-L，包装程序集哈希仍与来源索引一致；不沿用旧报告的版本称谓。535 个 ES3 文件测试前后哈希一致。肖像视觉显示、真实场景字典重建和保存重载尚未验证；证据见 verification/native-test-0.1.47/。

使用调试面板的“内嵌图集检查”可一次验证资源解码、隔离安装、`GetSprite`、`SetSprite` 原生读回、暂存、重复调用及 ID 冲突。0.1.47 同时检查肖像字典注册、原生 GetSprite 读回与重复注册。检查只创建并销毁两个未入库存的临时物品，肖像注册保留到停止，不请求保存。

## 原生验收

2026-10-21 已在 Probably Stolen 0.46D、MelonLoader 0.7.3、仅加载 EZapi 0.1.42 的进程中，用可丢弃槽 533 运行调试按钮。7 项原生检查全部通过：嵌入 PNG 解码并安装为一个隔离图集、返回有效 Unity Sprite、通过 `GameItem.SetSprite` 设置并读回图集/精灵键、重复安装幂等、安装回调只触发一次、预暂存赋值在图集安装后解析，以及重复 owner/atlas ID 在原生修改前被拒绝。临时物品销毁；测试前后 536 个本地存档相关文件均保持相同 SHA-256，诊断 `errors` 为空。

证据包位于 [`verification/native-test-0.1.42/`](verification/native-test-0.1.42/)，包含游戏内检查截图、MelonLoader 日志、EZapi 诊断和结构化结果。当前证据证明资源和原生绑定路径成功；尚未展示带该 Sprite 的物品图标，也没有执行保存重载，因此这些行为仍需后续验证。该历史版本未验证 0.1.47 新增的肖像注册。
