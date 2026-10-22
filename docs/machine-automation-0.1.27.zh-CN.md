# 0.1.27 机器自动化验收

本版新增BeforeBatch/AfterBatch、选择/阻止与冲突、可释放订阅、真实BeforeNight调度、动态夜间额度、持久夜次标记和RecoveryFailed隔离。修复切场景读取已销毁Unity对象，以及夜间夹具错误投递PlayerStore.gridInv。开发契约见[machine-automation.zh-CN.md](machine-automation.zh-CN.md)。

## 证据范围

- 10套共1,270项离线断言：自动化/调度217、注册81、配方406、事务55、倒液97、图鉴101、服务60、生产93、guards97、内容包63；替身只证明对应托管算法，不替代游戏。
- 最终DLL同进程合并560项实机断言：自动化119、注册42、标准材料109、配方118、事务52、布局46、工厂48、模块14、原生物品12。Before/After主动抛错产生两条预期WARNING。
- 独立测试档530：真实创建四台默认/动态/停用/无效额度机器，通过正常开店/打烊/睡觉/结算；第一晚分别产出1/2/0/0。重复同一BeforeNight上下文，投入/产物/夜次不变。
- 退出进程重新启动，载入同一测试档，核对数量、独立输入/输出/窗口与持久夜次。第二晚默认机器累计2件、动态机器因材料不足累计3件；重复调度没有多产。随包verification/native目录与native-gameplay-check.json记录最终检查结果。
- 373个直接游戏方法完整签名匹配，Unity包装直接88/可达93无反剥离空壳；三项目编译结果随包。只加载EZapi.dll，不自动调用SaveGame/InitialSave。

## 保留边界

游戏正常保存重载不等于任意特殊机器的SaveGraph.Decode支持，也不保证进程崩溃时持久补偿。指定液体成分默认扣液、内装物材料、外部电网、液体专用输出槽和共享制造终端仍有缺口。只加载EZapi的检查不代表联机大厅/搜打撤组合兼容已通过。完整Forge目标仍active；覆盖清单为16组实现、12组部分、11组缺失。

原生证据分为candidate/night-failed（调查失败）、verified/night-verified/reload（中间候选）以及final/final-night/final-reload（最终DLL）。禁止把调查截图或旧DLL测试冒充最终包实测。发布包只收录最终DLL验收与明确标注的局限，不含玩家存档。

最终只读校验：524份历史文件与527测试档SHA256均未改变，527拥有量/液体/测试计数基线未变；新建530独立验证。实机日志无ERROR。场景切换与新局、首晚、新进程载入和第二晚均用同一最终DLL；哈希见随包verification.json。
