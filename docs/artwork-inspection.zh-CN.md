# 图片内容检测：黑图、透明图与原生精灵

使用 `SpriteArtworkApi.InspectItem(item)` 检查当前物品的原生图片；已有 `Sprite` 时使用 `Inspect(sprite)`。接口返回只含统计值的托管结果，不保留或导出原版图片。当前 DLL 的实机验收以 [release-status.json](release-status.json) 为准。

```csharp
var result = SpriteArtworkApi.InspectItem(item);
if (result.Status != SpriteArtworkStatus.Inspected)
{
    // 未获得可靠图片证据：记录 Reason，暂停这个条目的选择，不把失败当成黑图。
    Log(result.Reason);
    return;
}
var content = result.Statistics!.Content;
// Artwork / Dark / Transparent。是否允许成为货源由自己的 Mod 决定。
```

| 使用场景 | 入口 | 依赖 |
| --- | --- | --- |
| 当前物品原图 | `SpriteArtworkApi.InspectItem(item, options)` | 已载入周目、游戏主线程、已知游戏构建 |
| 已有精灵/自定义图标 | `SpriteArtworkApi.Inspect(sprite, options)` | 已初始化 Unity 主线程；不要求物品或周目 |
| 自己的 RGBA 图片/离线工具 | `ArtworkPixels.AnalyzeRgba(width,height,rgba,options)` | 纯托管；可在后台线程使用 |

原生调用示例：[StockArtwork.cs](examples/sprite-inspection-dll/StockArtwork.cs)、[工程](examples/sprite-inspection-dll/Inspection.csproj)。纯 RGBA 示例：[Artwork.cs](examples/artwork-dll/Artwork.cs)、[工程](examples/artwork-dll/Artwork.csproj)。后者只需开发包自带的 EZapi.dll，不需游戏程序集。

## 判断含义与参数

默认 `MinimumAlpha=16`：低于此值的像素不计入可见内容，其 RGB 即使是白色也忽略。可见像素中，至少一个 RGB 通道大于 `MaximumDarkChannel=8` 才算细节像素。细节数量须达到 `max(MinimumDetailPixels=2, ceil(可见像素数 × MinimumDetailFraction=0.01))`，才分类为 Artwork；因此孤立的一个亮点不会把黑色占位图变成有效图片。没有可见像素则为 Transparent，其余为 Dark。

这是**可配置的内容检测策略**，不判断艺术质量、画风或可售资格。正常的纯黑剪影也可能分类为 Dark；作者可以调整阈值，1×1 有效图可以显式设 `MinimumDetailPixels=1`。它不能识别所有彩色占位符，也不保证原版的 UNKNOWN 图标会分类为 Dark。

`ArtworkStatistics` 返回 SampledPixels、VisiblePixels、DetailPixels、RequiredDetailPixels 和 Content。SampledPixels 只计算精灵 UV 三角形覆盖的像素中心；分母不是整张图集，也不是框外留白。结果不含任何 Unity 包装对象。

## 裁切、GPU 与兼容边界

- 非紧密打包精灵使用实际 `textureRect`，同时核对 UV 与三角形；紧密打包精灵不强取可能抛异常的 textureRect，而由 UV 求范围并建立三角形遮罩。图集中邻居的颜色、矩形内但精灵网格外的内容不会参与检测；UV 旋转或反转不改变这个来源范围。非整数矩形或异常网格会明确拒绝，不能用整张图集充当兜底。
- 先尝试可读纹理的当前裁切；不可读或 GetPixels 不支持的压缩纹理可经 GPU 按原裁切尺寸读回。`AllowGpuReadback=false` 或 GPU 不可用时返回 Unavailable，绝不假定图片为空。
- 支持原生 `associatedAlphaSplitTexture`，用对应范围的红通道还原透明度；尺寸不符则拒绝。RGB 检测按当前项目的显示色彩空间处理，拆分透明度按数据处理；GPU 不依赖调用者遗留的 sRGBWrite 设置。真实后端的色彩转换/压缩误差、UI 自定义 shader、tint、材质、灯光和屏幕最终效果仍需实机核对；本接口检查源精灵内容。
- GPU 流程在 finally 中分别恢复 `RenderTexture.active` 和 `GL.sRGBWrite`、Destroy 自建读回纹理、ReleaseTemporary 自借渲染目标。一个清理异常不阻止其余清理；任何清理失败返回 CleanupFailed，不发布成功统计。不会 Destroy 原图或修改原图过滤方式、wrapMode、可读标记、像素与原物品。
- 每张图默认/硬上限均为 1,048,576 像素，作者可降低 MaximumPixels；最多 1,024 个 UV 顶点、6,144 个三角形索引，三角形包围框累计栅格工作最多 8,388,608 次。超限拒绝整次结果，不能返回前 N 像素的假完整统计。
- 调用是同步的；单次 GPU ReadPixels 可能造成等待，框架不能抢占已经进入的原生调用。批量选货应每帧处理少量图片，不在 OnGUI、每张卡片绘制或每帧 tooltip 中重复检查。没有隐式后台扫描或无限缓存。作者可缓存纯统计，但图片、策略、周目或相关资源变化后须重新检查；不能跨场景缓存已失效的 Sprite/GameItem。
- `InspectItem` 使用游戏 `RenderHandler.LoadFromAtlasRaw` 与物品当前图集/精灵键，不创建目录工厂，不主动选择通用缺图兜底。读取中换周目或替换物品/图片键会使结果失效。空图集、缺图与运行时异常都有明确结果，不接管其他 Mod 的资源加载器。

纯托管 `AnalyzeRgba` 要求精确 `width*height*4` 字节、RGBA 顺序；它不修改/复制输入，调用过程中不要并发修改数组。非法输入抛参数异常，超预算抛带 `EZAPI_ARTWORK_LIMIT` 的异常；与原生接口返回状态的方式不同。

## 结果、语言与验证

只有 `Inspected` 有可靠 Statistics。Invalid 表示参数/几何无效；Unavailable 表示线程、资源或读取未就绪；LimitExceeded 表示预算拒绝；CleanupFailed 表示原生临时资源或渲染状态清理失败。后三者均不能当成 Transparent/Dark。

状态、数值与英文错误码不随语言改变；显示给玩家的文字应通过 GameLanguage.Resolve 提供中英文本。没有新增固定中文 UI、内容 JSON 字段或自动禁售规则。见 [机器索引](artwork-inspection.index.json)、[错误索引](errors.index.json)。

离线测试链接实际算法和原生适配器源码，用明确的 Unity 替身覆盖矩形/紧密网格、邻图、透明 RGB、色彩分支、拆分 alpha、GPU 各阶段失败及独立清理。替身模拟不证明真实 GPU、材质、后端色彩空间或所有 Mod 图片都正确；当前 DLL 实机仍待验收。
