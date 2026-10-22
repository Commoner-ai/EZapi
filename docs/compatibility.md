# 兼容性边界（0.1.15）

目标为 Probably Stolen Demo 049-REV5-L + MelonLoader IL2CPP .NET 6；独立前置，无 Forge 依赖。官方参考和当前接口差异见 official-api-map.zh-CN.md。

注册和 tooltip 走官方 ModHook。工厂默认状态独立，堆叠限制绑定实例的 mayStackItemFunc。JSON 协议仍为 0.1，原四个示例 ID 不变并新增两件激活示例；旧 description 继续兼容。字段语义、值域及单件限制保留。

PNG 兼容桥仅注册 EZapi.Content/<packId> 自有图集，核对冲突、不覆盖别人的图集；释放时检查实例身份。使用公开 atlasCache 是本机版本适配，不是官方文档承诺的资源注册 API。图片解码一次、资源进程内持有；游戏原生代码负责实际精灵解析、旋转和拖放。JSON 的形状明确声明，尚未从 PNG 透明度自动推导。

Harmony 用于自定义分类名、调试输入、库存守卫与精确生命周期；所有目标显式列在 patches.index.json。目录、图片、堆叠、tooltip 都无 Harmony 补丁。普通/详细 tooltip 共用原生地址的历史问题见 HOTFIX-0.1.1.zh-CN.md，不再安装这两个补丁。

来源行在官方 Late 阶段只追加一次；不保证原生短简介之前/之后或绝对末行。原生分类由游戏翻译，自定义分类和物品文本仍是固定文案；CSV 资源/显式语言查询见 localization.zh-CN.md，自动物品翻译尚未实现。掉落表使用当前构建公开的 LootRegistry 适配桥；ModHelper 的掉落便捷 API 和自定义 UI 仍未实现；周目数据使用下述兼容桥；激活行为仅开放文档中的 healWounds。

0.1.3 新增 JSON activation 与 C# 激活回调，使用实例委托，不增加补丁。原四个示例定义不变，新增两件治疗敷料；旧包省略新字段即可继续使用，新字段最低需要 0.1.3。行为异常时按 ID 停用至重启，已产生的副作用不会自动回滚。上下文只能同步使用，不能跨场景缓存；详见 item-activation.zh-CN.md。

保留测试环境只启用 EZapi。当前 0.1.14 未执行游戏内验证。跨天、保存重载和多 Mod 组合仍需实际日志，不能从编译通过推断。框架卸载前应先移除存档内新增物品，避免失去对应目录工厂。

0.1.4 GameEvents 转发18个官方阶段，不新增补丁。官方 ModHelper 存取便捷方法在本机缺失，因此 RunData 单点适配公开 PlayerStore.modData；键格式为 EZapi.RunData/v1/<modId>/<key>，不声称与官方便捷方法内部前缀兼容。句柄绑定一次载入，失效后拒绝写入。不主动保存，落盘结果待验证。


0.1.5 掉落表声明使用 JSON `LootTables/*.json` 或 `LootTables.Register`。新列表要求 owner 前缀；原生表/组只通过官方方法修改。提交等待物品目录与 `OnLootTablesLoaded`，同一 owner 每进程一次；场景/读档不重复追加。快照不持有原生对象，试抽只返回 ID 并推进 RNG，不发放物品。实际原生 journal 回放、拾荒/供应商路径和保存结果仍需游戏日志。


0.1.6 的 pack 清单新增可选版本、能力和依赖；旧清单保持有效。只扫描直接 JSON 内容包，不扫描 DLL，不伪造官方 LoadedMod。PNG 预检共享纯托管模块；签名/IHDR/尺寸通过不等于 Unity 解码通过。整包 Valid、框架 Queued、原生 Applied/Submitted 和游戏内行为是独立阶段。


0.1.7 新增食品/液体声明，旧包省略就不调用新 helper，原六件示例定义不变。新增两件示例的原生行为尚待验证。食品、液体、自定义激活和通用次数不能混绑；保留原生默认标记，显式配置才覆盖。不创建自己的健康/液体系统，不干预库存和窗口。

0.1.8 增加纯托管本地化资源 API，旧包无 Localization 时行为不变。存在该目录的包开始严格检查官方代码和 CSV：坏文件拒绝整包，缺译仅索引警告。GetPack 返回已排队包的只读快照，ItemRegistry.Stop 清除进程资源索引；跨场景不重读文件。手持旧快照可继续只读查询，它不持有原生状态。未添加补丁或自动保存；自动游戏语言/物品翻译尚未实现。


0.1.9 只对显式 container 声明创建原生储物图，原八件示例保持不变。过滤参数按实例复制，窗口和内部库存不共享；保持四个既有显式补丁。不能与其他使用行为混绑。没有覆盖原生交易/拖放/存档方法，未实现已有容器布局迁移；不要随意修改有内容的存档容器的 ID、内部尺寸或 mask。实际过滤、所有权、嵌套、关闭窗口和保存重载待玩家测试。


0.1.10原生服务不新增补丁，不引用Forge。通过GameSession约束当前周目主线程，回调后不持有原生引用。补偿事务不保证游戏文件原子保存；电源只支持拥有且已入库存的单件，液体目前只读。原十件JSON示例和四个显式补丁保留，运行与持久化待玩家验证。


0.1.11库存API按需调用原生槽位，无新全局补丁。不更改原生父图/单位数，不先Expel，不在异常后尝试再接纳。快照失效、数量不守恒、返回值不一致或写后异常均停止并明确报告。布局预算限制同步搜索，不能作跨帧预留；普通API不等同列表库存创建/冻结/准入注册，后者仍待开发。


## 0.1.12库存守卫

新增InventoryAdmissionApi、InventoryFreezeApi、ListInventoryApi及HookApi。先读inventory-guards.zh-CN.md和patches.index.json；三组钩子等官方目录后安装，缺目标关闭对应组。编译能力不代表原生Ready；导出诊断查看inventoryGuards。调试按钮“冻结主库存首件5秒”可手测冻结，点“解除测试冻结”立即释放。原生detour/准入/扩容保存仍待玩家验收。


## 0.1.13 原生物品图

新增 `SaveGraphApi.Items/Encode/Decode`，详见 `save-graph.zh-CN.md`。调试按钮“导出首件物品图”只做原生编码，输出到 UserData/EZapi/save-graph；不会恢复、生成或自动保存。先检查 `SaveGraphApi.CanDecode`；当前游戏参考程序集缺少必需恢复入口，解码与持久库存端点均在原生写入前关闭。端点 API 和限制见 `inventory-endpoints.zh-CN.md`。

0.1.14新增7入口的独立生命周期钩子组ezapi.lifecycle与内容通知；全部目标见patches.index.json。未执行真实IL2CPP/保存重载测试，详见lifecycle-content.zh-CN.md。


0.1.15 当前本机程序集SHA256已变化，缺MachineHydroponic.LoadHydroponicModuleInvData(GameItem):void，SaveGraph.Decode执行前拒绝，等待当前恢复链适配。生产/效果注册见production-effects.zh-CN.md；离线替身不证明真实随机或保存。
