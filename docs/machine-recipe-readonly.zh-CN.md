> 当前 API 指南。见[统一版本与验收状态](current-status.zh-CN.md)：当前 DLL 尚未完成实机验收；旧测试数量仅适用于其历史 DLL。

# 配方只读回调与产物配置（EZapi）

配方条件、数量/ID/费用/液体解析器、事务工厂及事务的 `Validate` 都应只读。`PrepareItem` 可以配置当前新产物，不能修改旧材料、机器、电池、液体容器或已有产物。真正的扣料、扣电和交付发生在事务 `Apply` 中；确认提交后的通知按原有契约执行。

```csharp
var recipe = new MachineRecipe(
    "author.tools.convert", new[] { new MachineIngredient("scrap_metal", 2) },
    new MachineItemOutput("author.tools.product") {
        // 只配置传入的新产物，不能修改plan.Context.Items里的材料。
        PrepareItem = (plan, product) => product.SetValue(123)
    }) {
    // IsTag读取已有状态；不要在条件里用GetTag创建或写标签。
    MachineCondition = machine => !machine.IsTag("AUTHOR_DISABLED")
};
```

0.1.69在选择和新产物配置之前，捕获整台机器的只读图；普通没有内装物的材料也完整检查标签。观察内容包括物品身份、数量、价值、所有权、形状、父库存、状态/字典身份、标签、窗口、图连接和库存锁。数量与液体不变而标签发生变化，同样会拒绝。

选择阶段变更会返回 `EZAPI_RECIPE_STALE`；加工前自动化回调变更返回 `EZAPI_MACHINE_AUTOMATION_STALE`。事务工厂、Validate或Prepare阶段的变更在Apply前重查时返回拒绝事务，框架创建的临时产物会清理，资源不会进入合法写入阶段。检测不会撤销作者自己的非法写入，也不能修复直接Destroy造成的原生损坏。回调不得销毁物品、切换场景或读档。

事务Apply开始后不再用选择阶段的整图快照判断合法消耗；Verify和Restore沿用独立资源日记、真实能源/液体读回和材料图核对。这样既能阻止只读阶段污染，也允许正常扣料、扣电、加工与失败补偿。自定义事务仍负责自己承诺的材料语义。

原生CreateEmptyItem可以产生尚未分配存档ID（-1）的临时物品。内部只读观察按指针区分这些实例；公开 `SaveGraphApi.Items`、Encode及Decode的持久身份检查保持严格，未分配ID仍不可编码。重复已分配ID、低于-1的ID、图环、死节点和既有大小限制仍会拒绝。观察通过不代表能保存。

## 验证与剩余工作

三项正式工程构建0警告0错误，49套离线检查通过。真实配方处理器与图日记源码参与测试：配方480/0、自动化281/0、SaveGraph54/0；游戏边界使用显式替身。实机配方256/0，含23场景138项新增只读检查；自动化135/0，含两种标签污染拒绝；八组实机合计1375/0。旧机器19项只读核对通过，committed仍为2，536份ES3字节不变；本轮没有过夜或保存请求。

`machine-public-contract.audit.json` 已记录7类型、30属性及1计算方法的行为与证据，余下字段仍为待审计。全框架覆盖仍37/39组（94.9%），机器注册和运行时尚未整组完成。100%覆盖后继续知识库API、易用性、AI索引及游戏英语兼容收尾。
