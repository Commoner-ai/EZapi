using System.Reflection;
namespace MelonLoader {public static class MelonLogger{public static List<string> Messages=new();public static void Msg(string s)=>Messages.Add(s);public static void Error(string s)=>Messages.Add(s);}}
namespace HarmonyLib {
    public static class Priority {public const int Last=0;}
    public class HarmonyMethod {public MethodInfo Method;public int priority;public HarmonyMethod(MethodInfo method){Method=method;}}
    public sealed record Patch(string Owner,HarmonyMethod? Prefix,HarmonyMethod? Postfix,HarmonyMethod? Finalizer);
    public class Patches {public IReadOnlyList<string> Owners;public Patches(IEnumerable<string> owners){Owners=owners.ToArray();}}
    public class Harmony {
        private string owner;public static bool FailPatch,FailUnpatch;public static string? FailTarget;
        public static Dictionary<MethodInfo,List<Patch>> Installed=new();
        public Harmony(string id){owner=id;}
        public void Patch(MethodInfo target,HarmonyMethod? prefix=null,HarmonyMethod? postfix=null,HarmonyMethod? finalizer=null){
            if(FailPatch||target.Name==FailTarget)throw new Exception("simulated patch failure");
            if(!Installed.TryGetValue(target,out var list))Installed[target]=list=new();list.Add(new(owner,prefix,postfix,finalizer));
        }
        public void UnpatchSelf(){if(FailUnpatch)throw new Exception("unpatch failed");foreach(var list in Installed.Values)list.RemoveAll(p=>p.Owner==owner);}
        public static Patches? GetPatchInfo(MethodInfo target)=>Installed.TryGetValue(target,out var list)?new Patches(list.Select(p=>p.Owner)):null;
    }
    public static class Sim {
        public static T Run<T>(MethodInfo method,object instance,object?[] args,Func<T> original){
            var result=default(T);var run=true;Exception? exception=null;
            var patches=Harmony.Installed.GetValueOrDefault(method)?.ToArray()??Array.Empty<Patch>();
            object? Invoke(HarmonyMethod callback){
                var parameters=callback.Method.GetParameters();var input=new object?[parameters.Length];
                for(var i=0;i<parameters.Length;i++)input[i]=parameters[i].Name switch {"__instance"=>instance,"__result"=>result,"__runOriginal"=>run,"__exception"=>exception,"__0"=>args[0],_=>throw new Exception("unknown patch param")};
                var returned=callback.Method.Invoke(null,input);
                for(var i=0;i<parameters.Length;i++)if(parameters[i].Name=="__result")result=(T)input[i]!;
                return returned;
            }
            foreach(var p in patches)if(p.Prefix is {} pre&&Invoke(pre) is false)run=false;
            try {
                if(run)result=original();
                foreach(var p in patches)if(p.Postfix is {} post)Invoke(post);
            }catch(Exception e){exception=e;}
            foreach(var p in patches)if(p.Finalizer is {} finalizer)exception=(Exception?)Invoke(finalizer);
            if(exception is not null)throw exception;
            return result!;
        }
    }
}
namespace Il2CppInterop.Common {
    public static class Il2CppInteropUtils {
        public static bool Missing;private static readonly Dictionary<MethodBase,int> Indices=new();
        public static FieldInfo? GetIl2CppMethodInfoPointerFieldForGeneratedMethod(MethodBase method){
            if(Missing||method.DeclaringType?.Namespace!="Il2Cpp")return null;
            if(!Indices.TryGetValue(method,out var i))Indices[method]=i=Indices.Count;
            var field=typeof(Fields).GetField("P"+i)!;field.SetValue(null,new IntPtr(i+1));return field;
        }
        #pragma warning disable CS0649 // Generated metadata fields are assigned through reflection by the stub.
        private class Fields {
            public static IntPtr P0,P1,P2,P3,P4,P5,P6,P7,P8,P9,P10,P11,P12,P13,P14,P15;
        }
        #pragma warning restore CS0649
    }
}
namespace Il2CppInterop.Runtime.Runtime {
    public struct Il2CppMethodInfo {}
    public sealed class FakeInfo {public IntPtr MethodPointer;}
    public static class UnityVersionHandler {
        public static bool Zero,Alias;
        public static unsafe FakeInfo Wrap(Il2CppMethodInfo* metadata)=>new(){MethodPointer=Zero?IntPtr.Zero:Alias?new IntPtr(99):new IntPtr((long)metadata+1000)};
    }
}
