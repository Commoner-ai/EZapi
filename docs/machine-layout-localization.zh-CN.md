> 当前 API 指南。见[统一版本与验收状态](current-status.zh-CN.md)：当前 DLL 尚未完成实机验收；旧测试数量仅适用于其历史 DLL。

# 机器窗口与槽位中英文（当前接口）

机器布局可以直接声明中英文标题和槽位标签。创建时按游戏当前语言显示；显示物品或重新打开窗口时通过 `ezapi.items.localization` 钩子刷新。自有常开界面在语言变更后可以调用 `MachineLayoutApi.RefreshLocalization(machine)`。完整字段与边界见 [AI 索引](machine-layout-localization.index.json)。

```csharp
// 在新物品目录工厂内调用。每次创建独立窗口和槽位。
MachineLayoutApi.Create(freshMachine, new MachineLayoutDefinition {
    LocalizedTitle = new("装配台", "Assembler"),
    Output = new() {
        Kind = "container", Size = new(3, 3),
        LocalizedLabel = new("产物容器", "Output container")
    },
    Battery = new(new(2, 2)), ManualSlot = true,
    LiquidInputs = new[] {
        new MachineContainerSlot {
            SlotId = "water", LocalizedLabel = new("净水容器", "Water vessel")
        }
    }
});
```

| C# 字段 | 独立 JSON 字段 | 省略时的行为 |
| --- | --- | --- |
| `MachineLayoutDefinition.LocalizedTitle` | `localizedTitle` | 使用固定 `Title`，再回退当前物品名称 |
| `MachineContainerSlot.LocalizedLabel` | `liquidInputs[].localizedLabel` | 使用原样 `Label`，可以为空 |
| `MachineOutputTemplate.LocalizedLabel` | `output.localizedLabel` | 容器产物槽显示“产物容器”或“Output container”；物品网格不用此标签 |
| `MachineLayoutDefinition.LocalizedBatteryLabel` | `localizedBatteryLabel` | “电池”或“Battery” |
| `MachineLayoutDefinition.LocalizedManualLabel` | `localizedManualLabel` | “说明书”或“Manual” |

每个对象至少提供 `chinese`、`english` 中的一种；已提供的文字须非空白、不含控制字符、最多 200 字符。`zh*` 选中文，其它语言选英文；缺失一侧回退另一侧。为了同时兼容游戏中文和英语，内容作者应提供两侧。旧固定文字保留原样，框架不会猜测翻译。

独立布局 JSON 没有所属内容包，因而不接受 CSV `key`。需要 CSV 的 DLL 作者在声明前用 `LocalizationCatalog` 解析后填固定字段，或读出两侧再构造 `LocalizedItemText`。普通物品 JSON 的包内 CSV 绑定是另一条现有路径，见 [物品文本索引](item-localization.index.json)。

完整 JSON 例子为 [machine-layout-bilingual.json](examples/machine-layout-bilingual.json)，编辑器提示见 [Schema](../schemas/machine-layout.schema.json)。先显式解析文件，再在目录工厂内调用 Create；这些布局文件不会自动被 EZapiPacks 导入。

```powershell
dotnet tools/ezapi-validate/ezapi-validate.dll --machine-layout docs/examples/machine-layout-bilingual.json
```

`RefreshLocalization` 只在初始化后的游戏主线程调用。普通物品或框架停用时返回 `false`；布局标记损坏、缺槽、尺寸或父图不一致会在任何文字更新前抛错。自动显示回调会捕获并记录错误，不让它越过游戏回调。原生 setter 发生异常时可能已有部分文字更新；它不影响布局内容，后续正常刷新会重设可见文字。

刷新只调用可见标题和槽位 Tooltip 文字设置，不替换窗口或库存，不修改过滤委托、电源要求、物品身份，不写布局标记或请求保存。`TryGetInventory` 保持只读，不隐式刷新。没有新增双语字段的旧布局仍序列化成原来的 JSON；给已有已保存布局增加字段仍属于布局声明变更，现有修订检查继续要求明确迁移。

> 历史验收记录（0.1.72），不作为当前 DLL 通过依据。
> 48 项离线检查覆盖实际解析、完整父图校验、旧标记兼容、语言刷新、自动显示路由和错误隔离，原生对象使用带监测的替代对象。标题与 Tooltip、中英切换、重新打开及五节点测试图保存重载已有历史 0.1.72 DLL 记录（历史标识 `verification/disk-supplier-native-0.1.72.json`；原件未公开）。标题文本及布局属性已检查，容器标题未另做像素视觉验收；0.1.72仍是开发候选。
> 