namespace EZapi
{
    internal static class GameSession { internal static int Thread=Environment.CurrentManagedThreadId; internal static bool OnThread=>Environment.CurrentManagedThreadId==Thread; internal static GameRunIdentity? Run=new(2,"test",1); }
    internal static class GameBuildFingerprint { internal static bool IsKnown=true; }
    internal static class NativeAccess
    {
        internal static GameRunIdentity RequireRun()=>GameSession.OnThread && GameSession.Run is {} run?run:throw new InvalidOperationException();
        internal static bool SameRun(GameRunIdentity run)=>GameSession.OnThread && GameSession.Run==run;
        internal static Il2Cpp.GameItem RequireItem(Il2Cpp.GameItem item)=>item is not null && item.Pointer!=IntPtr.Zero && item.unitCount>0?item:throw new InvalidOperationException();
    }
}
namespace Il2Cpp
{
    public class GameItem { public IntPtr Pointer=new(2); public int uniqueId=4,unitCount=1; public string identifier="item",spriteAtlasPath="atlas",spritePath="key"; }
    public static class RenderHandler
    {
        public static UnityEngine.Sprite? Sprite; public static int Calls; public static Action? Hook;
        public static UnityEngine.Sprite? LoadFromAtlasRaw(string atlas,string key) {Calls++;Hook?.Invoke();return Sprite;}
    }
}
namespace UnityEngine
{
    public static class Boundary
    {
        public static HashSet<string> Failures=new(); public static List<string> Actions=new();
        public static void At(string action) {Actions.Add(action);if(Failures.Contains(action))throw new InvalidOperationException(action);}
    }
    public class Object
    {
        private static int Next=10; public IntPtr Pointer=new(++Next); public bool Destroyed;
        public static void Destroy(Object value) {Boundary.At("destroy");value.Destroyed=true;}
    }
    public struct Vector2 {public float x,y;public Vector2(float x,float y)=>(this.x,this.y)=(x,y);}
    public struct Rect {public float x,y,width,height;public Rect(float x,float y,float width,float height)=>(this.x,this.y,this.width,this.height)=(x,y,width,height);}
    public struct Color
    {
        public float r,g,b,a;public Color(float r,float g,float b,float a)=>(this.r,this.g,this.b,this.a)=(r,g,b,a);
        public Color gamma=>new(Encode(r),Encode(g),Encode(b),a);
        public static float Encode(float x)=>x<=0.0031308f?12.92f*x:1.055f*(float)Math.Pow(x,1/2.4)-0.055f;
        public static float Decode(float x)=>x<=0.04045f?x/12.92f:(float)Math.Pow((x+0.055f)/1.055f,2.4);
    }
    public struct Color32
    {
        public byte r,g,b,a; public Color32(byte r,byte g,byte b,byte a)=>(this.r,this.g,this.b,this.a)=(r,g,b,a);
        public static implicit operator Color32(Color c)=>new((byte)Math.Clamp((int)Math.Round(c.r*255),0,255),(byte)Math.Clamp((int)Math.Round(c.g*255),0,255),(byte)Math.Clamp((int)Math.Round(c.b*255),0,255),(byte)Math.Clamp((int)Math.Round(c.a*255),0,255));
        public static implicit operator Color(Color32 c)=>new(c.r/255f,c.g/255f,c.b/255f,c.a/255f);
    }
    public enum TextureFormat {RGBA32} public enum RenderTextureFormat {ARGB32} public enum RenderTextureReadWrite {sRGB,Linear}
    public enum FilterMode {Point,Bilinear} public enum SpritePackingMode {Tight,Rectangle} public enum ColorSpace {Gamma,Linear}
    public static class QualitySettings {public static ColorSpace activeColorSpace=ColorSpace.Gamma;}
    public static class SystemInfo {public static bool supportsRenderTextures=true;}
    public static class GL
    {
        private static bool Value;public static bool ExpectedRestore;
        public static bool sRGBWrite {get=>Value;set {Boundary.At(value==ExpectedRestore?"restore-srgb":"set-srgb");Value=value;}}
        public static void Reset(bool value) {Value=ExpectedRestore=value;}
    }
    public class Texture2D : Object
    {
        public int width,height;public bool isReadable=true,isDataSRGB=true;public Color32[] Pixels;public Action? OnRead;
        public bool ThrowRead,WrongCount;public Rect? LastRead;
        public Texture2D(int width,int height,TextureFormat format=TextureFormat.RGBA32,bool mip=false) {Boundary.At("create-texture");this.width=width;this.height=height;Pixels=new Color32[width*height];}
        public Color[] GetPixels(int x,int y,int w,int h)
        {
            Boundary.At("cpu-read");OnRead?.Invoke();LastRead=new(x,y,w,h);
            if(ThrowRead||!isReadable)throw new InvalidOperationException();
            if(WrongCount)return Array.Empty<Color>();
            var pixels=new Color[w*h];for(var py=0;py<h;py++)for(var px=0;px<w;px++)pixels[py*w+px]=Pixels[(y+py)*width+x+px];return pixels;
        }
        public void ReadPixels(Rect rect,int x,int y,bool recalc) {Boundary.At("read-pixels");Pixels=RenderTexture.active!.Pixels.ToArray();}
        public Color32[] GetPixels32() {Boundary.At("gpu-pixels");return Pixels;}
    }
    public class RenderTexture : Object
    {
        public int width,height;public Color32[] Pixels;public FilterMode filterMode;public bool Released;public RenderTextureReadWrite ReadWrite;
        public RenderTexture(int width,int height) {this.width=width;this.height=height;Pixels=new Color32[width*height];}
        public static RenderTexture? Previous,Last;private static RenderTexture? Active;
        public static RenderTexture? active {get=>Active;set {Boundary.At(ReferenceEquals(value,Previous)?"restore-target":"set-target");Active=value;}}
        public static void Reset(RenderTexture previous) {Previous=Active=previous;Last=null;}
        public static RenderTexture GetTemporary(int w,int h,int depth,RenderTextureFormat format,RenderTextureReadWrite mode) {Boundary.At("allocate");Last=new(w,h){ReadWrite=mode};return Last;}
        public static void ReleaseTemporary(RenderTexture texture) {Boundary.At("release");texture.Released=true;}
    }
    public static class Graphics
    {
        public static Action? Hook;
        public static void Blit(Texture2D source,RenderTexture target,Vector2 scale,Vector2 offset)
        {
            Boundary.At("blit");Hook?.Invoke();
            var x=(int)Math.Round(offset.x*source.width);var y=(int)Math.Round(offset.y*source.height);
            if(Math.Abs(scale.x*source.width-target.width)>0.01||Math.Abs(scale.y*source.height-target.height)>0.01)throw new Exception("crop was rescaled");
            for(var py=0;py<target.height;py++)for(var px=0;px<target.width;px++)
            {
                Color color=source.Pixels[(py+y)*source.width+px+x];
                if(QualitySettings.activeColorSpace==ColorSpace.Linear)
                {
                    if(source.isDataSRGB)color=new(Color.Decode(color.r),Color.Decode(color.g),Color.Decode(color.b),color.a);
                    if(GL.sRGBWrite && target.ReadWrite==RenderTextureReadWrite.sRGB)color=color.gamma;
                }
                target.Pixels[py*target.width+px]=color;
            }
        }
    }
    public class Sprite : Object
    {
        public Texture2D texture=null!;public Texture2D? associatedAlphaSplitTexture;public bool packed;public SpritePackingMode packingMode;
        public Vector2[] uv={new(0,0),new(1,0),new(1,1),new(0,1)};public ushort[] triangles={0,1,2,0,2,3};public Rect Rectangle;
        public Rect textureRect {get {Boundary.At("texture-rect");if(packed&&packingMode==SpritePackingMode.Tight)throw new Exception("tight rect unavailable");return Rectangle;}}
    }
}
