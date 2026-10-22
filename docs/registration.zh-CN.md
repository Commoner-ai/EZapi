# 通用声明与依赖注册

`RegistrationApi` 在 Mod 初始化时登记作者版本、内容 ID、内容签名和依赖。对应参考框架的公开 Register、Snapshot、RegistrationBatch、RegistrationView 契约，EZapi 独立实现，无 Forge 运行依赖。官方指南提供各领域目录事件；通用元数据目录是 EZapi 的服务。

## 最小调用

```csharp
using EZapi;
using EZapi.Registration;

// 在 MelonMod.OnInitializeMelon 内、官方物品目录事件发生之前调用。
var result = RegistrationApi.Register("author.tools", "1.0.0", batch => batch
    .Require("author.core", "1.2.0")
    .Define(DefinitionKind.Effect, "stable", "effect:v1")
    .Define(DefinitionKind.Module, "sensor", "module:v1",
        "author.tools.stable", "author.core.base"));

// 此处result.Accepted仅说明整批元数据已接收；可以仍在等待依赖。
foreach (var entry in RegistrationApi.Snapshot())
    MelonLoader.MelonLogger.Msg($"{entry.OwnerId}: {entry.Status} {entry.Reason}");
```

`DefinitionKind` 有 Item、Module、Node、Effect、Start、Perk、Status 七类。`Define` 的 localId 自动加 `ownerId.` 前缀，也接受已带自身前缀的完整 ID；签名是作者选择的稳定内容标识，例如 `sensor:v1`。requiredIds 必须使用完整 ID，支持同批前向引用。

作者 ID 和内容 ID 使用小写字母、数字、下划线、连字符和分段点，最长160字符；版本必须是无前导零的三段非负整数，每段不超过2147483647。暂不支持预发布后缀。签名须非空、无控制字符、最长4096字符。

## 结果与依赖语义

| 结果 | 含义 |
| --- | --- |
| SubmitStatus.Accepted | 整批元数据已提交。仍应查询依赖状态。 |
| AlreadyPresent | 相同作者、版本、种类、ID、签名和依赖的幂等重放。声明顺序不影响比较。 |
| Invalid | 参数、线程、初始化阶段或作者回调错误；本批未提交。 |
| Conflict | 已有作者或全局内容 ID 对应不同声明；本批未提交。 |
| RegistrationStatus.DependenciesResolved | 本批所有声明及其依赖批次已满足。 |
| Waiting | 缺作者、版本过旧、缺内容 ID、上游等待或循环依赖；Reason提供原因。 |

后续批次提交会重新计算依赖。跨作者引用等待被引用作者整批就绪；同作者引用等待相应定义。循环依赖保持Waiting，不执行作者回调重试。不允许同作者通过再次提交升级版本，修改后需重启游戏。空批次允许只声明作者身份和依赖。

**DependenciesResolved只证明通用声明依赖满足。** 游戏物品、模块、效果和开局等仍通过 ItemRegistry、ItemApi、ModuleApi、EffectApi、StartApi 等各自入口提交，并检查各自的应用状态。此 API 不执行工厂、不创建物品、不安装补丁、不修改库存、不写存档，也不自动扫描 JSON 文件。

回调抛错时，通用元数据整批拒绝；回调中自行执行的其它 API 或游戏副作用无法由本服务回滚。因此回调只使用 Require/Define。嵌套Register被拒绝。回调返回或提交后 builder 封存；传入的字符串数组立即复制；快照及 DefinitionIds 只读，旧快照不会随后续提交改变。

Register、Register(batch)、RegisterJson 仅在框架初始化主线程且物品目录尚未开始时可用。builder 的构造、修改和提交须在同一线程。Snapshot、IsResolved 在已启动的框架主线程查询；卸载清空目录，读档和切换场景保留声明元数据。

## JSON

完整示例见 [examples/registration.json](examples/registration.json)，编辑器结构见 [registration.schema.json](../schemas/registration.schema.json)。与 items.json 分开，由作者显式打开：

```csharp
using var stream = File.OpenRead(absolutePath);
var result = RegistrationApi.RegisterJson(stream);
```

离线检查命令：`ezapi-validate --registration batch.json`。它检查单文件结构、参数及同批重复，输出 `dependencyResolutionPerformed=false`，不查询其它作者、不证明原生内容应用。JSON拒绝未知字段、重复字段、null、数字枚举及未声明种类。种类名大小写不敏感，其余字段名大小写敏感。dependencies和definitions可省略，minimumVersion默认为0.0.0。

每批最多64个作者依赖、500个定义，每定义最多64个requiredIds；目录最多256作者、8192定义；JSON最多2MiB、深度12。迭代依赖解析避免深链递归。

## 验证与定位

离线项目 `work/ezapi-registration-tests` 覆盖真实托管实现的图解析、重放、冲突、原子拒绝、封存、线程、停止重启和严格JSON。游戏调试按钮“通用注册”读取本次初始化夹具并复核载入后的查询及过晚注册拒绝；它不把元数据测试当成游戏物品生效证据。诊断字段为 `registrations` 和 `registrationDebug`。

相关源码：RegistrationBatch.cs（规则与解析）、RegistrationApi.cs（生命周期与公开入口）、JsonRegistration.cs（严格JSON）、DebugRegistration.cs（游戏进程检查）。可编译教学调用见 `src/EZapi.Sample/RegistrationExamples.cs`，方法不会自动执行。

0.1.52 实测：46个离线项目全部通过，其中通用注册114项、Schema与真实CLI交叉47项；新游戏进程内通用注册17项通过，与估值/夜间补货/NPC货源合计485项通过、0失败、常规截图0张。535个ES3文件哈希不变。元数据夹具覆盖真实初始化阶段及载入后的查询，未宣称验证其它领域的工厂、玩法或保存效果。
