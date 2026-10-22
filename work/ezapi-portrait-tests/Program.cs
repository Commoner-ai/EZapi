using System.Reflection;
using System.Text.Json;
using EZapi;
using Il2Cpp;
using UnityEngine;

var checks = new List<string>();
void Check(bool valid,string name) { if(!valid)throw new Exception(name); checks.Add(name); }
bool Throws(Action action,string text)
{ try { action(); return false; } catch(Exception error) { return error.Message.Contains(text,StringComparison.Ordinal); } }
void Reset() { SpriteAssets.Clear(); SpriteDict.Instance = new(); LegacySpriteAdapter.NotReady=false; }
EmbeddedSpriteAtlas Atlas(string owner="author.mod",string key="portraits")
    => SpriteAssets.CreateEmbeddedAtlas(owner,key,Assembly.GetExecutingAssembly(),new[]{new SpriteDefinition("face","portrait.fixture.png",1,1)});
Reset();
var atlas=Atlas();
Check(Throws(()=>atlas.RegisterPortrait("face"),"NOT_INSTALLED"),"uninstalled atlas cannot register portrait");
Check(Throws(()=>atlas.RegisterPortrait("missing"),"PORTRAIT_KEY"),"unknown key rejected before native mutation");
Check(atlas.TryInstall(),"real atlas state machine installs with substituted native decoder");
SpriteDict.Instance=null;
Check(Throws(()=>atlas.RegisterPortrait("face"),"PORTRAIT_NOT_READY")&&!atlas.Failed,"missing native dictionary retryable without poisoning atlas");
SpriteDict.Instance=new(){spriteDictionary=null};
Check(Throws(()=>atlas.RegisterPortrait("face"),"PORTRAIT_NOT_READY"),"null dictionary not created or replaced");
SpriteDict.Instance=new();
var sprite=atlas.GetSprite("face")!;
var key=atlas.RegisterPortrait("face");
Check(key.Length==79&&key.StartsWith("ezapi.portrait.")&&key.All(c=>char.IsAsciiLetterOrDigit(c)||c=='.'),"fixed-length portrait key fits visitor token without paths");
Check(SpriteDict.Instance.GetSprite(key)?.Pointer==sprite.Pointer,"registration verifies native resolver returns same sprite");
Check(atlas.RegisterPortrait("face")==key&&SpriteDict.Instance.spriteDictionary!.Count==1,"same current portrait registration idempotent");
Check(PortraitSprites.Key("a.b","c","d")!=PortraitSprites.Key("a","b.c","d"),"identity components unambiguous");
Check(PortraitSprites.Key(new string('a',128),new string('b',128),new string('c',128)).Length==79,"long resource identity still has bounded portrait token");
Check(Task.Run(()=>Throws(()=>atlas.RegisterPortrait("face"),"THREAD")).Result,"portrait native access rejects worker thread");
var other=Atlas(key:"other"); other.TryInstall();
Check(other.RegisterPortrait("face")!=key,"separate atlas gets separate portrait key");
var registry=SpriteDict.Instance;var prior=registry.spriteDictionary!;
SpriteDict.Instance=new();
Check(atlas.RegisterPortrait("face")==key&&SpriteDict.Instance.GetSprite(key)?.Pointer==sprite.Pointer,"scene dictionary rebuild can register again without old scene cache");
var replacement=new Sprite();SpriteDict.Instance.spriteDictionary![key]=replacement;
Check(Throws(()=>atlas.RegisterPortrait("face"),"PORTRAIT_COLLISION")&&SpriteDict.Instance.spriteDictionary[key]==replacement,"foreign replacement never overwritten");
SpriteAssets.Clear();
Check(SpriteDict.Instance.spriteDictionary[key]==replacement,"clear preserves foreign replacement");
Check(prior[key]==sprite,"clear does not retain or traverse old scene dictionary");
Check(!atlas.Installed&&atlas.Failed&&!atlas.TryInstall(),"released atlas handle stays inert after shutdown");
Check(Throws(()=>atlas.RegisterPortrait("face"),"NOT_INSTALLED"),"released portrait handle rejected");

Reset();atlas=Atlas();atlas.TryInstall();sprite=atlas.GetSprite("face")!;key=PortraitSprites.Key(atlas.OwnerId,atlas.AtlasKey,"face");
SpriteDict.Instance!.spriteDictionary!.Add(key,sprite);
Check(Throws(()=>atlas.RegisterPortrait("face"),"PORTRAIT_COLLISION"),"preexisting key not adopted even with identical sprite");
SpriteAssets.Clear();
Check(SpriteDict.Instance.spriteDictionary[key]==sprite,"unowned identical sprite key not removed on clear");

