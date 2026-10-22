# 0.1.19 模块阶段验收

日期2026-10-20，游戏050rev5-L。该阶段实现ModuleApi八个入口，完整Forge最低范围仍未完成；39组当前15实现、9部分、15缺失。DLL不引用或加载Forge。

本轮实际执行四套离线检查共251个唯一案例：模块84、内容包63、注册/应用通知52、生命周期52。模块测试直接编译真实Modules.cs、GameSession.cs和NativeHooks.cs，用替身模拟原生/Harmony；不能把它作为IL2CPP执行证据。三个项目构建均零警告零错误。最终354条游戏方法直接引用的完整签名匹配，0缺失。

实际加载最终DLL后，测试档527完成模块14通过、0失败，原有十件物品10通过、0失败。四组七个精确原生目标均安装成功；真实准入允许自定义类型，未注册类型被拒绝；TryAcceptOnce接受一件；GetModuleInv/FindInstalled返回真实模块；重算触发AfterTemp/BeforeAggregate/AfterAggregate；直接调用原生安装/移除阶段方法，分别收到BeforeAdded/AfterRemoved；ActivateOnUseAction与OnUsedLearningAlgo触发动作/逐调用成长；原生Expel后查询为空；Dispose后再次调用动作/成长不再收到内容回调。

当前Demo枚举到的目录中没有blender工厂，部分机器目录为空。测试显式构造2×2 GridPixelElement与(1,1)处的4×4模块仓，调用官方SetupModuleBay；`syntheticMachine=true`。测试壳/模块仅为临时实例，为满足原生准入设为owned，未交付玩家。报告scope中的临时“unowned factory”措辞不代表此测试壳的ownership为false，准确值见slot inputs中的ownedMachine/ownedModule。此证据不证明生产机器工厂、成功加工、跨夜或保存重建。

失败探测记录保留：默认目录查询没有找到原生机器；遍历实际目录后仍无blender；初始测试壳与模块未标owned，原生准入拒绝。补充占格后仍拒绝，最终在临时实例上标owned后通过。没有绕过原生准入，没有改全局锁或玩家所有权，也没有为测试伪造生产成功。

清理后模块订阅0、准入声明0、待完成成长帧0。玩家ownedCounts与0.1.18最终快照完全一致，无ezapi.debug临时物品。测试计数仍1，三瓶为499ml/500ml/1ml，工具包仍(7,6)。524份历史档和测试档527的文件哈希均未变；本轮不执行保存。游戏已正常退出。包内verification提供最终诊断、输入脚本/计划/记录、截图、日志和哈希，排除.es3。

尚未实测：生产机器实际成功/失败加工、模块机存档恢复、原生跳过/异常路径（离线覆盖）、多人/搜打撤组合。低层模块/节点/设施物品注册、机器/配方、持久库存端点等仍须继续实现；多语言扩展暂缓。ModuleApi已实现不等于整个EZapi目标完成。
