> 当前 API 指南。见[统一版本与验收状态](current-status.zh-CN.md)：当前 DLL 尚未完成实机验收；旧测试数量仅适用于其历史 DLL。

# EZapi图鉴中英文本与游戏语言切换

图鉴使用已有 `LocalizedItemText(Chinese, English)`，与原生物品名称的语言规则一致。旧字符串字段和位置构造参数保留；新增可选属性，不要求旧 Mod 修改代码。

## 最小步骤

1. 固定字段填默认可读文本；需要随语言显示的字段增加 `Localized...`。
2. 在初始化阶段调用 `RecipeGuideApi.RegisterRecipes`，或解析独立图鉴 JSON 后注册。
3. 游戏使用中文时显示中文，英语及其他语言显示英文。缺一侧时回退另一侧；未填双语属性的原固定字符串保持原文。
4. 切换游戏语言后，打开中的图鉴自动刷新文字和分页，不需要重新注册或关闭重开。

```csharp
var entry = new RecipeGuideEntry(
    "author.tools", "author.tools.press_recipe", "author.tools.press", "Press",
    "synthesis",
    new[] {
        new RecipeGuideFigure("author.tools.material", 2) {
            LocalizedName = new("材料", "Material"),
            LocalizedRequirement = new("必须属于玩家", "Must be owned"),
            LocalizedQuantity = new("2份", "2 units")
        }
    },
    new("author.tools.product", 1) { LocalizedName = new("成品", "Product") }) {
        LocalizedMachineName = new("压制机", "Press"),
        LocalizedNote = new("只显示说明。", "Instructions only.")
    };
var result = RecipeGuideApi.RegisterRecipes("author.tools", new[] { entry });
if (!result.Accepted) throw new InvalidOperationException(result.Reason);
```

完整 JSON 见 [recipe-guide-bilingual.json](examples/recipe-guide-bilingual.json)，可编译 C# 见 `MachineGuideExamples.RegisterBilingualGuide`。需要 `EZapi`、`System` 和游戏窗口代码使用的 `Il2Cpp` 命名空间。

## 字段索引

| 类型 | 双语属性 | 单个语言文本长度上限 |
| --- | --- | --- |
| `RecipeGuideEntry` | `LocalizedMachineName`、`LocalizedNote` | 200、4000 |
| `RecipeGuideFigure` | `LocalizedName`、`LocalizedRequirement`、`LocalizedQuantity` | 200、1000、200 |
| `RecipeGuideDisplay` | 同上三个字段，自动图鉴沿用 | 200、1000、200 |
| `RecipeGuideOptions` | `LocalizedNote`，可按机器或配方覆盖 | 4000 |
| `NativeItemOptions` | 已有的 `LocalizedName` 自动进入机器图鉴名称 | 沿用物品入口规则 |

这些属性在 JSON 使用 camelCase，如 `localizedName: { "chinese": "材料", "english": "Material" }`。属性省略表示沿用固定文本；对象至少一侧有值，已填写的一侧不能为空白、超限或含非换行/制表符控制字符。JSON 显式 null、未知字段和重复字段拒绝。错误码为 `EZAPI_LOCALIZED_TEXT` 或严格 JSON/图鉴校验错误。

自动图鉴由框架生成的“整堆、标签、容器槽、动态用量、动态产物、已有容器液体产物”等说明同时提供中英文本，不执行任何作者动态解析器。作者提供的固定字符串不会自动翻译，需要作者填写对应英文属性。

## 语言解析和显示

`RecipeGuideRules.ResolveLocale(entry, locale)` 是纯托管查询：返回新文本视图，不修改目录、稳定 ID、数量或原始双语字段。可用于离线工具和 AI 预览。locale 以 `zh` 开头时取中文（忽略大小写），其余语言取英文；空/未知 locale 也按英文路径回退。

游戏图鉴在主线程通过当前构建的官方 `LocHelper.GetCurrentLocaleCode()` 读取语言，复用已验证的 `NativeItemLocalization`；不读取 Windows 语言猜测游戏语言。未知游戏构建或 getter 不可用时，沿用物品接口的英文回退与单次诊断。

图鉴按钮、标题、材料/产出标题、缺图文案和分页同步切换。翻译后的当前条目只在选择、目录版本或语言改变时重新生成；图片缓存继续按稳定 ItemId 使用。关闭或读档清理文本视图与图片；不把翻译后的名称写进玩家存档。

## 验证与边界

离线检查覆盖所有字段、zh/en/其他语言、缺译回退、严格 JSON、文本限制、目录原文不变，以及自动图鉴的双语传播和框架生成说明。正式实机证据以对应验证报告为准。

调试面板“图鉴中英检查”只允许固定可丢弃测试档：使用 Unity 官方 `LocalizationSettings.SelectedLocale` 临时切英语、中文，检查实际图鉴渲染文本，最后恢复原语言。该入口不请求保存，不能把回归成功解释为整个 Mod 的所有文案已经翻译。

> 历史验收记录（0.1.61），不作为当前 DLL 通过依据。
> 历史阶段所用 DLL：图鉴离线131/0、注册104/0，49套离线通过，正式3工程零警告零错误；实机七组863/0、中英实际切换6/0、图片界面7/0。原语言zh已恢复，4张截图已核对；退出后536份ES3未变。见验证报告（历史标识 `verification/guide-localization-native-0.1.61.json`；原件未公开）。
> 