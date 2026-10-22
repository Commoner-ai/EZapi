using System.Text;
namespace EZapi;
/// <summary>有界纯文本分页。不依赖IL2CPP滚动裁切；保留换行，按Unicode标量分割，不拆代理项。</summary>
internal static class RecipeGuideText
{
    internal static string[] Pages(string text,int columns,int lines)
    {
        if(columns<1||lines<1)throw new ArgumentOutOfRangeException(nameof(columns));
        var result=new List<string>();var page=new StringBuilder();int col=0,row=0;
        var runes=text.Replace("\r\n","\n").Replace('\r','\n').EnumerateRunes().ToArray();
        static bool Word(Rune r)=>r.Value is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9' or '\'';
        for(var index=0;index<runes.Length;index++) {
            var rune=runes[index];
            // 英文短词整体移到下一行；超长ID仍按标量分割，保持有界分页和CJK/emoji旧行为。
            if(col>0&&Word(rune)&&(index==0||!Word(runes[index-1]))) {
                var length=1;while(length<=columns&&index+length<runes.Length&&Word(runes[index+length]))length++;
                if(length<=columns&&col+length>columns){page.Append('\n');col=0;row++;}
            }
            if(rune.Value=='\n') {page.Append('\n');col=0;row++;}
            else {if(col>=columns){page.Append('\n');col=0;row++;}if(row>=lines){result.Add(page.ToString());page.Clear();row=0;}page.Append(rune.ToString());col++;}
            if(row>=lines){result.Add(page.ToString());page.Clear();row=0;}
        }
        if(page.Length>0||result.Count==0)result.Add(page.ToString());return result.ToArray();
    }
}
