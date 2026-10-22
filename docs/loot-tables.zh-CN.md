> 当前 API 指南。见[统一版本与验收状态](current-status.zh-CN.md)：当前 DLL 尚未完成实机验收；旧测试数量仅适用于其历史 DLL。

# EZapi掉落表与获得路径

在物品注册之外，用掉落表把物品接到游戏实际使用的拾荒、供应商货物等来源。JSON 格式沿用官方 `tables / groups / patches`，运行时通过当前构建公开的 LootRegistry 处理。ModHelper 中的掉落便捷方法在本机缺失，所以不声称调用了那些方法。原生分配、最终随机结果和真实获得路径仍待游戏内验证。

## JSON 内容包

```text
Mods/EZapiPacks/author.med/
  pack.json
  items.json
  LootTables/
    01-medical.json
```

pack.json 继续使用 `id/name/ezapiVersion`，其中协议为 0.1。此功能最低需要 EZapi 0.1.6。items.json 仍必需；只修改原版表的内容包可填写空数组 `[]`。不写掉落声明的旧包保持原行为。

```json
{
  "tables": [
    {
      "id": "author.med.supplies",
      "entries": [
        {"id": "author.med.dressing", "weight": 3},
        {"id": "author.med.reusable_dressing", "weight": 1}
      ]
    }
  ],
  "groups": [
    {
      "id": "author.med.group",
      "entries": [{"id": "author.med.supplies", "weight": 1}]
    }
  ],
  "patches": [
    {"group": "dumpingGroundTG", "add": [{"id": "author.med.supplies", "weight": 10}]}
  ]
}
```

示例里的两件物品必须先在同包 items.json 中定义。新表和组的 ID 要以包 ID 加点号开头，以减少 Mod 间冲突；这项命名规则是 EZapi 的约束，比官方通用示例严格。条目 ID 是物品 ID，表组条目 ID 是掉落表 ID，不是另一个表组。新表本身不会自动进入拾荒，必须通过 patch 接到实际使用的表组。

安装自带的示例只创建 `ezapi.sample.medical_demo` 和 `ezapi.sample.demo_group`，没有修改任何原版表或表组。它用于检查与手动试抽，不自动发放。上面的 dumpingGroundTG patch 是开发示例，未装进默认测试包。

## 修改已有列表

```json
{
  "patches": [
    {
      "table": "junkTable",
      "add": [{"id": "author.med.dressing", "weight": 10}],
      "remove": ["rusty_can"],
      "setWeight": [{"id": "cup_noodle", "weight": 0}]
    }
  ]
}
```

一个 patch 只能填 table/group 其中一个。`clear:true` 会清空目标列表，然后按 add、remove、setWeight 的顺序执行；未声明 clear 的改动保留其它条目。clear 会影响共用这张表的所有调用方，普通扩展优先定点 add/setWeight。

每文件先 tables、再 groups、再 patches；多个 JSON 文件按序号/字母的 Ordinal 顺序处理，最多 16 文件。新表必须出现在引用它的组/补丁之前。多 Mod 编辑同一目标时，最终结果依赖加载顺序，后续 Mod 可能再次改变权重。

权重是相对值，不是百分比。当前表中两个条目的权重 3 与 1 表示 75% 与 25%；向原版表增加 10 不能笼统说成 1%。new/add 要求有限且 >0，setWeight 可为 0，最大 1e9。0 保留条目但不参与正常抽取；remove 移除条目。JSON 中每个 weight 都必须显式填写，避免遗漏 setWeight 意外成为 0。

## C# 声明

```csharp
var result = LootTables.Register("author.med", new LootDefinition {
    Tables = new[] {
        new LootListDefinition {
            Id = "author.med.supplies",
            Entries = new[] {new LootWeightDefinition {Id="author.med.dressing", Weight=1}}
        }
    }
});
```

