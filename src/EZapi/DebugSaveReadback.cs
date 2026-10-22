using System.Security.Cryptography;
using System.Text;
using MelonLoader;
using MelonLoader.Utils;
namespace EZapi;

/// <summary>显式按钮只读当前存档文件；不触发保存，不写存档，只导出托管检查结果。</summary>
internal static class DebugSaveReadback
{
    internal static object? LastReport{get;private set;}
    internal static void Clear()=>LastReport=null;
    internal static string Run()
    {
        if(!VisibleStoreInventory.IsReady||!GameSession.TryResolve(null,out _))return "请在测试档店铺中读取存档。";
        var checks=new List<object>();var details=new List<object>();int passed=0,failed=0;
        void Check(bool value,string name){checks.Add(new{name,passed=value});if(value)passed++;else failed++;}
        try {
            using(var result=SaveReadbackApi.ReadBytes(Encoding.UTF8.GetBytes("{12:{\"message\":\"a\\q\"},\"n\":1,\"n\":2}"))) {
                Check(result.Status==SaveReadStatus.Readable,"actual game host parses ES3 text extensions");
                Check(SaveReadbackApi.RequireProperty(result.Root,"12").GetProperty("message").GetString()=="a\\q","invalid escape preserved literally");
                Check(!SaveReadbackApi.TryGetUniqueProperty(result.Root,"n",out _,out _),"duplicate rejected by unique-property helper");
                Check(SaveReadbackApi.TryGetUniqueProperty(result.Root,"missing",out _,out var present)&&!present,"missing distinguished from duplicate");
                var cloned=result.Root.Clone();result.Dispose();Check(cloned.ValueKind==System.Text.Json.JsonValueKind.Object,"clone outlives result");
                try{var root=result.Root;Check(false,"disposed result rejected");}catch(InvalidOperationException){Check(true,"disposed result rejected");}
            }
            var run=NativeAccess.RequireRun();var filename="save_"+run.SaveSlotId+".es3";
            var file=Path.Combine(UnityEngine.Application.persistentDataPath,filename);
            var before=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(file)));
            using var read=SaveReadbackApi.ReadFile(file);
            Check(read.Status==SaveReadStatus.Readable,"current native save file readable");
            var state=SaveReadbackApi.RequireProperty(SaveReadbackApi.RequireProperty(read.Root,"playerStore"),"value");
            var slot=SaveReadbackApi.RequireProperty(state,"saveSlotId").GetInt32();var runId=SaveReadbackApi.RequireProperty(state,"runID").GetString();
            Check(slot==run.SaveSlotId&&runId==run.RunId,"readback slot and run match loaded native game");
            var after=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(file)));Check(before==after,"read-only save SHA unchanged");
            details.Add(new{file=filename,slotId=slot,runId,read.ByteCount,sha256=after,status=read.Status.ToString()});
        }catch(Exception e){Check(false,"execution: "+e);MelonLogger.Error("[EZapi] Save readback fixture: "+e);}
        LastReport=new{at=DateTimeOffset.Now,passed,failed,checks,details,scope="real installed DLL in game; text snapshot only, no game save or object restoration"};
        EZapiBootstrap.WriteDiagnostics();return $"存档回读：{passed}通过，{failed}失败。";
    }
}
