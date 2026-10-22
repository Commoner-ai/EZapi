using UnityEngine;
namespace EZapi;

/// <summary>只读图鉴。缓存托管目录快照和自有缩略图；窗口关闭、场景变化/读档立即清掉身份、资源和输入。</summary>
internal static class RecipeGuideBrowser
{
    private static readonly DebugInputShield Shield=new();
    private static readonly List<(string Id,Rect Area,Action Click)> Buttons=new();
    private static RecipeGuideSnapshot _snapshot=new(0,Array.Empty<RecipeGuideEntry>());
    private static GameRunIdentity? _run;
    private static int _recipe,_inputPage,_textPage;
    private static string? _pressed;
    private static GUIStyle? _label,_title,_button,_panelStyle;
    private static string? _lastError;
    private static string _lastText="";
    private static string[] _textPages=Array.Empty<string>();
    private static int _columns,_lines;
    private static bool _reportPending;
    private static string _locale="en";
    private static RecipeGuideEntry? _resolved;
    private static RecipeGuideEntry? _resolvedSource;
    private static string? _resolvedLocale;
    private static RecipeGuideEntry? _renderedSource;
    private static string? _renderedLocale;
    private static int _renderedInputPage=-1;
    private static string L(string chinese,string english)=>_locale.StartsWith("zh",StringComparison.OrdinalIgnoreCase)?chinese:english;
    internal static bool Visible {get;private set;}
    private static Rect Panel=>new((Screen.width-Mathf.Min(1080,Screen.width-40))/2,30,Mathf.Min(1080,Screen.width-40),Mathf.Min(920,Screen.height-60));
    internal static bool Open()
    {
        if(WorkshopApi.IsVisible || !GameSession.TryResolve(null,out _) || !VisibleStoreInventory.IsReady || InventoryApi.IsItemDragActive)return false;
        RecipeGuideImages.Clear();_run=GameSession.Identity;_locale=NativeItemLocalization.CurrentLocaleCode;_snapshot=RecipeGuideApi.Snapshot();_recipe=_inputPage=_textPage=0;_lastText="";_lastError=null;_pressed=null;Visible=true;_reportPending=true;ResolveSelected();return true;
    }
    internal static void Close()
    {
        Visible=false;_run=null;_pressed=null;Buttons.Clear();_resolved=null;_resolvedSource=null;_resolvedLocale=null;_renderedSource=null;_renderedLocale=null;_renderedInputPage=-1;_snapshot=new(0,Array.Empty<RecipeGuideEntry>());_lastText="";_textPages=Array.Empty<string>();RecipeGuideImages.Clear();Shield.Stop();
    }
    internal static bool SelectRecipe(string id)
    {
        if(!Visible)return false;var index=_snapshot.Recipes.ToList().FindIndex(r=>r.RecipeId==id);if(index<0)return false;
        _recipe=index;_inputPage=_textPage=0;_lastText="";_pressed=null;_reportPending=true;ResolveSelected();return true;
    }
    internal static bool VerifyLocaleProbe(string locale,string machine,string output,string requirement,string quantity,string note)
        =>Visible&&_locale==locale&&_resolved?.MachineName==machine&&_resolved.Output.Name==output&&
        _resolved.Output.Requirement==requirement&&_resolved.Output.Quantity==quantity&&_resolved.Note==note&&
        _lastText.Contains(output,StringComparison.Ordinal)&&_lastText.Contains(requirement,StringComparison.Ordinal)&&
        _lastText.Contains(quantity,StringComparison.Ordinal)&&_lastText.Contains(note,StringComparison.Ordinal);
    internal static bool PointerOver()
    {
        if(!Visible||!Application.isFocused)return false;
        var mouse=Input.mousePosition;return Panel.Contains(new Vector2(mouse.x,Screen.height-mouse.y));
    }
    internal static void Update()
    {
        if(!Visible)return;
        if(_run is null || !GameSession.TryResolve(_run.Epoch,out _) || !VisibleStoreInventory.IsReady) {Close();return;}
        var locale=NativeItemLocalization.CurrentLocaleCode;
        if(locale!=_locale){_locale=locale;_lastText="";_textPage=0;_pressed=null;Buttons.Clear();_reportPending=true;}
        Shield.Update(new Rect(0,0,0,0),Panel,true);
        if(!Application.isFocused){_pressed=null;return;}
        if(RecipeGuideApi.Revision!=_snapshot.Revision){var latest=RecipeGuideApi.Snapshot();_snapshot=latest;_recipe=Mathf.Clamp(_recipe,0,Mathf.Max(0,latest.Recipes.Count-1));_inputPage=_textPage=0;_lastText="";_pressed=null;RecipeGuideImages.Clear();Buttons.Clear();_reportPending=true;}
        if(Input.GetKeyDown(KeyCode.Escape)){Close();return;}
        var mouse=Input.mousePosition;var point=new Vector2(mouse.x,Screen.height-mouse.y);
        if(Input.GetMouseButtonDown(0))_pressed=Buttons.FirstOrDefault(b=>b.Area.Contains(point)).Id;
        if(Input.GetMouseButtonUp(0)) {
            var pressed=_pressed;_pressed=null;var button=Buttons.FirstOrDefault(b=>b.Id==pressed&&b.Area.Contains(point));
            if(pressed is not null)button.Click?.Invoke();
        }
        if(Visible&&_snapshot.Recipes.Count>0) {
            ResolveSelected();
            var row=_snapshot.Recipes[_recipe];
            // 每帧最多解析一个当前页ID，不在OnGUI内执行工厂；分页和返回复用缓存。
            var next=row.Inputs.Skip(_inputPage*4).Take(4).Append(row.Output).Select(f=>f.ItemId).Distinct(StringComparer.Ordinal).FirstOrDefault(id=>RecipeGuideImages.Find(id) is null);
            if(next is not null)try{RecipeGuideImages.Warm(next);}
            catch(Exception error){_lastError=error.ToString();Close();MelonLoader.MelonLogger.Error("[EZapi] EZAPI_GUIDE_IMAGE_UPDATE: "+error);}
            // 每次打开/翻页只导出一份预热完成的诊断，不在正常渲染热路径写文件。
            if(next is null&&_reportPending&&ReferenceEquals(_renderedSource,_resolvedSource)&&_renderedLocale==_locale&&_renderedInputPage==_inputPage){_reportPending=false;EZapiBootstrap.WriteDiagnostics();}
        }
    }
    private static void ResolveSelected()
    {
        var source=Visible&&_snapshot.Recipes.Count>0?_snapshot.Recipes[_recipe]:null;
        if(ReferenceEquals(source,_resolvedSource)&&_resolvedLocale==_locale)return;
        _resolvedSource=source;_resolvedLocale=_locale;_resolved=source is null?null:RecipeGuideRules.ResolveLocale(source,_locale);
    }
    private static void Button(string id,Rect area,string label,Action click){GUI.Box(area,label,_button);Buttons.Add((id,area,()=>{click();_reportPending=true;}));}
    internal static void Draw()
    {
        Buttons.Clear();if(!Visible)return;
        _label??=new GUIStyle(GUI.skin.label){fontSize=17,wordWrap=true,richText=false};
        _title??=new GUIStyle(_label){fontSize=23,fontStyle=FontStyle.Bold};
        _button??=new GUIStyle(GUI.skin.button){fontSize=17,richText=false};
        var depth=GUI.depth;GUI.depth=-1000;
        try {
            var p=Panel;var x=p.x+22;var width=p.width-44;
            _panelStyle??=new GUIStyle(GUI.skin.box);_panelStyle.normal.background=Texture2D.whiteTexture;
            var color=GUI.color;
            try{GUI.color=new Color(0.13f,0.12f,0.17f,1);GUI.Box(p,"",_panelStyle);}finally{GUI.color=color;}
            GUI.Label(new(x,p.y+18,width-130,32),L("EZapi 配方图鉴","EZapi Recipe Guide"),_title);
            Button("close",new(p.xMax-132,p.y+16,110,34),L("关闭","Close"),Close);
            if(_snapshot.Recipes.Count==0){GUI.Label(new(x,p.y+80,width,80),L("尚未登记配方说明。内容作者可以通过 RecipeGuideApi 或 JsonRecipeGuide 注册。","No recipes registered. Authors can use RecipeGuideApi or JsonRecipeGuide."),_label);return;}
            var recipe=_resolved??RecipeGuideRules.ResolveLocale(_snapshot.Recipes[_recipe],_locale);
            GUI.Label(new(x,p.y+64,width,30),$"{recipe.MachineName}  ·  {recipe.CategoryId}  ·  {_recipe+1}/{_snapshot.Recipes.Count}",_title);
            GUI.Label(new(x,p.y+100,width,52),$"{recipe.RecipeId}\n{L("来源：","Source: ")}{recipe.OwnerId}",_label);
            Button("previous",new(x,p.y+158,140,32),L("上一配方","Previous recipe"),()=>{_recipe=(_recipe+_snapshot.Recipes.Count-1)%_snapshot.Recipes.Count;_inputPage=0;});
            Button("next",new(x+150,p.y+158,140,32),L("下一配方","Next recipe"),()=>{_recipe=(_recipe+1)%_snapshot.Recipes.Count;_inputPage=0;});
            const int inputPageSize=4;var pages=Math.Max(1,(recipe.Inputs.Count+inputPageSize-1)/inputPageSize);
            _inputPage=Mathf.Clamp(_inputPage,0,pages-1);
            if(pages>1) {
                Button("input-prev",new(x+320,p.y+158,145,32),L("上一组材料","Previous inputs"),()=>_inputPage=(_inputPage+pages-1)%pages);
                Button("input-next",new(x+475,p.y+158,145,32),L("下一组材料","Next inputs"),()=>_inputPage=(_inputPage+1)%pages);
            }
            var rows=recipe.Inputs.Skip(_inputPage*inputPageSize).Take(inputPageSize).ToArray();
            var figures=rows.Select((f,i)=>(Figure:f,Label:L("投入 ","Input ")+(_inputPage*inputPageSize+i+1))).Append((Figure:recipe.Output,Label:L("产出","Output"))).ToArray();
            var cardWidth=Math.Min(160,(width-20)/5);var gap=(width-cardWidth*figures.Length)/Math.Max(1,figures.Length-1);
            for(var i=0;i<figures.Length;i++) {
                var cardX=x+i*(cardWidth+gap);var f=figures[i];
                GUI.Label(new(cardX,p.y+203,cardWidth,26),f.Label,_label);
                var image=new Rect(cardX+(cardWidth-76)/2,p.y+232,76,76);
                if(!RecipeGuideImages.Draw(f.Figure.ItemId,image))GUI.Box(image,L("暂无图片","No image"));
                GUI.Label(new(cardX,p.y+312,cardWidth,49),f.Figure.Name??f.Figure.ItemId,_label);
            }
            var text=L($"投入（第 {_inputPage+1}/{pages} 组）\n",$"Inputs (page {_inputPage+1}/{pages})\n")+(recipe.Inputs.Count==0?L("无物品投入\n","No item inputs\n"):string.Join("\n\n",rows.Select(Figure)))+
                L("\n\n产出\n","\n\nOutput\n")+Figure(recipe.Output)+(recipe.Note is null?"":L("\n\n说明\n","\n\nNotes\n")+recipe.Note);
            var area=new Rect(x,p.y+374,width,p.height-400);
            var columns=Math.Max(8,(int)((area.width-20)/20));var lines=Math.Max(4,(int)(area.height/28)-1);
            if(text!=_lastText||columns!=_columns||lines!=_lines){_lastText=text;_columns=columns;_lines=lines;_textPages=RecipeGuideText.Pages(text,columns,lines);_textPage=0;}
            if(_textPages.Length>1) {
                Button("text-prev",new(x+640,p.y+158,140,32),L("上页文字","Previous text"),()=>_textPage=(_textPage+_textPages.Length-1)%_textPages.Length);
                Button("text-next",new(x+790,p.y+158,140,32),L($"下页 {_textPage+1}/{_textPages.Length}",$"Next {_textPage+1}/{_textPages.Length}"),()=>_textPage=(_textPage+1)%_textPages.Length);
            }
            GUI.Label(area,_textPages[_textPage],_label);
            _renderedSource=_resolvedSource;_renderedLocale=_locale;_renderedInputPage=_inputPage;
        }catch(Exception e){_lastError=e.ToString();Close();MelonLoader.MelonLogger.Error("[EZapi] EZAPI_GUIDE_RENDER: "+e);}
        finally{GUI.depth=depth;}
    }
    private static string Figure(RecipeGuideFigure f)=>$"{f.Name??f.ItemId} × {f.Quantity??f.Amount.ToString(System.Globalization.CultureInfo.InvariantCulture)}\nID：{f.ItemId}"+
        (f.Requirement is null?"":L("\n要求：","\nRequires: ")+f.Requirement);
    internal static object Report()=>new{visible=Visible,revision=_snapshot.Revision,selectedRecipe=Visible&&_snapshot.Recipes.Count>0?_snapshot.Recipes[_recipe].RecipeId:null,
        inputPage=_inputPage,textPage=_textPage,lastError=_lastError,locale=_locale,resolved=_resolved,renderedText=_lastText,title=L("EZapi 配方图鉴","EZapi Recipe Guide"),images=RecipeGuideImages.Report(),
        scope="read-only recipe guide; bounded temporary directory instances for preview loading in Update; no inventory writes or saving"};
}