先通过 ItemRegistry 提交自己的物品，再在同一初始化主线程登记掉落。一个 owner 一次 Register，整份定义中可包含多张表/组/补丁；同 owner 的 JSON 包与 C# 二选一，避免重复声明。定义预检后复制为独立操作快照，调用方后来清空自己的 List 不会改变已排队内容。参数无效、重复 owner/ID、晚注册或线程错误抛带错误码的异常。

`Accepted/Queued` 只表示声明接收。运行时等待物品目录与 OnLootTablesLoaded 两个条件就绪，预检目标/组引用/原生 ID 冲突，再按顺序提交 journal。owner 下的物品必须为 Applied；已知的其它 EZapi 物品也要求 Applied。非 EZapi 的外部物品 ID 未由此 API 验证，请自行保证依赖已安装；游戏原生掉落接口不会检查物品 ID 是否能正确生成。

原生 journal 由游戏负责在场景重建时重放；EZapi 不在每次读档/切场景重新追加声明。诊断 `loot.batches` 的 Submitted 只表示原生方法返回、操作已提交，不表示最终玩法和其它 Mod 组合已验证。目标不存在或引用错误时 Rejected；原生调用已尝试后抛错时 FailedPartial。接口没有事务回滚，框架不会删全局 journal 或自动重试部分成功的声明；修复后重启进程。

## 查看与抽取

```csharp
var table = LootTables.GetSnapshot("author.med.supplies");
var group = LootTables.GetSnapshot("author.med.group", isGroup: true);
if (LootTables.TryRoll("author.med.supplies", isGroup: false, out var itemId)) {
    // 只得到 ID，不是 GameItem，也没有放入库存。
}
```

场景卸载时关闭读取门控，等待新场景的官方 OnLootTablesLoaded 后，于游戏主线程调用。不存在的表快照 Found=false；不存在或没有正权重时 TryRoll=false。快照是纯托管副本，包含权重、当前表内概率、添加来源及修改来源。表组的概率表示选中该表的概率；最终物品概率还要乘以所选表内部概率，不能直接把它当作物品总概率。

TryRoll 使用游戏 RNG，并触发官方掉落抽取事件，所以会推进随机数状态，其他 Mod 也可能改写返回 ID；它不生成、发放或保存物品。观察当前分布优先 GetSnapshot，不要每帧试抽。生成与库存落点属于另一条需要核对所有权/容量/原生接纳条件的路径。

## CLI 与限制

```powershell
tools/ezapi-validate/ezapi-validate.exe --loot LootTables/01-medical.json
```

CLI 和运行时共用 JsonLoot。字段 Schema 在 `schemas/loot.schema.json`。校验覆盖类型、重复/未知/null 字段、权重和数量；不运行 Unity，不证明外部 ID 存在，也不检查当前游戏最终表。每文件最多 1 MiB、64 张表、64 个组、128 个 patch；每个条目列表最多 512 项，每 owner 最多 4096 个原生操作。新增表内部每条 entries 由一次 Register 提交，不计成单独 Add。

内容包会先解析全部文件，检查新 ID 冲突并准备物品资源，成功后一起入队。后一个文件损坏或 PNG 缺失时不会留下先前文件的掉落提交。初始化声明的一起入队不等于原生提交事务成功。

## 玩家测试

重启确认 0.1.6，进入测试档后点“查看示例掉落”，应看到两件敷料的权重 3/1（75%/25%）。点“试抽 ID（不发放）”应返回两件敷料之一；少量试抽不用于验证概率统计。按钮明确提示它会推进游戏随机数。

导出诊断查看 loot.batches 和 lootDebug；原生错误查 EZAPI_LOOT_APPLY_FAILED 或 [LootRegistry]。外出返回后重新查看，检查表仍在且无重复 journal 提交日志。要验证实际拾荒获得，再由内容 Mod 明确添加到实际使用的表组，并走对应路径；当前默认示例没有开启这一步。物品、存档、跨天和其他 Mod 组合依然需要实际运行日志。