Reset();atlas=Atlas();atlas.TryInstall();sprite=atlas.GetSprite("face")!;key=PortraitSprites.Key(atlas.OwnerId,atlas.AtlasKey,"face");
SpriteDict.Instance!.Resolver=_=>new Sprite();
Check(Throws(()=>atlas.RegisterPortrait("face"),"PORTRAIT_READBACK")&&!SpriteDict.Instance.spriteDictionary!.ContainsKey(key),"wrong resolver sprite rolls back only new mapping");
SpriteDict.Instance.Resolver=null;
Check(atlas.RegisterPortrait("face")==key,"resolver failure retry succeeds after rollback");
SpriteAssets.Clear();
Check(!SpriteDict.Instance.spriteDictionary!.ContainsKey(key),"clear removes owned current portrait mapping");

Reset();atlas=Atlas();atlas.TryInstall();key=PortraitSprites.Key(atlas.OwnerId,atlas.AtlasKey,"face");
var dictionary=SpriteDict.Instance!.spriteDictionary!;
dictionary.AfterAdd=()=>throw new Exception("after-add failure");
Check(Throws(()=>atlas.RegisterPortrait("face"),"after-add failure")&&!dictionary.ContainsKey(key),"side-effect then Add exception cleans only matching inserted sprite");

Reset();atlas=Atlas();atlas.TryInstall();key=PortraitSprites.Key(atlas.OwnerId,atlas.AtlasKey,"face");dictionary=SpriteDict.Instance!.spriteDictionary!;
dictionary.AfterAdd=()=>SpriteDict.Instance=new();
Check(Throws(()=>atlas.RegisterPortrait("face"),"PORTRAIT_READBACK")&&!dictionary.ContainsKey(key),"dictionary replacement during Add rejects and cleans old insertion");

Reset();atlas=Atlas();atlas.TryInstall();key=PortraitSprites.Key(atlas.OwnerId,atlas.AtlasKey,"face");dictionary=SpriteDict.Instance!.spriteDictionary!;
replacement=new Sprite();dictionary.AfterAdd=()=>dictionary[key]=replacement;
Check(Throws(()=>atlas.RegisterPortrait("face"),"PORTRAIT_READBACK")&&dictionary[key]==replacement,"failed readback preserves foreign replacement from reentry");

Reset();atlas=Atlas();atlas.TryInstall();key=PortraitSprites.Key(atlas.OwnerId,atlas.AtlasKey,"face");dictionary=SpriteDict.Instance!.spriteDictionary!;
SpriteDict.Instance.Resolver=_=>null;dictionary.ThrowRemove=true;
Check(Throws(()=>atlas.RegisterPortrait("face"),"PORTRAIT_READBACK")&&dictionary.ContainsKey(key),"cleanup failure preserves original failure and pending mapping");
dictionary.ThrowRemove=false;SpriteAssets.Clear();
Check(!dictionary.ContainsKey(key),"pending cleanup receipt retried on clear");

Reset();atlas=Atlas();atlas.TryInstall();key=atlas.RegisterPortrait("face");dictionary=SpriteDict.Instance!.spriteDictionary!;
SpriteDict.Instance.Resolver=_=>null;
Check(Throws(()=>atlas.RegisterPortrait("face"),"PORTRAIT_READBACK")&&dictionary.ContainsKey(key),"repeat registration resolver failure does not remove established mapping");
SpriteAssets.Clear();
Check(!dictionary.ContainsKey(key),"established mapping still cleaned after resolver failure");

Reset();atlas=Atlas();atlas.TryInstall();key=atlas.RegisterPortrait("face");
SpriteDict.Instance=null;SpriteAssets.Clear();
Check(!atlas.Installed&&atlas.Failed,"clear tolerates native scene already destroyed");
var report=new{passed=checks.Count,failed=0,checks,nativeRuntimeVerified=false,
    scope="real SpriteAssets/EmbeddedSpriteAtlas/PortraitSprites and resource parsing; native dictionary/resolver/Unity decode are stubs; no game launch or persistence"};
Directory.CreateDirectory("work");File.WriteAllText("work/ezapi-portrait-tests.json",JsonSerializer.Serialize(report,new JsonSerializerOptions{WriteIndented=true}));
Console.WriteLine(JsonSerializer.Serialize(report));
