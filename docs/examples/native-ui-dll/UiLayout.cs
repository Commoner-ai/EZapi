using EZapi;
using Il2Cpp;

namespace Author.NativeUi;

/// <summary>
/// Compile-only consumer example, called by your current machine/window factory on the game thread.
/// 调用方负责窗口关闭、场景切换和周目变化时销毁窗口；此示例不注册机器、不保存、不发物品。
/// </summary>
public static class UiLayout
{
    // Supply detached controls from your own inventories. A native control may have only one parent.
    // 传入自己库存的独立原生控件，不重复挂接其他窗口已拥有的控件。
    public static PixelWindow Build(PixelElement input, PixelElement output, Il2CppSystem.Action onProcess)
    {
        string Text(string zh, string en) => GameLanguage.Resolve(new LocalizedItemText(zh, en));
        var bays = NativeUiComponents.Row(6, UiAlign.Center,
            NativeUiComponents.Section(Text("输入", "Input"), input),
            NativeUiComponents.Section(Text("输出", "Output"), output));
        var button = NativeUiComponents.Button(Text("加工", "Process"), 96, 20, onProcess);
        var body = NativeUiComponents.Inset(NativeUiComponents.Column(6, UiAlign.Center, bays,
            NativeUiComponents.Center(button.Cast<PixelElement>(), Math.Max(bays.widthPixels, 96), 28)), 6);
        var window = new PixelWindow(true, Text("我的机器", "My machine"));
        if (!window.Attach(body)) throw new InvalidOperationException("Native layout attachment refused.");
        window.Validate(); window.Show().Center();
        return window;
    }

    // Inspect on the game thread after native layout settles. This never changes the window.
    // 原生布局稳定后只读检查实际字体、字号、对齐与溢出；重新构建/切换语言后应重新检查。
    public static NativeUiInspection Inspect(PixelWindow window) => NativeUiPreviewApi.InspectWindow(window);
}
