> 当前开发指南；版本与验收边界见 [release-status.json](release-status.json)。布局预览不代表机器加工、交易或保存功能验收。

# 原版 UI 积木与 AI 布局预览

用游戏实际的 `PixelWindow`、`TagElement`、`RichTextElement`、`ButtonElement` 和库存控件检查布局。JSON 修改后点一次重载，不需要每次重编 DLL。预览不绑定玩法回调，空库存锁定；不发放物品、不保存、不替换原版或其他 Mod 窗口。

## 先选一个布局

| 模板 | 用途 |
| --- | --- |
| [computer.json](examples/ui-preview/computer.json) | 两列部件槽、说明、居中的操作按钮 |
| [furnace.json](examples/ui-preview/furnace.json) | 电源、模组、输入、输出、说明横向分区 |
| [aquaculture.json](examples/ui-preview/aquaculture.json) | 高低不同控件横排并上下居中，下面独立居中按钮 |

这些只是布局示例，没有复制原版机器的玩法、贴图、配方或代码。官方 LearnerMod 的原生机器窗口示例用于核对控件用法；美术参考另见 [item-art.zh-CN.md](item-art.zh-CN.md)。

## 不改存档的预览流程

安装当前 DLL，退出游戏后启用普通开发面板；不需要 `regressionFixtures`。在 SDK 根目录运行：

```powershell
./developer-tools/Set-EZapiDeveloperTools.ps1 -GameDir '<游戏目录>'
./developer-tools/Set-EZapiUiPreview.ps1 -GameDir '<游戏目录>' -LayoutPath './docs/examples/ui-preview/computer.json'
```

启动游戏，进入任意可丢弃店铺档，打开 EZapi 调试，点击“UI 预览”。修改 JSON 后再次运行第二行并点击重载。普通文本、标签、按钮使用当前游戏语言对应的字体；不靠字数估计宽度。关闭预览原生窗口即可释放临时控件；读档、离店和停用框架也会清理。

点击“导出诊断”，读取游戏目录的 `UserData/EZapi/diagnostics.json` → `uiPreview.current`，保留同一次预览的截图。SDK 自动化工具可使用 [进入游戏脚本](automation.zh-CN.md)，普通重复点击由 `Invoke-EZapiNativeTest.ps1` 的计划执行。输入自动化会聚焦窗口，需在允许操作桌面时使用。

AI 按下列顺序迭代：

1. 用 `NativeUiLayout.Parse/Measure` 或游戏重载检查 JSON；结构错误会给出节点 ID。解析失败保留已打开的旧预览。
2. 看 `Controls` 的 `Expected` 与 `Actual`：位置与大小都以逻辑像素记录，原点在内容左上角。`Issues` 指出原生实际布局差异。
3. 看 `Inspection.Text` 的实际字体、字号、对齐、换行、所需宽高与 `Overflows`；放不下时扩大框、缩短文案或明确换行。不要无限缩小字体。
4. 看截图：标签是否清楚、槽位是否同列、按钮是否上下居中、空白是否合理。报告能发现几何问题，不能自动判断所有美术质量。
5. 切换游戏中英语言后重复；最后关闭窗口再导出诊断，检查 `visible=false`、`retainedControls=0`。

`uiPreview.last` 是最近一次测量，窗口关闭后仍可能保留；它不是当前打开窗口的证据。检查 `visible` 和 `current`。新建的每次预览都使用独立原生对象，不把玩家物品放进临时槽。

## JSON 的布局规则

每个节点必须有唯一 `id` 与 `kind`。支持 `Row`、`Column`、`Frame`、`Label`、`Tag`、`Button`、`Inventory`、`Slot`、`Spacer`。

