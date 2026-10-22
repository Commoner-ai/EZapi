// Only native objects, hooks and unrelated registries are replaced. Layout parsing, graph
// validation, text updates, refresh routing and saved-marker handling use product sources.
namespace Il2Cpp {
    public sealed class NativeText {public float fontSize=16,fontSizeMin=14,fontSizeMax=16;public bool enableAutoSizing,enableWordWrapping=true;public string text="";public (float x,float y) GetPreferredValues(string value)=>(value.Sum(c=>c<128?fontSize*.25f:fontSize),10);public (float x,float y) GetPreferredValues(string value,float size,float height)=>(value.Sum(c=>c<128?size*.25f:size),10);}
    public sealed class TreeNodeRender {public NativeText text=new();public NativeRect rectTransform=new();public static void ApplyLocaleFont(TreeNodeRender node)=>node.text.fontSize=16;}
    public sealed class NativeRect {public (float x,float y) sizeDelta=(14,10);public (float width,float height) rect=(68,12);public UnityEngine.Vector3 localScale=new(1,1,1);}
    public class PixelElement {
        private static long _next;
        public IntPtr Pointer=new(Interlocked.Increment(ref _next));
        public PixelElement? Parent;
        public T Cast<T>() where T:class=>(T)(object)this;
        public T? TryCast<T>() where T:class=>this as T;
    }
    public sealed class PixelWindow:PixelElement {
        public string titleString="";public PixelElement? childElement;public GameItem? Owner;
        public int xPadding=2;public TreeNodeRender topCloseButton=new();public void Validate()=>titleText.text.text=titleString;
        public int Writes,widthPixels=240,heightPixels=72;public TreeNodeRender titleText=new();
        public PixelWindow(bool draggable,string title){SetTitle(title);Writes=0;}
        public bool Attach(PixelElement element){childElement=element;element.Parent=this;return true;}
        public void SetTitle(string text){var size=text.Length*10;titleString=size>widthPixels-20?text[..Math.Min(2,text.Length)]+"...":text;Writes++;Validate();}
    }
    public sealed class GridPixelElement:PixelElement {
        public int gridWidth,gridHeight;
        private readonly Dictionary<(int,int),PixelElement> _elements=new();
        public GridPixelElement(int width,int height,bool flag){gridWidth=width;gridHeight=height;}
        public GridPixelElement SetSpacing(int x,int y)=>this;
        public bool AttachPos(PixelElement child,int x,int y){_elements[(x,y)]=child;child.Parent=this;return true;}
        public PixelElement? GetElement(int x,int y)=>_elements.GetValueOrDefault((x,y));
    }
    public class GameInventory:PixelElement {
        public string identifier="";
        public GameItem? ParentOverride;
        public string NativeRole="";
        public GameItem? GetParentItem(){if(ParentOverride is not null)return ParentOverride;for(PixelElement? p=Parent;p is not null;p=p.Parent)if(p is PixelWindow w)return w.Owner;return null;}
    }
    public sealed record GridShape(int globalWidth,int globalHeight);
    public sealed class GameGridInventory:GameInventory {
        public GridShape inventoryShape;
        public GameGridInventory(int w,int h)=>inventoryShape=new(w,h);
    }
    public sealed class GameSlotInventory:GameInventory {
        public int defaultGridWidth,defaultGridHeight,Writes;
        public string Tooltip="";
        public bool ThrowOnText;
        public object? mayInventoryAddItemFunc;
        public GameSlotInventory(int w,int h,bool a,bool b){defaultGridWidth=w;defaultGridHeight=h;}
        public void SetTooltipName(string value){if(ThrowOnText)throw new InvalidOperationException("text setter failed");Tooltip=value;Writes++;}
    }
    public sealed class GameItem:PixelElement {
        public string identifier="test.mod.machine",name="Original machine",identifierName="Original machine";
        public string shortDescription="",longDescription="",flavorText="",customText="";
        public int unitCount=1,Syncs;
        public GameInventory? parentInventory;
        public PixelWindow? contentWindow;
        public TagSystem? state;
        public void SetContentWindow(PixelWindow window){contentWindow=window;window.Owner=this;}
        public void SyncModifiedState()=>Syncs++;
        public void SetName(string value)=>name=value;
        public string GetDisplayName(bool brief)=>name;
        public void RestoreContentWindowPosition() { }
        public bool IsTag(string id)=>false;
        public TagState? GetTagReadonly(string id)=>state?.dict.GetValueOrDefault(id);
    }
    public sealed class TagSystem {
        public Dictionary<string,TagState> dict=new();
        public TagState GetTag(string key){if(!dict.TryGetValue(key,out var tag))dict.Add(key,tag=new());return tag;}
    }
    public sealed class TagState {
        public string valueString="";
        public TagState Enable()=>this;
        public TagState SetString(string text){valueString=text;return this;}
        public bool IsEnabled()=>true;
    }
    public static class MachineHelper {
        public static void SetupBatterySlot(GameSlotInventory slot,GameItem machine){slot.NativeRole="battery";slot.SetTooltipName("helper battery text");}
        public static void SetupModuleBay(GameGridInventory slot,GameItem machine,object? action,object types)=>slot.NativeRole="modules";
        public static void SetupManualSlot(GameSlotInventory slot){slot.NativeRole="manual";slot.SetTooltipName("helper manual text");}
    }
    public static class ContainerHelper {public static void InitContainerItemTagOnly(GameItem machine) { }}
    public static class WaterHelper {public static int GetFreeCapacity(GameItem item)=>0;public static int GetTotalVolume(GameItem item)=>0;}
}
namespace Il2CppInterop.Runtime.InteropTypes.Arrays {
    public sealed class Il2CppStringArray {public Il2CppStringArray(string[] strings) { }}
}
namespace MelonLoader {
    public static class MelonLogger {public static readonly List<string> Warnings=new();public static void Warning(string text)=>Warnings.Add(text);}
}
namespace EZapi {
    internal static class GameSession {internal static bool OnThread=true;internal static void RequireThread(){if(!OnThread)throw new InvalidOperationException("thread");}}
    internal static class GameBuildFingerprint {internal static bool IsKnown=true;}
    internal static class NativeItemLocalization {
        internal static string Locale="en";internal static int Reads;
        internal static string CurrentLocaleCode {get{Reads++;return Locale;}}
    }
    internal static class NativeDelegateBridge {internal static object MachineSlot()=>new();}
    internal static class MachineContainerConditionApi {
        internal static void Start() { }internal static void Stop() { }
        internal static void RequireRegistered(string id,MachineLayoutDefinition spec) { }
        internal static bool Allows(Il2Cpp.GameItem item,Il2Cpp.GameInventory inventory,Il2Cpp.GameItem owner,string json,string key)=>true;
    }
    internal sealed record NativeHookStatus(bool Ready);
    internal sealed record NativeHookSpec(Type Type,string Name,Type Return,Type[] Arguments,string? Prefix=null,string? Postfix=null);
    internal sealed class NativeHookGroup {
        internal bool Ready;internal NativeHookGroup(string owner) { }
        internal void Install(Type callbacks,params NativeHookSpec[] targets)=>Ready=true;
        internal NativeHookStatus Snapshot()=>new(Ready);internal void Stop()=>Ready=false;
    }
    internal static class ItemRegistry {
        internal static bool TryGet(string id,out ItemDefinition d){d=null!;return false;}
        internal static IReadOnlyList<ItemRegistrationResult> RegisterBatch(IReadOnlyList<ItemDefinition> definitions)=>Array.Empty<ItemRegistrationResult>();
    }
}

namespace UnityEngine { public readonly record struct Vector3(float x,float y,float z); }
