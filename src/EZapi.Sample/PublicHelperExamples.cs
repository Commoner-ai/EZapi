using EZapi;

namespace EZapi.Sample;

/// <summary>只保存托管帮助器；不缓存玩家、库存、窗口或原生物品。</summary>
public sealed class PublicHelperExamples
{
    private readonly ModLog _log=new("example.tools",LogLevel.Info);
    private readonly WindowInputCapture _input=new();

    public void WindowOpened(int frame,bool mouseHeld)
    {
        _input.Open(frame,mouseHeld);
        _log.Info("Tools opened / 工具已打开");
    }
    public void WindowClosed(int frame,bool mouseHeld)=>_input.Close(frame,mouseHeld);

    // 在自己的输入路由中调用。true时保留拒绝，不让关闭按钮的释放事件落到原生库存。
    // 框架不替作者全局patch输入；失焦/场景卸载/读档时调用Reset。
    public bool ShouldBlockUnderlyingPointer(int frame,bool pointerInside,bool mouseHeld)
        =>_input.BlocksPointer(frame,pointerInside,mouseHeld);
    public void InputScopeEnded()=>_input.Reset();

    public string ResolveTerminal(string itemId)
    {
        // 只有明确由自己维护的历史ID才放入aliases。不自动迁移任何存档物品。
        var resolved=ManufacturingTerminal.NormalizeItemId(itemId,new[]{"example.tools.old_press"});
        _log.Write(LogLevel.Debug,()=>"Resolved terminal: "+resolved);
        return resolved;
    }
}