| 选项 | 含义 |
| --- | --- |
| `horizontal: "Start\|Center\|End"` | 左对齐、左右居中、右对齐 |
| `vertical: "Start\|Center\|End"` | 顶部、**上下居中**、底部；默认 Center |
| `gap` | 相邻控件间距，0..64 逻辑像素，默认 4 |
| `padding` | 分组内部留白，0..64，四边相同 |
| `width` / `height` | 外框逻辑像素；分组 0 为自动尺寸；文字必须明确尺寸 |
| `reverse` | Row/Column 顺序反转；不旋转文字、不改变阅读方向 |
| `fontSize` | 可省略，沿用原生当前语言字号；明确设置范围 4..24 |
| `wrap` | 文字按实际字体换行；按钮建议短文案 |
| `columns` / `rows` | 空库存格数，1..32；一格 16×16 **逻辑像素** |
| `text` | `{ "chinese": "中文", "english": "English" }`；至少一侧可读，缺侧回退 |

屏幕中每个逻辑像素可能被游戏整数放大，不要拿屏幕截图尺寸直接填入 JSON。`Tag` 的原生最小高度按当前构建要求至少 16；中文字体需要的高度可能高于英文。字号范围是接口边界，不代表 4px 适合正常阅读。不要用 `<size>`、HTML、空格或换行模拟居中。

`Row` 先计算所有子控件的宽度之和与最大高度；`vertical=Center` 让高低不同控件居于同一垂直中心。`Column` 先计算高度之和与最大宽度，再按 `horizontal` 对齐。固定 `Frame` 可以让单个子元素在一个较大框里居中。分组内容超出固定尺寸时直接报错，不会偷偷压缩槽位、字形或点击区。

JSON 为最多 64 KiB、128 个节点、深度 12；拒绝未知/重复字段、未知枚举、数值枚举和非有限字号。没有 `onClick`、发物品、费用、保存或任意脚本字段。完整机器可读规则见 [native-ui-preview.index.json](native-ui-preview.index.json) 和 [schema](../schemas/native-ui.schema.json)。

## 实际 Mod 复用同一套积木

`NativeUiComponents.Row(gap, vertical, children)`、`Column(gap, horizontal, children)`、`Center(child,width,height)`、`Frame`、`Inset`、`Section`、`Label`、`Button`、`Spacer` 都是公开 DLL API。布局容器保留原生控件的图归属与尺寸；调用方不得将同一控件同时挂到两个父节点。

```csharp
var bays = NativeUiComponents.Row(6, UiAlign.Center,
    NativeUiComponents.Section("输入", input.Cast<PixelElement>()),
    NativeUiComponents.Section("输出", output.Cast<PixelElement>()));
var button = NativeUiComponents.Button("加工", 96, 20, callback);
var body = NativeUiComponents.Column(6, UiAlign.Center, bays,
    NativeUiComponents.Center(button.Cast<PixelElement>(), bays.widthPixels, 24));
var window = new PixelWindow(true, "我的机器");
window.Attach(body);
```

这段只展示布局。物品必须由调用 Mod 工厂持有窗口，槽准入、库存绑定、加工事务和保存仍由其实现；`MachineLayoutApi` 的既有保存布局不自动变更。完整可编译调用方见 [UiLayout.cs](examples/native-ui-dll/UiLayout.cs)。

现有 Mod 已有 `PixelWindow` 时，用 `NativeUiPreviewApi.InspectWindow(window)` 只读检查实际文字尺寸；不需要把它改成 JSON。该入口不调整布局、不克隆窗口、不执行按钮。报告最多读取 256 个当前字体文字节点，并且只对当前载入周目的有效窗口调用，不缓存原生对象跨读档。

## 给 AI 的指令

> 使用 EZapi 原版 UI 积木。先读 native-ui-preview.index.json、本文和真实 API 索引。先选接近用途的 JSON 模板，所有控件在原生逻辑像素里排列；高低不同控件横排使用 vertical=Center，按钮文字上下/左右居中，gap/padding 明确设置。进入游戏预览并导出 uiPreview.current，读实际字体所需宽高和 Issues，再结合截图调整。中英分别检查，保持稳定 ID，不用空格/字号标签凑位置。预览按钮没有业务回调、库存锁定；确认布局后再将同样积木接入自有机器工厂与库存绑定。不要把几何通过当作加工、兼容所有 Mod 或保存验收。

如果未放置 layout.json，点击预览会显示 DLL 内嵌的电脑模板。三个 SDK JSON 模板也嵌入同一 DLL，测试与开发资料使用相同定义。
