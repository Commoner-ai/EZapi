> 当前 API 指南。见[统一版本与验收状态](current-status.zh-CN.md)：当前 DLL 尚未完成实机验收；旧测试数量仅适用于其历史 DLL。

# 配方图鉴的物品图片（EZapi）

图鉴沿用 `RecipeGuideFigure.ItemId`，不需要图片路径或新增 JSON 字段。物品先由作者注册；图鉴通过当前目录创建一个临时游离实例，再用官方 `GameItemElement.ResolveItemSprite()` 读取实际 Sprite。原版、EZapi JSON 和通过该解析路径提供图片的 Mod 都能参与。

```csharp
RecipeGuideApi.RegisterRecipes("author.tools", new[] {
    new RecipeGuideEntry(
        "author.tools", "author.tools.assembly", "author.tools.assembler",
        "装配台", "synthesis",
        new[] { new RecipeGuideFigure("author.tools.scrap", 2, "废料") },
        new RecipeGuideFigure("author.tools.part", 1, "零件"), "仅提供图鉴说明。") {
            LocalizedMachineName = new("装配台", "Assembler"),
            LocalizedNote = new("仅提供图鉴说明。", "Display only.")
        }
});
```

名称、要求和数量使用可选 `LocalizedName`、`LocalizedRequirement`、`LocalizedQuantity`，中英语义见[双语指南](recipe-guide-localization.zh-CN.md)。自动机器图鉴通过 `RecipeGuideDisplay.IllustrationItemId` 指定展示物品；它与真正消耗/产出的 ID 分开。图片声明不会执行 Condition、动态数量、动态产物或加工回调。

## 图片与几何

0.1.66 使用公开 `Sprite.vertices`、`uv`、`triangles`、`rect`、`pivot` 和 `pixelsPerUnit`，将真实三角形与 UV 映射到透明画布。因此支持矩形、紧密不规则轮廓、四分之一旋转及任意有效 UV 映射，并保留原图透明边距与宽高比。不依赖 `textureRect` 或打包旋转枚举，未知打包方式只要提供有效公开网格也可显示。

每页最多四项投入和一项产出；完整名称、ID、要求和数量仍在下方分页文字区。未知 ID、无效/超限几何或图片失败显示“暂无图片”及原文字，原因写入 `diagnostics.json.recipeGuide.images.entries`。不改变背包占格或真实物品。

## 生命周期和上限

- Update 每帧最多预热一个未缓存 ID；OnGUI 只绘制缓存。工厂返回默认游离实例，不应发奖励、保存或修改玩家库存；框架无法撤销第三方工厂自行产生的副作用。
- 最多缓存128个 ID，含失败记录；按最近使用淘汰。关闭、重开、目录修订、场景或周目变化清理自有资源。
- 缩略图最长边128像素，GPU仅回读UV包围区且最长边512像素；顶点上限4096、索引24576、栅格候选200万。退化三角形跳过；全部退化、非有限值、越界坐标/索引和空几何拒绝。
- 源纹理即使不可读也可显示。Graphics.Blit 后按网格组装自有缩略图，三角形外透明；所有路径恢复 RenderTexture.active 并释放临时目标/读回纹理。源Sprite/纹理不修改、不销毁。
- 图片代表临时默认实例，不代表玩家持有的具体状态、材质着色或动画帧；源内容变化后需关闭重开刷新。

## 验证与排查

调试面板的“内嵌图集检查”包含真实 GPU 裁切、方向、透明度、四种旋转×两种轮廓的逐像素比较、实际原生 Sprite 三角形、四个真实物品ID、缓存寿命及12次连续工厂创建。“配方图鉴”可检查图片、材料分页、缺图、关闭/重开和Escape。

初次预览观察Unity公开初始化/语言/字符串预加载句柄；随后使用SelectedLocaleChanged完成事件门控，切换中不重新获取InitializationOperation。050REV5-L原生日历Update会同步取语言表，切换中精确延后此文字刷新，完成后恢复；本版实测仅2帧。等待不执行工厂、不缓存失败，也不调用WaitForCompletion。语言变化释放旧缩略图；工厂内部再次切语言的旧结果不进入缓存。诊断含localeState/localeError。

> 历史验收记录（0.1.66），不作为当前 DLL 通过依据。
> 本版49套离线检查通过；图片43项、目录/JSON131项、自动图鉴与注册181项。最终DLL同一进程九组原生954/0（资产60/0），图鉴界面7/0，中英实际切换8/0（含两种语言的液体容器预览），复杂机器旧状态19/0；全部536份ES3未变。见阶段证据（历史标识 `verification/guide-mesh-native-0.1.66.json`；原件未公开）、六类型/22属性契约审计（历史标识 `recipe-guide-public-contract.audit.json`；原件未公开）和[AI索引](recipe-guide-images.index.json)。

测试夹具曾在不可读纹理上修改几何、并把局部单位误用于OverrideGeometry输入；两次拒绝均保留证据。按Unity官方文档修正为先建立网格、使用rect内像素坐标，再丢弃CPU副本；读取vertices才使用局部坐标。生产图片代码不调用OverrideGeometry。
