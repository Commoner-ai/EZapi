// Instruments only native field writes, text output and hook declarations. These stand-ins
// do not prove Harmony installation, UI layout, saved-field semantics or game language switching.
namespace Il2Cpp {
    public sealed class NativeText {public float fontSize=16,fontSizeMin=14,fontSizeMax=16;public bool enableAutoSizing,enableWordWrapping=true;public string text="";public (float x,float y) GetPreferredValues(string value){var size=enableAutoSizing?fontSizeMax:fontSize;return(value.Sum(c=>c<128?size*.25f:size),10);}public (float x,float y) GetPreferredValues(string value,float size,float height)=>(value.Sum(c=>c<128?size*.25f:size),10);}
    public sealed class TreeNodeRender {public NativeText text=new();public NativeRect rectTransform=new();public static void ApplyLocaleFont(TreeNodeRender node)=>node.text.fontSize=16;}
    public sealed class NativeRect {public (float x,float y) sizeDelta=(14,10);public (float width,float height) rect=(68,12);public UnityEngine.Vector3 localScale=new(1,1,1);}
    public sealed class PixelWindow {
        public IntPtr Pointer=new(1);public string titleString="";public int Writes;
        public int xPadding=2;public TreeNodeRender topCloseButton=new();public void Validate()=>titleText.text.text=titleString;
        public int widthPixels=68,heightPixels=72;public TreeNodeRender titleText=new();
        public void SetTitle(string text){var size=text.Length*10;titleString=size>widthPixels-20?text[..Math.Min(2,text.Length)]+"...":text;Writes++;Validate();}
    }
    public sealed class GameItem {
        public IntPtr Pointer=new(1);
        public string identifier="",identifierName="",name="",shortDescription="",longDescription="",flavorText="",customText="";
        public PixelWindow? contentWindow;public int NameWrites;
        public Dictionary<string,TagState> Tags=new();
        public void SetName(string text){name=text;NameWrites++;}
        public string GetDisplayName(bool compact)=>name;
        public void RestoreContentWindowPosition() {
            if(contentWindow is not null)contentWindow.SetTitle("EZ...");
            EZapi.NativeHookGroup.InvokePostfix(this);
        }
        public TagState? GetTagReadonly(string id)=>Tags.GetValueOrDefault(id);
    }
    public sealed class TagState {
        public bool Enabled=true;public int Value;public long valueLong;
        public bool IsEnabled()=>Enabled;
        public string GetString()=>"value";
        public int GetInt()=>Value;
        public float GetFloat()=>Value;
        public double GetDouble()=>Value;
        public bool GetBool()=>Enabled;
    }
    public static class RenderHandler {
        public enum ColorPalette {None,LightGray,Gray,LightTurquoise}
    }
    public sealed class RichTextBuilder {
        public List<string> Lines=new();
        public void AddLine(string text,bool add,RenderHandler.ColorPalette color,bool bold,bool italic,bool underline,bool strike)=>Lines.Add(text);
    }
}
namespace MelonLoader {
    public static class MelonLogger {public static List<string> Warnings=new();public static void Warning(string text)=>Warnings.Add(text);}
}
namespace EZapi {
    internal static class MachineLayoutApi {internal static bool RefreshLocalization(Il2Cpp.GameItem item)=>false;}
    internal static class GameSession {internal static bool OnThread=true;}
    internal static class GameBuildFingerprint {internal static bool IsKnown=true;}
    internal static class NativeItemLocalization {internal static string CurrentLocaleCode="en";internal static bool IsEnglish=>!CurrentLocaleCode.StartsWith("zh");}
    internal sealed record NativeHookStatus(bool Ready);
    internal sealed record NativeHookSpec(Type Type,string Name,Type Return,Type[] Arguments,string? Prefix=null,string? Postfix=null);
    internal sealed class NativeHookGroup {
        private static Type? _callbacks;private static NativeHookSpec[] _targets=Array.Empty<NativeHookSpec>();
        internal static bool NativeTargetsAvailable=true;
        internal bool Ready;internal NativeHookGroup(string owner) { }
        internal void Install(Type callbacks,params NativeHookSpec[] targets){_callbacks=callbacks;_targets=targets;Ready=NativeTargetsAvailable;}
        internal static void InvokePostfix(Il2Cpp.GameItem item){
            var target=_targets.SingleOrDefault(t=>t.Name==nameof(Il2Cpp.GameItem.RestoreContentWindowPosition));
            if(target?.Postfix is {} callback)_callbacks!.GetMethod(callback,System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.NonPublic)!.Invoke(null,new object[]{item,true});
        }
        internal NativeHookStatus Snapshot()=>new(Ready);
        internal void Stop()=>Ready=false;
    }
    internal sealed record ModuleText(string? AdditionalEffect=null,IReadOnlyList<string>? Lines=null);
    internal static class PresentationApi {
        internal static void RefreshEffectTexts() { }
        internal static bool TryGetModuleText(string id,Il2Cpp.GameItem item,out ModuleText text){text=new();return false;}
    }
    internal static class ItemRegistry {
        internal static readonly Dictionary<string,ItemDefinition> Definitions=new();
        internal static bool Applied=true;
        internal static bool TryGet(string id,out ItemDefinition d)=>TryGetDeclarative(id,out d);
        internal static bool TryGetDeclarative(string id,out ItemDefinition d){d=null!;return Applied&&Definitions.TryGetValue(id,out d!);}
        internal static string SourceName(ItemDefinition d)=>ItemText.Resolve(d,d.Tooltip.LocalizedSourceName,d.Tooltip.SourceName??"Sample Pack",NativeItemLocalization.CurrentLocaleCode);
        internal static IReadOnlyList<ItemRegistrationResult> RegisterBatch(IReadOnlyList<ItemDefinition> definitions)=>Array.Empty<ItemRegistrationResult>();
    }
}

namespace UnityEngine { public readonly record struct Vector3(float x,float y,float z); }
