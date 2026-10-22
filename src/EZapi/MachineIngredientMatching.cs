namespace EZapi;

/// <summary>
/// 纯数量分配：先有界枚举整堆组合，再用整数最大流分配可拆材料。
/// 同一堆不会被多条条件重复使用；宽条件不会贪心抢走严格条件的唯一材料。
/// 超8192搜索节点或8192增广步抛错，调用方必须整批拒绝，不能使用部分分配。
/// </summary>
internal static class MachineIngredientMatching
{
    internal static int[]? Allocate(int[] stock,int[] demand,bool[] whole,bool[,] eligible)
    {
        if(stock.Length>64||demand.Length>16||demand.Length!=whole.Length||eligible.GetLength(0)!=demand.Length||eligible.GetLength(1)!=stock.Length||
            stock.Any(x=>x is <1 or >1000000000)||demand.Any(x=>x is <1 or >1000000000))throw MachineRecipeRules.Invalid("invalid matching matrix");
        var steps=0;var edges=0;var used=new bool[stock.Length];var wholeTakes=new int[stock.Length];
        var exact=Enumerable.Range(0,demand.Length).Where(r=>whole[r]).OrderBy(r=>Enumerable.Range(0,stock.Length).Count(i=>eligible[r,i])).ToArray();
        void Budget(){if(++steps>8192)throw new InvalidOperationException("EZAPI_RECIPE_BUDGET: bounded material search stopped without writes.");}
        void EdgeBudget(){if(++edges>262144)throw new InvalidOperationException("EZAPI_RECIPE_BUDGET: bounded matching edge scan stopped without writes.");}
        int[]? Solve(int index) {
            Budget();if(index==exact.Length)return Flow();
            var row=exact[index];
            int[]? Pick(int from,int remaining) {
                Budget();if(remaining==0)return Solve(index+1);
                long available=0;for(var i=from;i<stock.Length;i++)if(!used[i]&&eligible[row,i]&&stock[i]<=remaining)available+=stock[i];
                if(available<remaining)return null;
                for(var i=from;i<stock.Length;i++)if(!used[i]&&eligible[row,i]&&stock[i]<=remaining) {
                    used[i]=true;wholeTakes[i]=stock[i];var answer=Pick(i+1,remaining-stock[i]);used[i]=false;wholeTakes[i]=0;
                    if(answer is not null)return answer;
                }
                return null;
            }
            return Pick(0,demand[row]);
        }
        int[]? Flow() {
            var rows=Enumerable.Range(0,demand.Length).Where(r=>!whole[r]).ToArray();
            var n=stock.Length+rows.Length+2;var sink=n-1;var residual=new long[n,n];
            for(var i=0;i<stock.Length;i++)if(!used[i])residual[0,i+1]=stock[i];
            for(var r=0;r<rows.Length;r++) {
                var node=stock.Length+1+r;residual[node,sink]=demand[rows[r]];
                for(var i=0;i<stock.Length;i++)if(!used[i]&&eligible[rows[r],i])residual[i+1,node]=stock[i];
            }
            long total=0;var required=rows.Sum(r=>(long)demand[r]);
            while(total<required) {
                Budget();var previous=Enumerable.Repeat(-1,n).ToArray();var q=new Queue<int>();q.Enqueue(0);previous[0]=0;
                while(q.Count>0&&previous[sink]<0) {var u=q.Dequeue();for(var v=1;v<n;v++){EdgeBudget();if(previous[v]<0&&residual[u,v]>0){previous[v]=u;q.Enqueue(v);}}}
                if(previous[sink]<0)return null;
                var delta=long.MaxValue;for(var v=sink;v!=0;v=previous[v])delta=Math.Min(delta,residual[previous[v],v]);
                for(var v=sink;v!=0;v=previous[v]){residual[previous[v],v]-=delta;residual[v,previous[v]]+=delta;}
                total+=delta;
            }
            return Enumerable.Range(0,stock.Length).Select(i=>checked(wholeTakes[i]+(int)residual[i+1,0])).ToArray();
        }
        return Solve(0);
    }
}
