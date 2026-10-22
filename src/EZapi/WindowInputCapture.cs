namespace EZapi;

/// <summary>
/// 每个窗口独立的纯托管指针捕获状态；不会安装全局补丁或截获键盘。
/// Open/Close传入当前帧和鼠标按住状态；输入路由调用BlocksPointer。
/// 面板内开始的按住手势持续阻挡到释放帧，即使指针移出或窗口关闭，避免放手穿透原生库存。
/// 场景卸载/读档/失焦后Reset。仅同一输入线程使用，frame必须非负且不倒退。
/// </summary>
public sealed class WindowInputCapture
{
    private int _lastFrame=-1,_transitionFrame=-1,_releaseFrame=-1;
    private bool _open,_gesture,_wasHeld;
    public bool IsOpen=>_open;
    public void Open(int frame,bool mouseHeld)
    {
        Observe(frame);
        if(_open)return;
        _open=true;_transitionFrame=frame;_gesture|=mouseHeld;_wasHeld=mouseHeld;
    }
    public void Close(int frame,bool mouseHeld)
    {
        Observe(frame);
        if(!_open)return;
        _open=false;_transitionFrame=frame;_gesture|=mouseHeld;_wasHeld=mouseHeld;
    }
    /// <summary>
    /// true表示本窗口要求路由方跳过该次底层指针输入。多窗口取OR，原生已有拒绝不能被本方法改成允许。
    /// 同帧重复查询稳定保留释放屏障；窗口外开始的拖动不会因经过面板而永久被本窗口接管。
    /// </summary>
    public bool BlocksPointer(int frame,bool pointerInside,bool mouseHeld)
    {
        Observe(frame);
        if(mouseHeld&&!_wasHeld&&_open&&pointerInside)_gesture=true;
        if(_gesture&&!mouseHeld){_gesture=false;_releaseFrame=frame;}
        _wasHeld=mouseHeld;
        return (_open&&pointerInside)||_gesture||frame==_transitionFrame||frame==_releaseFrame;
    }
    /// <summary>释放本窗口全部托管状态；不清理游戏或其他Mod的拖拽对象。</summary>
    public void Reset(){_lastFrame=_transitionFrame=_releaseFrame=-1;_open=_gesture=_wasHeld=false;}
    private void Observe(int frame)
    {
        if(frame<0)throw new ArgumentOutOfRangeException(nameof(frame));
        if(frame<_lastFrame)throw new InvalidOperationException("EZAPI_INPUT_FRAME: frame moved backwards; Reset after input scope changes.");
        _lastFrame=frame;
    }
}
