// Instrument native graph calls; these do not simulate Unity windows, ownership, admission or saved graph restoration.
namespace Il2CppSystem.Collections.Generic { public sealed class List<T>:System.Collections.Generic.List<T> { } }
namespace Il2Cpp {
    public sealed class NativeText {public float fontSize=16,fontSizeMin=14,fontSizeMax=16;public bool enableAutoSizing,enableWordWrapping=true;public string text="";public (float x,float y) GetPreferredValues(string value)=>(value.Sum(c=>c<128?fontSize*.25f:fontSize),10);public (float x,float y) GetPreferredValues(string value,float size,float height)=>(value.Sum(c=>c<128?size*.25f:size),10);}
    public sealed class TreeNodeRender {public NativeText text=new();public NativeRect rectTransform=new();public static void ApplyLocaleFont(TreeNodeRender node)=>node.text.fontSize=16;}
    public sealed class NativeRect {public (float x,float y) sizeDelta=(14,10);public (float width,float height) rect=(68,12);public UnityEngine.Vector3 localScale=new(1,1,1);}
    public sealed class PixelElement {public object Value=null!;}
    public sealed class GameGridInventory {
        public string identifier="";public int Width,Height;public string? Shape;
        public bool NativeInitialized;public string[]? AllowedTags,AllowedIds;
        public GameGridInventory(int width,int height)=>(Width,Height)=(width,height);
        public GameGridInventory(string shape,int width)=>(Width,Height,Shape)=(width,shape.Length/width,shape);
        public T? TryCast<T>() where T:class=>PixelWindow.BadCast?null:new PixelElement{Value=this} as T;
    }
    public sealed class PixelWindow {
        public int xPadding=2;public TreeNodeRender topCloseButton=new();public void Validate()=>titleText.text.text=titleString;
        public static bool FailAttach,BadCast;public static int Created;
        public bool Draggable;public string Title="";public GameGridInventory? Grid;
        public IntPtr Pointer=new(1);public int widthPixels=68,heightPixels=72;public TreeNodeRender titleText=new();
        public string titleString{get=>Title;set=>Title=value;}
        public void SetTitle(string title){Title=title.Length*10>widthPixels-20?title[..Math.Min(2,title.Length)]+"...":title;Validate();}
        public PixelWindow(bool canMove,string title){Draggable=canMove;Created++;SetTitle(title);}
        public bool Attach(PixelElement element){if(FailAttach)return false;Grid=(GameGridInventory)element.Value;return true;}
    }
    public static class ContainerHelper {
        public static int Calls;public static bool Throw;
        public static void InitContainerItem(GameGridInventory grid,GameItem item,
            Il2CppSystem.Collections.Generic.List<string>? tags,Il2CppSystem.Collections.Generic.List<string>? ids) {
            Calls++;grid.NativeInitialized=true;grid.AllowedTags=tags?.ToArray();grid.AllowedIds=ids?.ToArray();
            item.activateDefault=true;item.canUseOutsideCombat=true;item.state=new();item.state.InitTagString("container");
            if(Throw)throw new InvalidOperationException("native container failure");
        }
    }
}

namespace UnityEngine { public readonly record struct Vector3(float x,float y,float z); }
