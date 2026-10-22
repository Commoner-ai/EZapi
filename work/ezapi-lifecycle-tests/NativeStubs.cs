namespace Il2Cpp {
    public class NativeObject {
        private static int serial=100;
        public IntPtr Pointer {get;set;}=new(Interlocked.Increment(ref serial));
        public T? TryCast<T>()where T:class=>this as T;
        public T Cast<T>()where T:class=>(T)(object)this;
    }
    public class GraphNodeStorage:NativeObject {public List<GraphNodeStorage> children=new();}
    public class PixelElement:GraphNodeStorage {}
    public class TagState {public int Value;public TagState Enable()=>this;public TagState SetInt(int n){Value=n;return this;}public int GetInt()=>Value;}
    public class TagSystem {public Dictionary<string,TagState> Tags=new();public TagState GetTag(string id){if(!Tags.TryGetValue(id,out var tag))Tags[id]=tag=new();return tag;}}
    public class PixelWindow:GraphNodeStorage {
        public PixelElement? childElement;
        public PixelWindow(bool draggable,string title){}
        public bool Attach(PixelElement element){childElement=element;children.Add(element);return true;}
    }
    public class GameItem:GraphNodeStorage {
        private static int serial;
        public int uniqueId=Interlocked.Increment(ref serial),unitCount=1;
        public string identifier="test.item";
        public string name="test";
        public PixelWindow? contentWindow;
        public TagSystem? state=new();
        public GameItem SetContentWindow(PixelWindow window){contentWindow=window;children.Add(window);if(window.childElement is GameInventory inv)inv.Owner=this;return this;}
        public TagState? GetTagReadonly(string id)=>state?.Tags.GetValueOrDefault(id);
        public void SyncModifiedState(){}
        public bool Owned=true,Removable=true;
        public int RemoveLimit=int.MaxValue;
        public GameInventory? parentInventory;
        public GridShape shape=new(1,1);
        public GridShape modifiedShape=>shape;
        public bool MayRemove()=>HarmonyLib.Sim.Run(typeof(GameItem).GetMethod(nameof(MayRemove))!,this,Array.Empty<object?>(),()=>Removable);
        public int MaxNumRemove()=>HarmonyLib.Sim.Run(typeof(GameItem).GetMethod(nameof(MaxNumRemove))!,this,Array.Empty<object?>(),()=>RemoveLimit);
    }
    public class GameInventory:PixelElement {
        public string identifier="test.storage";
        public GameItem? Owner;
        public GameItem? GetParentItem()=>Owner;
        public GraphNodeStorage? parent;
        public List<GameItem> childItems=new();
        public bool InsertLocked,RemoveLocked,PlayerInsert=true,OwnershipAccept=true;
        public bool IsInsertLocked()=>InsertLocked;
        public bool IsRemoveLocked()=>RemoveLocked;
        public virtual SlotMarker? TryInventorySlot(GameItem item,int requestedNum,GridShape shape,object? state=null)=>null;
    }
    public class GameGridInventory:GameInventory {
        public GridShape inventoryShape;
        public int AcceptCalls;
        public bool NativeAdmission=true;
        public int TransferLimit=int.MaxValue;
        public Action? BeforeFind;
        public Func<SlotMarker,int,int>? AcceptOverride;
        public Func<GameItem,GridShape,bool>? Admit;
        public GameGridInventory(int width=8,int height=8){inventoryShape=new(width,height,new byte[width*height]){Outside=1};}
        public GameGridInventory SetShape(int width,int height){inventoryShape=new(width,height,new byte[width*height]){Outside=1};return this;}
        public bool MayHaveValidInventorySlot(GameItem item)=>HarmonyLib.Sim.Run(typeof(GameGridInventory).GetMethod(nameof(MayHaveValidInventorySlot))!,this,new object?[]{item},()=>NativeAdmission);
        public bool CheckInventorySlotStillValid(GameItem item,GridShape shape,TagSystem state)=>HarmonyLib.Sim.Run(typeof(GameGridInventory).GetMethod(nameof(CheckInventorySlotStillValid))!,this,new object?[]{item,shape,state},()=>true);
        public bool UncheckedAccept(GameItem item)=>HarmonyLib.Sim.Run(typeof(GameGridInventory).GetMethod(nameof(UncheckedAccept))!,this,new object?[]{item},()=>{Add(item);return true;});
        public bool Expel(GameItem item)=>HarmonyLib.Sim.Run(typeof(GameGridInventory).GetMethod(nameof(Expel))!,this,new object?[]{item},()=>{Remove(item);return true;});
        public SlotMarker? TryFindOneValidInventorySlot(GameItem item,bool keep=false){
            BeforeFind?.Invoke();
            var stack=childItems.FirstOrDefault(i=>i.identifier==item.identifier&&i.unitCount>1);
            if(stack is not null)return new(){item=item,inventory=this,targetItem=stack,numTransfer=Math.Min(item.unitCount,TransferLimit)};
            for(var y=inventoryShape.minY;y<inventoryShape.minY+inventoryShape.globalHeight;y++)for(var x=inventoryShape.minX;x<inventoryShape.minX+inventoryShape.globalWidth;x++){
                var shape=new GridShapeBuilder(item.shape).SetPosition(x,y).Clone();
                var slot=TryInventorySlot(item,item.unitCount,shape);if(slot is not null)return slot;
            }
            return null;
        }
        public override SlotMarker? TryInventorySlot(GameItem item,int units,GridShape shape,object? state=null){
            return TryInventorySlot(item,units,new UnityEngine.Vector2(),shape,state as TagSystem);
        }
        public SlotMarker? TryInventorySlot(GameItem item,int units,UnityEngine.Vector2 position,GridShape shape,TagSystem? state){
            var method=typeof(GameGridInventory).GetMethod(nameof(TryInventorySlot),new[]{typeof(GameItem),typeof(int),typeof(UnityEngine.Vector2),typeof(GridShape),typeof(TagSystem)})!;
            return HarmonyLib.Sim.Run<SlotMarker?>(method,this,new object?[]{item,units,position,shape,state},()=>FindSlot(item,units,shape));
        }
        private SlotMarker? FindSlot(GameItem item,int units,GridShape shape){
            if(InsertLocked||Admit?.Invoke(item,shape)==false)return null;
            for(var y=shape.minY;y<shape.minY+shape.globalHeight;y++)for(var x=shape.minX;x<shape.minX+shape.globalWidth;x++){
                if(shape.Get(x,y)==0)continue;
                if(inventoryShape.Get(x,y)!=0||childItems.Any(i=>i.modifiedShape.Get(x,y)>0))return null;
            }
            return new(){item=item,inventory=this,itemGridShape=shape.Clone(),numTransfer=Math.Min(units,TransferLimit)};
        }
        public void Add(GameItem item,int x=0,int y=0){item.parentInventory=this;item.shape=new GridShapeBuilder(item.shape).SetPosition(x,y).Clone();childItems.Add(item);children.Add(item);}
        public void Remove(GameItem item){childItems.Remove(item);children.Remove(item);item.parentInventory=null;}
    }
    public class SlotMarker:NativeObject {
        public GameItem item=null!;public GameGridInventory inventory=null!;public GameItem? targetItem;
        public GridShape itemGridShape=null!;public int numTransfer;
        public bool IsValid()=>item is not null&&inventory is not null&&!inventory.InsertLocked;
        public int TryAcceptOnce(int amount=-1)=>HarmonyLib.Sim.Run(typeof(SlotMarker).GetMethod(nameof(TryAcceptOnce))!,this,new object?[]{amount},()=>AcceptNative(amount));
        private int AcceptNative(int amount){
            inventory.AcceptCalls++;
            if(inventory.AcceptOverride is {} callback)return callback(this,amount);
            var source=(GameGridInventory)item.parentInventory!;
            var move=Math.Min(amount,Math.Min(numTransfer,item.unitCount));
            if(targetItem is not null){targetItem.unitCount+=move;item.unitCount-=move;if(item.unitCount==0){source.Remove(item);item.Pointer=IntPtr.Zero;}return move;}
            if(move==item.unitCount){source.Remove(item);inventory.Add(item,itemGridShape.minX,itemGridShape.minY);item.shape=itemGridShape.Clone();}
            else {item.unitCount-=move;var split=new GameItem(){identifier=item.identifier,unitCount=move,Owned=item.Owned,shape=itemGridShape.Clone()};inventory.Add(split,itemGridShape.minX,itemGridShape.minY);}
            return move;
        }
    }
    public class GridShape:NativeObject {
        internal byte[] Data;
        public int minX,minY,orientation;public bool flipped;
        public int globalWidth,globalHeight;
        public GridShape(int w,int h,byte[]? data=null){globalWidth=w;globalHeight=h;Data=data??Enumerable.Repeat((byte)1,w*h).ToArray();}
        public byte Outside;
        public byte GetOutsideBounds()=>Outside;
        public byte Get(int x,int y)=>x>=minX&&y>=minY&&x<minX+globalWidth&&y<minY+globalHeight?Data[(y-minY)*globalWidth+x-minX]:Outside;
        public GridShape Clone()=>new(globalWidth,globalHeight,(byte[])Data.Clone()){minX=minX,minY=minY,orientation=orientation,flipped=flipped,Outside=Outside};
    }
    public class GridShapeBuilder {
        private GridShape s;
        public GridShapeBuilder(GridShape source){s=source.Clone();}
        public GridShapeBuilder SetTransform(int x,int y,bool flipped,int orientation){
            var turns=(orientation-s.orientation+4)%4;
            for(var i=0;i<turns;i++){
                var old=s;var rotated=new GridShape(old.globalHeight,old.globalWidth,new byte[old.Data.Length]);
                for(var yy=0;yy<old.globalHeight;yy++)for(var xx=0;xx<old.globalWidth;xx++)rotated.Data[xx*rotated.globalWidth+old.globalHeight-1-yy]=old.Data[yy*old.globalWidth+xx];
                s=rotated;
            }
            s.minX=x;s.minY=y;s.flipped=flipped;s.orientation=orientation;return this;
        }
        public GridShapeBuilder SetPosition(int x,int y){s.minX=x;s.minY=y;return this;}
        public GridShape Clone()=>s.Clone();
    }
    public class PlayerStore:NativeObject {
        public int saveSlotId=1;public string runID="test";
        public void LoadGame(){} public void EndNight(){} public void EndDay(){}

        public static PlayerStore? instance=new();
        public List<GameItem> Found=new();
        public Dictionary<string,GameItem> saveBags=new();
        public GameGridInventory? gridInv;
        public List<GameItem> FindAllItem(bool owned)=>Found;
    }
    public class ItemMouseDragHandler:NativeObject {public static ItemMouseDragHandler? current;public bool IsDraggingItem;}
    public class ItemMultiSelectHandler:NativeObject {public enum State{Idle,PendingBand,Banding,Selected,PendingGroupDrag,GroupDragging}public static ItemMultiSelectHandler? current;public State state;}
    public static class GeneralHelper {
        public static bool IsItemOwned(GameItem item)=>item.Owned;
        public static bool MayPlayerInsertInto(GameInventory inv)=>inv.PlayerInsert;
        public static bool MayOwnershipAcceptItem(GameItem item,GameInventory inv)=>inv.OwnershipAccept;
    }
    public class GameGridScrollableInventory:GameInventory {
        public bool Expel(GameItem item)=>HarmonyLib.Sim.Run(typeof(GameGridScrollableInventory).GetMethod(nameof(Expel))!,this,new object?[]{item},()=>{childItems.Remove(item);return true;});
    }
    public class GameSlotInventory:GameInventory {
        public bool Expel(GameItem item)=>HarmonyLib.Sim.Run(typeof(GameSlotInventory).GetMethod(nameof(Expel))!,this,new object?[]{item},()=>{childItems.Remove(item);return true;});
    }
    public static class ContainerHelper {public static void InitContainerItemTagOnly(GameInventory inventory,GameItem owner){inventory.Owner=owner;}}
}
namespace UnityEngine {public struct Vector2 {}}

namespace EZapi {
 internal static class RunData {internal static void ValidateModId(string id){if(string.IsNullOrWhiteSpace(id))throw new ArgumentException("owner");}}
 public enum GameEvent { GameLoadedInit, GameLoadedNormal }
 internal static class InventoryRuntime {internal static bool CatalogReady=true;internal static IReadOnlyList<NativeHookStatus> Status()=>Array.Empty<NativeHookStatus>();}
}
namespace Il2Cpp {
 public static class ModHook {public static void FireOnHandlingNightlyServicesEarly(){} public static void FireOnHandlingNightlyServicesLate(){} public static void FireOnGoingSleepLate(){} public static void FireOnGameLoadedLate(){}}
 public static class StoreStation {public static int Day=5;public static bool FailDay;public static int GetDayCounter()=>FailDay?throw new Exception("day unavailable"):Day;}
}
