> 当前 API 指南。见[统一版本与验收状态](current-status.zh-CN.md)：当前 DLL 尚未完成实机验收；旧测试数量仅适用于其历史 DLL。

# EZapi内容包清单与依赖

## 准备物品图片

希望贴近原版画风时，使用[物品美术指南](item-art.zh-CN.md)中的分类参考图和AI提示词。开发包已附原始尺寸与4倍最近邻透明图；真实占格仍由items.json独立声明。

## 目录

```text
Mods/EZapiPacks/author.med/
  pack.json
  items.json
  Localization/en.csv
  Localization/zh.csv
  LootTables/*.json
  Textures/Items/*.png
```

旧清单仍可用：

```json
{"id":"author.med","name":"医疗包","ezapiVersion":"0.1"}
```

新包可声明：

```json
{
  "id":"author.med", "name":"医疗包", "ezapiVersion":"0.1",
  "version":"1.2.0", "minEzapiVersion":"0.1.6",
  "requires":["items.json","items.png","loot.tables"],
  "dependencies":[{"id":"author.base","minVersion":"1.0.0"}]
}
```

`version` 和 `minVersion` 都是三段非负版本。`requires` 只能填写 `docs/capabilities.json` 中已实现的能力；能力的 Preview 状态表示接口可用，仍需玩家验证。依赖必须位于同一个 `EZapiPacks` 根目录，按 ID 唯一匹配，不能自依赖、重复或循环。

## 校验阶段

```powershell
tools/ezapi-validate/ezapi-validate.exe --pack Mods/EZapiPacks/author.med
tools/ezapi-validate/ezapi-validate.exe --packs Mods/EZapiPacks
```

`--pack` 检查清单、物品所有权、PNG 签名/IHDR/尺寸、LootTables 文件和包内引用；它不检查目录外依赖。`--packs` 检查所有直接子包、依赖最低版本和循环，并返回稳定加载顺序。两者都不启动 Unity，也不证明原生目录、图片解码、存档或玩法成功。

运行时使用相同检查。依赖按拓扑顺序先排队；一个包自身校验失败时不会留下物品/掉落声明，其消费者也不会排队。运行时 `Queued` 仍需等待官方目录事件成为 `Applied`，掉落则需等待官方表事件成为 `Submitted`。

## AI 开发规则

1. 先选择唯一小写包 ID，物品 ID 使用 `包ID.物品`。
2. 先写完整 JSON，再运行 CLI；不要依靠运行时“部分成功”。
3. 跨包依赖写最低内容版本；不要把 DLL Mod 当作 JSON 依赖。
4. 掉落表引用自己的物品或表时先声明目标；跨包/原版 ID 仍由作者保证存在。
5. PNG 只放在 `Textures/Items/` 直接子目录，文件名与 `sprite` 匹配。
6. 发布报告分开写 Valid、Queued、Applied、Submitted 和游戏内验证。

## 0.1.8 可选 Localization 目录

`Localization/<locale>.csv` 与物品/PNG/掉落一起预检，任何 CSV 错误在提交前拒绝整包。普通物品 `localized*` 字段可用 `{"key":"name_key"}` 绑定本包文本；每个被引用的键必须有 `en.csv` 英文条目，否则整包拒绝。其它字段未引用的缺译项仍返回警告。包只有在物品批次接受后才向 `Localization.GetPack` 发布只读目录；返回资源不等于 Applied。CLI `--localization <Localization目录>` 独立输出索引，`--pack/--packs` 在结果的 Localization 属性返回同一索引。官方无表头格式、语言代码、范围和查询示例见 localization.zh-CN.md；输出 Schema 为 schemas/localization-index.schema.json。


## 0.1.9 储物声明

含 container 的内容包应设置 minEzapiVersion 为 0.1.9，并在 requires 中加入 items.container。内部网格和过滤字段见 containers.zh-CN.md；声明错误在整包预检阶段拒绝，不先排队部分物品。allowedIds 中跨包物品存在性仍由内容作者负责，用 dependencies 保证相关包按序加载。
