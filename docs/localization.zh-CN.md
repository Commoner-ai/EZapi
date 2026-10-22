> 0.1.72-preview.1 封装变更：开发工具默认关闭，见[安装与调试](install-and-debug.zh-CN.md)。下述415项及保存重载报告属于记录哈希4238A189…的封装前DLL；新DLL不继承其实机通过状态。

> 当前0.1.72：DLL作者可以使用 [GameLanguage](game-language.index.json) 按游戏设置解析这里的CSV目录；普通 JSON 物品也可以通过 `localized*` 字段绑定所属包的 CSV 键，且整包检查要求英语键完整。已登记物品会在显示/重开容器时刷新，当前DLL原生中英切换及五节点测试图读档界面已有[415项检查证据](verification/disk-supplier-native-0.1.72.json)，保留已有其它游戏语言CSV解析与缺译回退。

> 历史说明：本页下方记录0.1.8 CSV资源接口。当前语言读取见[游戏语言指南](game-language.zh-CN.md)；JSON物品字段绑定与其它跨API未完成项以当前版本指南和审计索引为准。

# EZapi 0.1.8：本地化资源与显式语言查询

这一版提供 `Localization` 资源 API，DLL 作者可以读取、查询和格式化翻译，JSON 包作者可以随包提供语言文件并运行整包检查。能力 ID 是 `localization.resources`。

当前目标游戏的 `ModHelper.GetLocalized/GetCurrentLocale` 不可用。EZapi 不伪造这两个方法，不往官方 `LoadedMod` 或字符串表写数据。普通 JSON 物品字段的 CSV 绑定从0.1.72开始提供；历史版本中的“固定文本不会自动切换”不适用于当前版。当前 API 和验证范围见[当前语言指南](game-language.zh-CN.md)。

## 文件结构与官方格式

依据官方 [中文指南·本地化](https://github.com/dragonpan2/probably-stolen-modding/blob/main/ModdingGuide.zh-CN.md#本地化)，内容包内直接放：

```text
author.example/
  pack.json
  items.json
  Localization/
    en.csv
    zh.csv
```

`pack.json` 仍采用协议 `ezapiVersion:"0.1"`；使用新能力时增加 `minEzapiVersion:"0.1.8"`、`requires:["localization.resources"]`。旧包省略 Localization 目录仍可用。items.json 仍必需，只做资源的包可填 `[]`。

官方精确代码：`en/fr/de/ru/zh/ja/es/ptBR/ko/it`。中文使用 `zh.csv`，不能写 `zh-CN.csv`；葡萄牙语保持 `ptBR.csv` 大小写。只扫描直接 `.csv`，不递归；未知代码或不准确的扩展名大小写拒绝。其它扩展名不参与扫描。

```csv
# en.csv：没有表头，每个键独占一行
food_name,Field Ration
food_desc,"Food, packed for travel."
merchant_line,"She said ""no refunds""."
days_left,Expires in {0} day(s)
help_lines,First line\nSecond line
```

```csv
# zh.csv
food_name,野外口粮
food_desc,便于携带的食品。
merchant_line,她说“概不退款”。
days_left,还可保存 {0} 天
help_lines,第一行\n第二行
```

- UTF-8，可带 BOM；接受 LF/CRLF。空行和首字符为 `#` 的行忽略。
- 按第一个逗号拆分键和值；后续未引用的逗号仍属于文本。**无表头**；不要导出 `key,value` 标题行。
- 值可用双引号包裹，内含双引号写成 `""`。值中的字面量 `\n` 转换为换行；真实跨行拒绝。
- 键允许 ASCII 字母、数字、下划线，区分大小写，1..160 字符；重复键拒绝。
- EZapi 严格校验非空白文本、UTF-8、配对引号、编号占位符。每文件最多 1 MiB、4096 条，文本解码后最多8192字符；目录最多10个语言文件。

## C# 调用

```csharp
// 已通过 ItemRegistry.RegisterPack 或自动内容包入口排队后可读。
var catalog = Localization.GetPack("author.example");
if (catalog is not null) {
    var result = catalog.Resolve("zh", "days_left", "保鲜信息不可用", 3);
    string label = result.Text;
    // result.ResolvedLocale/UsedFallback/MissingKey/FormatError 可用于诊断。
}

// 也可以显式读取独立目录。只读托管快照，不要求游戏或 Unity。
var standalone = Localization.LoadDirectory(Path.Combine(packFolder, "Localization"));
```

`GetPack` 只公开成功排队的包；未知/拒绝包返回 null。排队不等于原生目录 Applied。`LoadDirectory` 每次得到新快照，缺少可选目录返回空目录，文件不可读或无效时抛错。不要在 Update/Tooltip 每帧读取文件。`Parse(Stream,string)` 不关闭调用方的流。

查询顺序：指定语言 → `en` → 调用者提供的可读 fallback。未知查询语言也回退英语；缺少英语文件允许，但索引会提醒没有英语回退。fallback 必须非空白，最多8192字符，按字面量显示，不参与参数格式化。调用方不要把内部 ID 当作正常玩家文案。

支持 `string.Format` 编号参数和 `{{`/`}}` 字面花括号；下标0..31、对齐绝对值不超过256、格式片段最多64字符，标准数字格式精度最多256。命名占位符和 Unity Smart Strings 不支持。翻译与英语同名键的参数下标集合必须相同，次序和重复次数可不同。占位符语法错误在加载时拒绝；缺运行参数或不适合参数类型的格式字符串在 Resolve 返回 `FormatError` 和原文本，便于记录。格式化使用 `InvariantCulture`，并限制最终文本长度65536；自定义参数对象的异常不在本 API 的吞错范围。

此 API 不缓存翻译后的游戏物品或窗口引用。语言由调用者明确传入；自定义 UI 在语言改变后重新查询。自动刷新游戏物品字段需后续生命周期适配。

## CLI、索引与故障定位

```text
ezapi-validate.exe --localization <Localization目录>
ezapi-validate.exe --pack <包目录>
ezapi-validate.exe --packs <EZapiPacks根目录>
```

独立 `--localization` 要求目录已存在，空目录合法。输出含 Files/Entries/Resources/Warnings，资源包含语言、相对文件名、键列表、MissingEnglishKeys；格式见 `schemas/localization-index.schema.json`。缺译项/没有英语回退仅警告；重复键、非法格式和占位符不一致拒绝整个包。单包/目录模式沿用原先的依赖检查边界。

错误代码以 `EZAPI_LOCALIZATION_` 开头；文件行错误附带 `zh.csv:行号`。常见代码：ENCODING/ROW/KEY/TEXT/QUOTE/FORMAT/DUPLICATE/LOCALE/LIMIT/PLACEHOLDERS/PATH/FALLBACK；警告 COVERAGE。查询返回的 FormatError 不会自动写日志，由调用方决定记录。

调试面板新增“校验中英文”，读取已排队的示例资源，分别查询 zh/en 并填入实际文件数。随后“导出诊断”，查看 `localizationDebug` 和 `packs[].localization`。按钮不改游戏语言、库存或存档。实际按钮显示和游戏进程初始化仍需玩家验证。
