using Il2Cpp;
using Il2CppInterop.Runtime;
namespace EZapi;

/// <summary>店铺与电话共用的自有原生图；不持有共享界面状态，调用方提供许可与引用根。</summary>
internal class NativeDialogueGraph
{
        internal readonly DialogueExecution Execution;
        internal readonly GameRunIdentity Identity;
        internal readonly long Manager;
        internal readonly DialoguePlan Plan;
        internal readonly List<Dialogue> Pages=new(),Nodes=new();
        internal readonly List<GameItem> Items=new();
        internal readonly List<Il2CppSystem.Action> Actions=new();
        internal readonly Dictionary<(long,int),DialogueEdge> Edges=new();
        private readonly HashSet<long> _tokens=new();
        internal bool ReleaseFault,InspectFault,Suspended;
        internal object? LastNativeState;
        internal NativeDialogueGraph(DialogueScript script,long generation,GameRunIdentity identity,string request,long manager,int count)
        {
            // A graph owns one immutable locale view. Reopening a retained script resolves
            // again; neither locale changes nor navigation may recreate business callbacks.
            var localized=DialogueContract.ResolveLocale(script,NativeItemLocalization.CurrentLocaleCode);
            Execution=new(localized,generation,identity.RunId,identity.SaveSlotId,request);
            Identity=identity;Manager=manager;Plan=new(localized,count);
        }
        private Dialogue Node(string speaker,string body)
        {
            var node=new Dialogue();Nodes.Add(node);_tokens.Add(node.Pointer.ToInt64());node.SetText(speaker,body);return node;
        }
        internal void Build(Action<long,int,DialogueEdge> confirm,Action<long,int,DialogueEdge>? leave=null,
            Action<long,int,DialogueEdge>? continueAction=null)
        {
            var definition=Execution.Script.Definition;
            foreach(var page in Plan.Pages)Pages.Add(Node(definition.Speaker,page.Body));
            foreach(var page in Pages)page.isMainDialog=true;
            for(var p=0;p<Plan.Pages.Count;p++)for(var i=0;i<Plan.Pages[p].Edges.Count;i++) {
                var edge=Plan.Pages[p].Edges[i];var source=Pages[p];var sourceToken=source.Pointer.ToInt64();var index=i;
                var playerLine=Node("PLAYER",edge.Label);if(edge.Target>=0)playerLine.SetNextDialogue(Pages[edge.Target]);
                var item=DirectoryMaster.Item("handnote",true)??throw new InvalidOperationException("EZAPI_DIALOGUE_TEMPLATE");Items.Add(item);
                if(item.parentInventory is not null)throw new InvalidOperationException("EZAPI_DIALOGUE_ATTACHED_ITEM");
                Il2CppSystem.Action? action=null;
                if(edge.Action==DialogueAction.Confirm) {
                    action=DelegateSupport.ConvertDelegate<Il2CppSystem.Action>((Action)(()=>confirm(sourceToken,index,edge)))??throw new InvalidOperationException("EZAPI_DIALOGUE_DELEGATE");Actions.Add(action);
                }else if(edge.Action==DialogueAction.Continue&&continueAction is not null) {
                    action=DelegateSupport.ConvertDelegate<Il2CppSystem.Action>((Action)(()=>continueAction(sourceToken,index,edge)))??throw new InvalidOperationException("EZAPI_DIALOGUE_DELEGATE");Actions.Add(action);
                }else if(edge.Action==DialogueAction.Leave&&leave is not null) {
                    action=DelegateSupport.ConvertDelegate<Il2CppSystem.Action>((Action)(()=>leave(sourceToken,index,edge)))??throw new InvalidOperationException("EZAPI_DIALOGUE_DELEGATE");Actions.Add(action);
                }
                Edges.Add((sourceToken,index),edge);source.AddChoice(item,edge.Label,"",playerLine,action,null);
            }
        }
        internal bool Owns(Dialogue? node)=>node is not null&&_tokens.Contains(node.Pointer.ToInt64());
        internal bool TryFindChoice(Dialogue source,string choiceId,out int index)
        {
            index=-1;
            var token=source.Pointer.ToInt64();
            foreach(var pair in Edges)
            {
                if(pair.Key.Item1!=token)continue;
                var edge=pair.Value;
                var choice=-1;
                if(edge.Action==DialogueAction.Confirm)choice=edge.Choice;
                else if(edge.Action==DialogueAction.Navigate&&edge.Target>=0)
                {
                    var confirms=Plan.Pages[edge.Target].Edges.Where(x=>x.Action==DialogueAction.Confirm).Take(2).ToArray();
                    if(confirms.Length==1)choice=confirms[0].Choice;
                }
                if(choice>=0&&choice<Execution.Script.Definition.Choices.Count&&
                    string.Equals(Execution.Script.Definition.Choices[choice].Id,choiceId,StringComparison.Ordinal))
                {index=pair.Key.Item2;return true;}
            }
            return false;
        }
        internal bool ReferencedBy(IEnumerable<Dialogue?> roots)
        {
            var queue=new Queue<Dialogue>();var visited=new HashSet<long>();
            void Add(Dialogue? node){if(node is not null)queue.Enqueue(node);}
            foreach(var root in roots)Add(root);
            while(queue.Count>0) {
                var node=queue.Dequeue();if(!visited.Add(node.Pointer.ToInt64()))continue;
                if(Owns(node))return true;
                if(visited.Count>4096)throw new InvalidOperationException("EZAPI_DIALOGUE_REFERENCE_LIMIT: retained without unsafe release.");
                Add(node.nextDialogue);
                if(node.choices is not null)for(int i=0;i<node.choices.Count;i++)Add(node.choices[i].Item2);
            }
            return false;
        }
        internal void Release()
        {
            // 只有原生管理器已脱离整图才拆链/委托；不清理任何共享管理器列表或外部节点。
            foreach(var node in Nodes){node.startAction=null;node.endAction=null;node.nextDialogue=null;node.choices?.Clear();}
            foreach(var item in Items) {
                if(item.parentInventory is not null)throw new InvalidOperationException("EZAPI_DIALOGUE_ATTACHED_ITEM: retained; not destroyed.");
                item.Destroy();
            }
            Items.Clear();Actions.Clear();Pages.Clear();Nodes.Clear();Edges.Clear();_tokens.Clear();
        }

}
