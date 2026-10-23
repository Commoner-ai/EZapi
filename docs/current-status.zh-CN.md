# EZapi 当前版本与验收状态

0.1.87 修复语言兼容：读取游戏语言与已有物品本地化刷新不再由整份生成包装 DLL 的固定 SHA 拦截。保留主线程、getter 准确签名、locale 合法性及刷新钩子核验；读取失败可在下次刷新恢复。公开 API 签名不变，保留 0.1.86 空简介修复。只完成离线回归，当前 DLL 的真实语言切换、混合 Mod 和读档显示仍待验收。

当前当前预发布版 **0.1.87-preview.1**，程序集 **0.1.87**，MIT；通过 GitHub Releases 分发。玩家只安装一份 `Mods/EZapi.dll`；开发包含同一 DLL、XML、校验器、schema、文档、示例与进入游戏工具，调试默认关闭。

可选调试面板新增“信用点 +10,000”，每次完整点击调用原生接口并核对实际余额，正常保存会保留；不自动发钱或请求保存。见[调试工具](install-and-debug.zh-CN.md)。

保留[阵营声望界面与扩展](faction-reputation.zh-CN.md)：动态阵营列表、搜索、真实阈值横条、原版表情与效果悬停。底部只显示当前权益，无列表滚动条；重复或密集效果可切换，长文字可翻页。放大原生关闭/信息控件。兼容原生阵营扩展，并提供只读提供者 API；计算、解锁与保存仍归调用 Mod。保留 0.1.77 的王尔德首次打开闪屏修复。

正式构建入口包含 67 组离线套件、25 项 CLI 进程检查和独立 DLL 示例。当前候选的准确哈希、实际执行结果和局部原生观察统一见 [release-status.json](release-status.json)。参考功能 39 组实现不代表全框架实机验收。

**全框架、任意 Mod 组合和当前 DLL 保存重载仍待验收。** 0.1.74/0.1.76/0.1.77/0.1.78/0.1.79 的观察均留在历史字段，不能转为 0.1.87 验收。测试原始日志/存档不分发。

- [AI 开发入口](AI-START-HERE.zh-CN.md)、[公开签名](public-api.index.json)、[任务](developer-workflows.index.json)、[源码](source.index.json)。
- [安装](install-and-debug.zh-CN.md)、[源码构建](build-from-source.zh-CN.md)、[游戏自动化](automation.zh-CN.md)、[UI 预览](native-ui-preview.zh-CN.md)。
- [Tooltip](tooltip-style.zh-CN.md)、[美术参考](item-art.zh-CN.md)、[历史目录](history/README.md)。

开发包的 96 张原版美术参考保留独立权属，排除在 MIT 美术授权之外。


[原版与 Mod 全物品目录](item-catalog.zh-CN.md)：完整ID、分帧原生名称/价值/原图路径、失败条目和语言/周目失效。

[物品来源与热度](item-origin.zh-CN.md)：普通货物、赃物、固定百分比与原生初始化，写后核对。

[图片内容检测](artwork-inspection.zh-CN.md)：原生精灵裁切、黑图/透明图、紧密网格、GPU 与纯 RGBA 分析。

[物品交付凭据与对账](delivery-receipts.zh-CN.md)：礼包、订单稳定编号、游离物品标签、交付写后核对与读档恢复。交付不明时不自动补发。

[原生预制物品](prepared-items.zh-CN.md)：明确选择已填充物资箱、随机模块或真品注射器；保留普通目录创建行为，失败不自动重掷。

[带内容容器整图交付核验](delivery-graphs.zh-CN.md)：比较物资箱根、子物品与库存边；不确定时保留账本，不自动补发。
