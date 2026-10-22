// 只模拟原生边界，链接实际生产缓存/裁切源码；不模拟 GPU 像素结果，后者在游戏中验证。
namespace UnityEngine {
    public class Object {
        private static long _next;
        public IntPtr Pointer {get;}=new(++_next);
        public bool Destroyed;
        public static readonly List<Object> DestroyedObjects=new();
        public static void Destroy(Object value){value.Destroyed=true;DestroyedObjects.Add(value);}
    }
    public class Texture2D(int width,int height,TextureFormat format=TextureFormat.RGBA32,bool mip=false):Object {
        public int width=width,height=height;public TextureFormat Format=format;public bool Mip=mip;public FilterMode filterMode;public TextureWrapMode wrapMode;
        public static bool FailRead;
        public void ReadPixels(Rect rect,int x,int y,bool mip){if(FailRead)throw new InvalidOperationException("readback failure");}
        public void Apply(bool mip,bool discard){}
        public Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<Color32> GetPixels32()=>new(width*height);
        public void SetPixels32(Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<Color32> value){}
    }
    public class Sprite: Object {
        public Texture2D texture=new(256,128);
        public Rect textureRect=new(32,16,64,32),rect=new(0,0,80,48);
        public Vector2 textureRectOffset=new(8,4);public int packingRotation;
        public Vector2 pivot=new(40,24);public float pixelsPerUnit=1;
        public Vector2[] vertices={new(-32,-20),new(32,-20),new(32,12),new(-32,12)};
        public Vector2[] uv={new(.125f,.125f),new(.375f,.125f),new(.375f,.375f),new(.125f,.375f)};
        public ushort[] triangles={0,1,2,0,2,3};
    }
    public class RenderTexture(int width,int height,int depth,RenderTextureFormat format):Object {
        public int Width=width,Height=height,Depth=depth;public RenderTextureFormat Format=format;public static RenderTexture? active;public static bool FailCreate;
        public bool Released;
        public bool Create()=>!FailCreate;
        public void Release(){Released=true;}
    }
    public class GUIStyle {
        public static bool FailCreate;
        public GUIStyle(){if(FailCreate)throw new InvalidOperationException("style failure");}
        public GUIStyleState normal=new();
    }
    public class GUIStyleState {public Texture2D? background;}
    public record struct Rect(float x,float y,float width,float height);
    public record struct Vector2(float x,float y);
    public record struct Color32(byte r,byte g,byte b,byte a);
    public enum TextureFormat {RGBA32}
    public enum RenderTextureFormat {ARGB32}
    public enum FilterMode {Point}
    public enum TextureWrapMode {Clamp}
    public static class Graphics {public static void Blit(Texture2D source,RenderTexture dest,Vector2 scale,Vector2 offset){}}
    public static class GUI {public static int Boxes;public static void Box(Rect rect,string text,GUIStyle style){Boxes++;}}
}
namespace Il2Cpp {
    public class GameItem {
        public object? parentInventory;
        public bool Destroyed;public bool FailDestroy;
        public T? TryCast<T>() where T:class=>this as T;
        public void Destroy(){if(FailDestroy)throw new InvalidOperationException("destroy failure");Destroyed=true;}
    }
    public class GameItemElement:GameItem {
        public UnityEngine.Sprite Sprite=new();
        public UnityEngine.Sprite ResolveItemSprite()=>Sprite;
    }
}
namespace EZapi {
    internal static class NativeLocaleCalendarGuard {internal static bool Start()=>true;internal static void Stop(){}}
    public static class NativeItemLocalization {public static string CurrentLocaleCode="zh";}
    public record GameRunIdentity(int Epoch);
    public static class NativeAccess {
        public static GameRunIdentity Current=new(1);
        public static GameRunIdentity RequireRun()=>Current;
        public static bool SameRun(GameRunIdentity run)=>run==Current;
    }
    public static class Items {
        public static int Creates;public static readonly List<Il2Cpp.GameItemElement> Created=new();
        public static Action<Il2Cpp.GameItemElement>? OnCreate;
        public static bool Exists(string id)=>!id.StartsWith("missing",StringComparison.Ordinal);
        public static Il2Cpp.GameItem Create(string id,bool owned){Creates++;var item=new Il2Cpp.GameItemElement();Created.Add(item);OnCreate?.Invoke(item);return item;}
    }
}

namespace UnityEngine.ResourceManagement.AsyncOperations {
 public enum AsyncOperationStatus{None,Succeeded,Failed}
 public sealed class TestHandle {public bool Valid=true,IsDone=true,Throw;public AsyncOperationStatus Status=AsyncOperationStatus.Succeeded;public bool IsValid(){if(Throw)throw new InvalidOperationException("released handle");return Valid;}}
}
namespace UnityEngine.Localization.Settings {
 public static class LocalizationSettings {
  private static Il2CppSystem.Action<UnityEngine.Localization.Locale>? changed;
  public static int Subscriptions,Unsubscriptions;
  public static void add_SelectedLocaleChanged(Il2CppSystem.Action<UnityEngine.Localization.Locale> value){changed=value;Subscriptions++;}
  public static void remove_SelectedLocaleChanged(Il2CppSystem.Action<UnityEngine.Localization.Locale> value){changed=null;Unsubscriptions++;}
  public static void CompleteLocale(string code)=>changed?.Invoke(new(code));
  public static UnityEngine.ResourceManagement.AsyncOperations.TestHandle InitializationOperation=new(),SelectedLocaleAsync=new();
  public static TestDatabase? StringDatabase=new();
 }
 public sealed class TestDatabase {public UnityEngine.ResourceManagement.AsyncOperations.TestHandle PreloadOperation=new();}
}
namespace UnityEngine.Localization {public sealed class Locale(string code){public LocaleIdentifier Identifier=new(code);}public record LocaleIdentifier(string Code);}
namespace Il2CppSystem {public sealed class Action<T>(System.Action<T> action){public void Invoke(T value)=>action(value);}}
namespace Il2CppInterop.Runtime {public static class DelegateSupport {public static T? ConvertDelegate<T>(Delegate value) where T:class=>new Il2CppSystem.Action<UnityEngine.Localization.Locale>((System.Action<UnityEngine.Localization.Locale>)value) as T;}}
namespace MelonLoader {public static class MelonLogger {public static void Error(string value){}}}

namespace Il2CppInterop.Runtime.InteropTypes.Arrays {
 public sealed class Il2CppStructArray<T>:IEnumerable<T> where T:struct {
  private readonly T[] data;public Il2CppStructArray(int length){data=new T[length];}
  public int Length=>data.Length;public T this[int index]{get=>data[index];set=>data[index]=value;}
  public IEnumerator<T> GetEnumerator()=>((IEnumerable<T>)data).GetEnumerator();
  System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()=>GetEnumerator();
 }
}
