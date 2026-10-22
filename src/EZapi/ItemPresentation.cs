using Il2Cpp;
namespace EZapi;

/// <summary>官方 tooltip 回调的窄适配层。只读取 EZapi 自己登记的 ID，不清空其它 Mod 内容。</summary>
internal static class ItemPresentation
{
    internal static void Early(RichTextBuilder builder,GameItem item)
    {
        PresentationApi.RefreshEffectTexts();
        DeclarativeItemLocalization.Refresh(item);
        Append(builder,item,"early");
    }
    internal static void Late(RichTextBuilder builder,GameItem item)=>Append(builder,item,"late");
    private static void Append(RichTextBuilder builder,GameItem item,string phase)
    {
        if(builder is null||item is null||item.Pointer==IntPtr.Zero||!ItemRegistry.TryGet(item.identifier,out var d))return;
        var tip=d.Tooltip;
        var locale=NativeItemLocalization.CurrentLocaleCode;
        if(d.DescriptionPlacement==phase) {
            AddText(builder,ItemText.Resolve(d,d.LocalizedShortDescription??d.LocalizedDescription,d.ShortDescription??d.Description,locale),RenderHandler.ColorPalette.None,false,false,false,false);
            AddText(builder,ItemText.Resolve(d,d.LocalizedLongDescription,d.LongDescription,locale),RenderHandler.ColorPalette.None,false,false,false,false);
            AddText(builder,ItemText.Resolve(d,d.LocalizedFlavorText,d.FlavorText,locale),RenderHandler.ColorPalette.LightGray,false,true,false,false);
            AddText(builder,ItemText.Resolve(d,d.LocalizedCustomText,d.CustomText,locale),RenderHandler.ColorPalette.None,false,false,false,false);
        }
        if(phase=="late") {
            foreach(var label in tip.Labels) {
                if(label.TagId is { } id) {
                    // 只读接口考虑容器/装备施加的临时状态，且不创建不存在的标签。
                    var state=item.GetTagReadonly(id);
                    if(state is null||!state.IsEnabled())continue;
                    var value=label.ValueKind=="none"?"": " = "+Read(state,label.ValueKind);
                    AddText(builder,ItemText.Resolve(d,label.LocalizedText,label.Text,locale)+value,Palette(label.Color),label.Bold,label.Italic,false,false);
                } else AddText(builder,ItemText.Resolve(d,label.LocalizedText,label.Text,locale),Palette(label.Color),label.Bold,label.Italic,false,false);
            }
            foreach(var line in tip.Lines.Where(x=>x.Position=="late")) AddText(builder,ItemText.Resolve(d,line.LocalizedText,line.Text,locale),Palette(line.Color),line.Bold,line.Italic,line.Underline,line.Strikethrough);
            // Localized module prose belongs to the official live tooltip callback, not a
            // helper-initialized saved string that would freeze the creation-time language.
            if(d.Module?.LocalizedAdditionalEffectDisplay is { } moduleEffect)
                AddText(builder,ItemText.Resolve(d,moduleEffect,d.Module.AdditionalEffectDisplay,locale),RenderHandler.ColorPalette.None,false,false,false,false);
            if(PresentationApi.TryGetModuleText(d.Id,item,out var moduleText)) {
                AddText(builder,moduleText.AdditionalEffect,RenderHandler.ColorPalette.None,false,false,false,false);
                foreach(var line in moduleText.Lines??Array.Empty<string>()) AddText(builder,line,RenderHandler.ColorPalette.None,false,false,false,false);
            }
            if(tip.ShowSource) AppendSource(builder,d);
        } else {
            foreach(var line in tip.Lines.Where(x=>x.Position=="early")) AddText(builder,ItemText.Resolve(d,line.LocalizedText,line.Text,locale),Palette(line.Color),line.Bold,line.Italic,line.Underline,line.Strikethrough);
        }
    }
    private static string Read(TagState state,string kind)=>kind switch {
        "string"=>state.GetString(),"int"=>state.GetInt().ToString(),"long"=>state.valueLong.ToString(),
        "float"=>state.GetFloat().ToString("0.###"),"double"=>state.GetDouble().ToString("0.###"),"bool"=>state.GetBool().ToString(),_=>""};
    private static void AddText(RichTextBuilder b,string? text,RenderHandler.ColorPalette color,bool bold,bool italic,bool underline,bool strike)
    { if(!string.IsNullOrWhiteSpace(text)) b.AddLine(text,true,color,bold,italic,underline,strike); }
    private static RenderHandler.ColorPalette Palette(string name)=>Enum.TryParse<RenderHandler.ColorPalette>(name,out var color)?color:RenderHandler.ColorPalette.None;
    internal static void AppendSource(RichTextBuilder builder,ItemDefinition d)=>AddText(builder,(NativeItemLocalization.IsEnglish?"Source: ":"来源：")+ItemRegistry.SourceName(d),RenderHandler.ColorPalette.Gray,false,true,false,false);
}

