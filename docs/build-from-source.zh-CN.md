# 从源码构建 EZapi

EZapi 自 0.1.73-preview.2 起公开当前源码，采用 [MIT 许可证](../LICENSE)。框架位于 `src/EZapi`，离线校验器位于 `src/EZapi.ContentValidator`，调用示例位于 `src/EZapi.Sample`。旧私有仓库历史不包含在此仓库中。

## 只运行离线验证

安装 .NET 8 SDK、Python 3.12 或更新版本、PowerShell。无需游戏或 MelonLoader，无需启动游戏：

```powershell
git clone https://github.com/Commoner-ai/EZapi.git
cd EZapi
pwsh -NoProfile -File ./Build-EZapi.ps1 -OfflineOnly
```

运行全部 59 组源代码链接测试、25 项校验器独立进程检查及能力索引检查。测试使用明确的原生边界替身；测试通过不证明真实游戏 UI、原生方法或跨天存档正常。输出报告在忽略的 `work/offline-tests-report.json`。

## 构建游戏 DLL 与安装包候选

另外安装 .NET 6 SDK；准备自己拥有的 Probably Stolen 游戏和适用的 MelonLoader IL2CPP .NET6，并至少启动过一次以生成 `MelonLoader/Il2CppAssemblies`。当前适配基线为 Demo050REV5-L。游戏及加载器 DLL 不随源码分发。

```powershell
pwsh -NoProfile -File ./Build-EZapi.ps1 -GameDir '你的游戏目录' -OutputDir './outputs/my-candidate'
python scripts/Package-EZapi.py --candidate outputs/my-candidate --output outputs/my-binary-packages
```

输出目录必须不存在。构建框架、示例、校验器及索引工具，运行离线验证，重新生成并核对当前 DLL 的元数据索引，再生成安装包候选。不会启动、操作或修改游戏，不会安装到 Mods。重建 DLL 的 SHA256 可能因编译器版本和路径而变化，实机验收不会从历史 DLL 继承。

## 给 AI 或贡献者的入口

- [AI 任务入口](AI-START-HERE.zh-CN.md)、[公开签名索引](public-api.index.json)、[源码领域索引](source.index.json)。
- `work/ezapi-*-tests/`：可运行的离线测试工程，只包含明确测试替身，不包含游戏实现。
- `work/ezapi-inspect/`：使用 Mono.Cecil 读取程序集签名与 XML 注释的工具源码。
- `.github/workflows/offline.yml`：无需游戏文件的 GitHub Actions 检查。
- `scripts/Fetch-OfficialDocs.ps1`：单独获取固定提交的官方参考文档，按原仓库许可使用。

玩家直接从 [Releases](https://github.com/Commoner-ai/EZapi/releases) 下载游戏包，仍只安装一份 EZapi.dll。开发包保留自动进入游戏工具；这些脚本仅在明确调用时操作电脑，见[自动化说明](automation.zh-CN.md)。
