using System.Globalization;
using Il2Cpp;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Il2CppTMPro;
using MelonLoader;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Object = UnityEngine.Object;
using EventTrigger = UnityEngine.EventSystems.EventTrigger;

namespace EZapi;

/// <summary>Read-only adapter for the native paper panel. All generated objects and callbacks have window scope.</summary>
internal static class FactionReputationBrowser
{
    private static readonly NativeHookGroup Hooks = new("ezapi.presentation.faction-browser");
    private sealed class Part { internal readonly List<GameObject> Nodes=new(); internal readonly List<object> Delegates=new(); }
    private static readonly Part Shell=new(), Rows=new(), Marks=new();
    private static readonly List<Action> Undo=new();
    private static readonly List<(GameObject Object,bool Active)> Hidden=new();
    private static readonly Dictionary<string,string> NativeErrors=new(StringComparer.Ordinal);
    private static ReputationUIManager? _manager;
    private static RectTransform? _panel, _listContent;
    private static TMP_InputField? _search;
    private static ScrollRect? _listScroll;
    private static TextMeshProUGUI? _title, _relation, _amount, _known, _empty;
    private static RectTransform? _marker, _bar, _ruler, _hover, _effectTick;
    private static TextMeshProUGUI? _hoverTitle, _hoverDescription, _hoverFlavor;
    private static TextMeshProUGUI? _minimum, _maximum, _valueCaption;
    private static GameRunIdentity? _run;
    private static string? _selected, _fingerprint, _error, _hovered;
    private static FactionPerkView[] _group=Array.Empty<FactionPerkView>();
    private static int _effectIndex,_page,_pages=1;
    private static TextMeshProUGUI? _effectCount,_pageCount;
    private static RectTransform? _effectControls,_pageControls;
    private static string _query="", _locale="";
    private static bool _installed, _busy, _closing;
    private static float _next;
    private static long _generation;
    private static IntPtr _failedPanel;
    private static int _openFrame=-1, _bindFrame=-1;
    private static IReadOnlyList<FactionUiSnapshot> _current=Array.Empty<FactionUiSnapshot>();
    private static readonly Color Ink=new(.19f,.18f,.19f,1), Line=new(.34f,.35f,.31f,.65f), Selected=new(.35f,.40f,.33f,.32f);
    private static bool Alive(Object? v)=>v is not null&&v.Pointer!=IntPtr.Zero&&v;
    private static string L(string zh,string en)=>_locale.StartsWith("zh",StringComparison.OrdinalIgnoreCase)?zh:en;
    private static string N(double value)=>value.ToString("0.##",CultureInfo.InvariantCulture);
    private static string Text(LocalizedItemText? text)=>text?.ForLocale(_locale)??"";
    private static Color ColorOf(string hex)=>ColorUtility.TryParseHtmlString(hex,out var color)?color:Ink;
    internal static void RefreshSoon()=>_next=0;
    internal static object Report()=>new{hooks=Hooks.Snapshot(),visible=Alive(_panel)&&_panel!.gameObject.activeInHierarchy,
        factions=_current.Select(x=>new{id=x.Definition.Id,name=Text(x.Definition.Name),native=x.Native,value=x.State.Amount,perks=x.State.Perks.Count}).ToArray(),
        selected=_selected,query=_query,locale=_locale,providerErrors=FactionUiApi.Catalog.Errors.ToArray(),nativeErrors=NativeErrors.ToArray(),
        selectedPerks=_current.FirstOrDefault(x=>x.Definition.Id==_selected)?.State.Perks,
        displayOrder=FactionUiRules.OrderedPerks(_current.FirstOrDefault(x=>x.Definition.Id==_selected)?.State.Perks??Array.Empty<FactionPerkView>()).Select(p=>new{p.Id,p.Threshold}).ToArray(),
        markers=Marks.Nodes.Where(n=>Alive(n)&&n.name.StartsWith("EZapi.Faction.Marker.")).Select(n=>new{name=n.name,x=n.GetComponent<RectTransform>().anchoredPosition.x,icon=n.GetComponent<Image>()?.sprite?.name}).ToArray(),
        hover=new{visible=Alive(_hover)&&_hover!.gameObject.activeSelf,perk=_hovered,title=_hoverTitle?.text,description=_hoverDescription?.text},
        listContentHeight=Alive(_listContent)?_listContent!.rect.height:0,benefitScroll=false,groupSize=_group.Length,selectedEffectIndex=_effectIndex,textPages=_pages,
        listPosition=Alive(_listScroll)?_listScroll!.verticalNormalizedPosition:0,
        ownedObjects=Shell.Nodes.Count+Rows.Nodes.Count+Marks.Nodes.Count,retainedCallbacks=Shell.Delegates.Count+Rows.Delegates.Count+Marks.Delegates.Count,
        opening=new{openFrame=_openFrame,bindFrame=_bindFrame,sameFrame=_openFrame>=0&&_openFrame==_bindFrame},error=_error};

    internal static IReadOnlyList<FactionUiSnapshot> ReadCurrent()
    {
        if(!GameSession.TryResolve(null,out var store))return Array.Empty<FactionUiSnapshot>();
        NativeErrors.Clear();
        var run=GameSession.Identity!;var rows=new List<FactionUiSnapshot>();var ids=new HashSet<string>(StringComparer.Ordinal);
        var catalog=store.storeReputations;
        // Read the real rendered sprites; iconName is not an emoji recipe in the native catalog.
        var icons=new Dictionary<IntPtr,string>();var manager=ReputationUIManager.Instance;
        if(Alive(manager)&&Alive(manager.layout))foreach(var element in manager.layout.GetComponentsInChildren<PerkElement>(true))
            if(Alive(element)&&element.perk is not null&&Alive(element.image)&&Alive(element.image.sprite))icons[element.perk.Pointer]=IconName(element.image.sprite.name);
        if(catalog is not null)for(int i=0;i<Math.Min(512,catalog.Count);i++){
            var faction=catalog[i];if(faction is null||faction.Pointer==IntPtr.Zero||string.IsNullOrWhiteSpace(faction.factionId)||!ids.Add(faction.factionId))continue;
            try{
            string name=ReadNativeText(faction.factionId+".name",()=>StoreReputation.GetFactionNameByID(faction.factionId),faction.factionDisplay??faction.factionId);
            if(string.IsNullOrWhiteSpace(name)||name==faction.factionId)name=faction.factionDisplay??faction.factionId;
            string color=faction.factionColor2;
            if(string.IsNullOrWhiteSpace(color))color="#665867";else if(!color.StartsWith('#'))color="#"+color;
            var perks=new List<FactionPerkView>();var native=faction.perks;
            if(native is not null)for(int p=0;p<Math.Min(256,native.Count);p++){
                var perk=native[p];if(perk is null||perk.Pointer==IntPtr.Zero)continue;
                var key=faction.factionId+"."+perk.perkId;
                var title=ReadNativeText(key+".name",perk.GetLocalizedPerkName,perk.perkId);var description=ReadNativeText(key+".description",perk.GetLocalizedDescription,"");var flavor=ReadNativeText(key+".flavor",perk.GetLocalizedFlavorText,"");
                if(string.IsNullOrWhiteSpace(title))title=perk.perkId;
                perks.Add(new(perk.perkId,new(title,title),new(description,description),perk.perkThreshold,perk.isUnlocked){
                    Flavor=string.IsNullOrWhiteSpace(flavor)?null:new(flavor,flavor),AlwaysActive=perk.isAlwaysActive,Icon=icons.TryGetValue(perk.Pointer,out var icon)?icon:RelationIcon(perk.perkThreshold)});
            }
            double value=faction.GetReputationExact();if(!double.IsFinite(value))continue;
            var relationship=StoreReputation.GetReputationStatus(faction.GetReputation());
            var range=FactionUiRules.NativeRange(value,perks);
            rows.Add(new("game.native",new(faction.factionId,new(name,name)){Color=color,Order=i,Minimum=range.Minimum,Maximum=range.Maximum},
                new(value){Relationship=new(relationship,relationship),Perks=perks.AsReadOnly()},true));
            }catch(Exception e){NativeErrors[faction.factionId]=e.GetType().Name;}
        }
        var extras=FactionUiApi.Catalog.Read(new(run,GameLanguage.Current.LocaleCode),()=>GameSession.TryResolve(run.Epoch,out _)&&GameSession.Identity==run);
        if(!GameSession.TryResolve(run.Epoch,out _)||GameSession.Identity!=run)return Array.Empty<FactionUiSnapshot>();
        foreach(var row in extras)if(ids.Add(row.Definition.Id))rows.Add(row);
        return rows.AsReadOnly();
    }
    private static string ReadNativeText(string key,Func<string> read,string fallback)
    {try{return read()??fallback;}catch(Exception e){NativeErrors[key]=e.GetType().Name;return fallback;}}
    private static string IconName(string? icon)
    {
        var value=(icon??"").ToLowerInvariant();
        return value.Contains("star")?"star":value.Contains("skull")?"skull":value.Contains("angry")?"angry":value.Contains("sad")?"sad":value.Contains("happy")?"happy":"smile";
    }
    private static Sprite Icon(string name)=>name switch{
        "star"=>_manager!.spriteStar,"happy"=>_manager!.spriteHappy,"sad"=>_manager!.spriteSad,"angry"=>_manager!.spriteAngry,"skull"=>_manager!.spriteSkull,_=>_manager!.spriteSmile};
    internal static PresentationResult Open()
    {
        if(!GameSession.TryResolve(null,out _)||!Hooks.Ready)return new(PresentationStatus.Unavailable,"EZAPI_FACTION_NOT_READY");
        var manager=ReputationUIManager.Instance;if(!Alive(manager))return new(PresentationStatus.Unavailable,"EZAPI_FACTION_PANEL");
        if(!Alive(manager.uiPanel)||!manager.uiPanel.activeInHierarchy)manager.OpenUI();
        return Alive(_panel)?new(PresentationStatus.Opened):new(PresentationStatus.Unavailable,"EZAPI_FACTION_LAYOUT");
    }
    internal static void Maintain()
    {
        if(_busy||!InventoryRuntime.CatalogReady||!GameSession.OnThread||!GameBuildFingerprint.IsKnown)return;
        if(!_installed){_installed=true;Hooks.Install(typeof(FactionReputationBrowser),
            new(typeof(ReputationUIManager),"OpenUI",typeof(void),Type.EmptyTypes,Prefix:nameof(BeforeOpen),Postfix:nameof(AfterOpen)),
            new(typeof(ReputationUIManager),"CloseUI",typeof(void),Type.EmptyTypes,Prefix:nameof(BeforeClose),Postfix:nameof(AfterClose)));}
        if(!Hooks.Ready)return;
        RefreshWindow(null);
    }
    private static void BeforeOpen(ReputationUIManager __instance)
    {if(Hooks.Ready){Restore();_closing=false;_failedPanel=IntPtr.Zero;_openFrame=Time.frameCount;_bindFrame=-1;}}
    private static void AfterOpen(ReputationUIManager __instance)=>RefreshWindow(__instance);
    private static void BeforeClose(ReputationUIManager __instance){if(Hooks.Ready){_closing=true;DebugFactions.Clear();}}
    private static void AfterClose(ReputationUIManager __instance)
    {if(Hooks.Ready&&(!Alive(__instance.uiPanel)||!__instance.uiPanel.activeInHierarchy))Restore();}
    private static void RefreshWindow(ReputationUIManager? opened)
    {
        if(_busy||!Hooks.Ready||!GameSession.OnThread||!InventoryRuntime.CatalogReady||!GameBuildFingerprint.IsKnown)return;
        _busy=true;
        try{
            var manager=opened??ReputationUIManager.Instance;
            if(!GameSession.TryResolve(null,out _)||!Alive(manager)||!Alive(manager.uiPanel)||!manager.uiPanel.activeInHierarchy){Restore();return;}
            if(_closing||_failedPanel==manager.uiPanel.Pointer)return;
            if(!Alive(_panel)||_manager!.Pointer!=manager.Pointer||_run!=GameSession.Identity){Restore();Bind(manager);}
            Fit();
            if(Time.unscaledTime<_next)return;_next=Time.unscaledTime+.25f;
            foreach(var hidden in Hidden)if(Alive(hidden.Object))hidden.Object.SetActive(false);
            Refresh();
        }catch(Exception e){var pointer=Alive(_panel)?_panel!.gameObject.Pointer:IntPtr.Zero;_error=e.ToString();Restore();_failedPanel=pointer;MelonLogger.Warning("[EZapi] EZAPI_FACTION_BROWSER: restored original window: "+e);}
        finally{_busy=false;}
    }
    private static void Hide(GameObject go)
    {if(Alive(go)){Hidden.Add((go,go.activeSelf));go.SetActive(false);}}
    private static RectTransform Node(Part part,string name,Transform parent,float x,float y,float w,float h,Color? color=null)
    {
        var go=new GameObject("EZapi.Faction."+name,new Il2CppReferenceArray<Il2CppSystem.Type>(new[]{Il2CppType.Of<RectTransform>()}));part.Nodes.Add(go);
        var r=go.GetComponent<RectTransform>();r.SetParent(parent,false);Place(r,x,y,w,h);
        if(color.HasValue){var image=go.AddComponent<Image>();image.color=color.Value;image.raycastTarget=true;}return r;
    }
    private static void Place(RectTransform r,float x,float y,float w,float h)
    {r.anchorMin=r.anchorMax=r.pivot=new(0,1);r.anchoredPosition=new(x,-y);r.sizeDelta=new(w,h);r.localScale=Vector3.one;}
    private static TextMeshProUGUI Label(Part part,Transform parent,string name,string value,float x,float y,float w,float h,float size=24,Color? color=null,bool wrap=false)
    {
        var rect=Node(part,name,parent,x,y,w,h);var label=rect.gameObject.AddComponent<TextMeshProUGUI>();
        label.font=_manager!.perkTitle.font;label.fontSharedMaterial=_manager.perkTitle.fontSharedMaterial;
        label.text=value;label.richText=false;label.fontSize=size;label.enableAutoSizing=false;label.color=color??Ink;
        label.alignment=TextAlignmentOptions.MidlineLeft;label.enableWordWrapping=wrap;label.raycastTarget=false;
        label.overflowMode=wrap?TextOverflowModes.Overflow:TextOverflowModes.Ellipsis;
        // CJK font ascenders can exceed a nominal row. Ellipsis otherwise hides the whole title.
        if(!wrap)rect.sizeDelta=new(w,Math.Max(h,label.GetPreferredValues(string.IsNullOrEmpty(value)?"国Ag":value,w,float.PositiveInfinity).y+4));return label;
    }
    private static void Picture(Part part,Transform parent,string name,string icon,float x,float y,float size)
    {var rect=Node(part,name,parent,x,y,size,size);var image=rect.gameObject.AddComponent<Image>();image.sprite=Icon(icon);image.preserveAspect=true;image.raycastTarget=false;}
    private static Button Button(Part part,RectTransform rect,Action click)
    {
        var button=rect.gameObject.AddComponent<Button>();button.targetGraphic=rect.GetComponent<Image>();button.transition=Selectable.Transition.ColorTint;
        var nav=button.navigation;nav.mode=Navigation.Mode.None;button.navigation=nav;
        var generation=_generation;Action guarded=()=>{if(generation==_generation&&Alive(_panel)&&_panel!.gameObject.activeInHierarchy){click();_next=0;}};
        var native=DelegateSupport.ConvertDelegate<UnityAction>(guarded)!;part.Delegates.Add(guarded);part.Delegates.Add(native);button.onClick.AddListener(native);return button;
    }
    private static ScrollRect Scroll(Transform parent,string name,float x,float y,float w,float h,out RectTransform content)
    {
        var root=Node(Shell,name,parent,x,y,w,h);var viewport=Node(Shell,name+"Viewport",root,0,0,w-22,h,new Color(1,1,1,.025f));viewport.gameObject.AddComponent<RectMask2D>();
        content=Node(Shell,name+"Content",viewport,0,0,w-22,h);var scroll=root.gameObject.AddComponent<ScrollRect>();scroll.content=content;scroll.viewport=viewport;
        scroll.horizontal=false;scroll.vertical=true;scroll.movementType=ScrollRect.MovementType.Clamped;scroll.scrollSensitivity=48;
        var track=Node(Shell,name+"Track",root,w-16,0,12,h,new Color(.20f,.21f,.18f,.35f));
        var handle=Node(Shell,name+"Handle",track,0,0,0,0,new Color(.68f,.69f,.59f,1));handle.anchorMin=Vector2.zero;handle.anchorMax=Vector2.one;handle.offsetMin=handle.offsetMax=Vector2.zero;
        var bar=track.gameObject.AddComponent<Scrollbar>();bar.handleRect=handle;bar.targetGraphic=handle.GetComponent<Image>();bar.direction=Scrollbar.Direction.BottomToTop;
        scroll.verticalScrollbar=bar;scroll.verticalScrollbarVisibility=ScrollRect.ScrollbarVisibility.Permanent;return scroll;
    }
    private static void Bind(ReputationUIManager manager)
    {
        _manager=manager;_run=GameSession.Identity;_locale=GameLanguage.Current.LocaleCode;_panel=manager.uiPanel.GetComponent<RectTransform>();_error=null;
        var p=_panel;var min=p.anchorMin;var max=p.anchorMax;var pivot=p.pivot;var pos=p.anchoredPosition;var size=p.sizeDelta;var scale=p.localScale;
        Undo.Add(()=>{if(Alive(p)){p.anchorMin=min;p.anchorMax=max;p.pivot=pivot;p.anchoredPosition=pos;p.sizeDelta=size;p.localScale=scale;}});
        p.anchorMin=p.anchorMax=p.pivot=new(.5f,.5f);p.anchoredPosition=Vector2.zero;p.sizeDelta=new(1600,930);
        Hide(manager.layout);Hide(manager.reputationTooltip);
        var detail=manager.perkTitle.transform.parent;
        if(Alive(detail)&&detail.Pointer!=p.Pointer&&detail.GetComponentsInChildren<Button>(true).Length==0)Hide(detail.gameObject);
        var shell=Node(Shell,"Browser",p,0,0,1600,930);
        // Native paper Image, info button and close button remain on the original panel.
        Label(Shell,shell,"Heading",L("阵营声望","Faction reputation"),48,20,1300,66,44);
        Node(Shell,"HeaderLine",shell,48,100,1504,2,Line);Node(Shell,"Divider",shell,445,116,2,760,Line);
        Label(Shell,shell,"ListHeading",L("阵营","Factions"),48,116,360,40,30);
        var search=Node(Shell,"Search",shell,48,166,370,48,new Color(.50f,.52f,.44f,.25f));
        var text=Label(Shell,search,"SearchText","",12,0,346,48,23);var placeholder=Label(Shell,search,"SearchPlaceholder",L("筛选阵营 / ID…","Filter factions / ID…"),12,0,346,48,23,new Color(.30f,.32f,.29f,.8f));
        _search=search.gameObject.AddComponent<TMP_InputField>();_search.textViewport=search;_search.textComponent=text;_search.placeholder=placeholder;_search.characterLimit=160;
        _listScroll=Scroll(shell,"FactionList",48,234,370,578,out _listContent);
        _known=Label(Shell,shell,"Known","",48,834,370,36,23);
        _title=Label(Shell,shell,"FactionName","",488,118,1030,62,42);
        _relation=Label(Shell,shell,"Relationship","",488,192,580,46,27);
        _amount=Label(Shell,shell,"Amount","",1090,192,450,46,27);
        _minimum=Label(Shell,shell,"Minimum","",488,252,130,28,21);
        _maximum=Label(Shell,shell,"Maximum","",1420,252,120,28,21);_maximum.alignment=TextAlignmentOptions.MidlineRight;
        var ruler=_ruler=Node(Shell,"Ruler",shell,510,292,1000,4,Ink);
        for(int tick=0;tick<=20;tick++)Node(Shell,"Tick",ruler,tick*50,-(tick%5==0?12:6),3,tick%5==0?28:16,Ink);
        _bar=Node(Shell,"ValueBar",ruler,500,-1,0,6,new Color(.34f,.50f,.19f,1));
        _effectTick=Node(Shell,"EffectTick",ruler,0,-12,3,24,new Color(.55f,.30f,.18f,1));_effectTick.gameObject.SetActive(false);
        _marker=Node(Shell,"ValueMarker",ruler,498,-34,4,34,Ink);
        _valueCaption=Label(Shell,_marker,"ValueCaption","0",-66,-38,136,32,22);_valueCaption.alignment=TextAlignmentOptions.Center;
        Label(Shell,shell,"Hostile",L("敌对","Hostile"),488,348,200,36,24);
        var neutral=Label(Shell,shell,"Neutral",L("中立","Neutral"),910,348,200,36,24);neutral.alignment=TextAlignmentOptions.Midline;
        var friendly=Label(Shell,shell,"Friendly",L("友好","Friendly"),1328,348,200,36,24);friendly.alignment=TextAlignmentOptions.MidlineRight;
        Node(Shell,"DetailLine",shell,470,402,1075,2,Line);
        Label(Shell,shell,"PerkHeading",L("声望权益","Reputation benefits"),488,414,1000,44,30);
        _empty=Label(Shell,shell,"Empty","",512,510,980,240,26);_empty.alignment=TextAlignmentOptions.Center;
        _hover=Node(Shell,"Details",shell,488,470,1036,398,new Color(.45f,.47f,.40f,.12f));_hover.GetComponent<Image>().raycastTarget=false;
        Node(Shell,"DetailTop",_hover,0,0,1036,2,Line);
        _hoverTitle=Label(Shell,_hover,"HoverTitle","",24,14,988,84,26,wrap:true);
        _hoverDescription=Label(Shell,_hover,"HoverDescription","",24,108,988,164,24,wrap:true);
        _hoverFlavor=Label(Shell,_hover,"HoverFlavor","",24,286,988,84,22,new Color(.36f,.35f,.37f,1),true);
        foreach(var label in new[]{_hoverTitle,_hoverDescription,_hoverFlavor}){label.alignment=TextAlignmentOptions.TopLeft;label.overflowMode=TextOverflowModes.Page;}
        _hoverFlavor.fontStyle=FontStyles.Italic;
        _effectControls=Node(Shell,"EffectControls",shell,488,872,580,40);
        _effectCount=Label(Shell,_effectControls,"EffectCount","",110,0,360,40,21);_effectCount.alignment=TextAlignmentOptions.Center;
        DetailButton(_effectControls,"PreviousEffect","‹",0,()=>CycleEffect(-1));
        DetailButton(_effectControls,"NextEffect","›",480,()=>CycleEffect(1));
        _pageControls=Node(Shell,"PageControls",shell,1120,872,416,40);
        _pageCount=Label(Shell,_pageControls,"PageCount","",82,0,250,40,21);_pageCount.alignment=TextAlignmentOptions.Center;
        DetailButton(_pageControls,"PreviousText","‹",0,()=>CyclePage(-1));
        DetailButton(_pageControls,"NextText","›",336,()=>CyclePage(1));
        HideHover();
        // Keep the existing close/info controls above the non-raycasting shell.
        shell.SetAsLastSibling();
        PlaceHeaderControl("Panel/CloseButton",1508);
        PlaceHeaderControl("Panel/infoIcon",1444);
        _bindFrame=Time.frameCount;
        Refresh();Canvas.ForceUpdateCanvases();_listScroll.verticalNormalizedPosition=1;
        Fit();EZapiBootstrap.WriteDiagnostics();
        MelonLogger.Msg("[EZapi] Faction browser bound: "+_current.Count+" factions, opening frame="+_openFrame+", binding frame="+_bindFrame+".");
    }
    private static void PlaceHeaderControl(string path,float x)
    {
        var control=_panel!.Find(path);if(!Alive(control))return;
        var r=control.GetComponent<RectTransform>();if(!Alive(r))return;
        var parent=r.parent;int sibling=r.GetSiblingIndex();var min=r.anchorMin;var max=r.anchorMax;var pivot=r.pivot;var pos=r.anchoredPosition;var size=r.sizeDelta;var scale=r.localScale;
        Undo.Add(()=>{if(!Alive(r)||!Alive(parent))return;r.SetParent(parent,false);r.SetSiblingIndex(sibling);r.anchorMin=min;r.anchorMax=max;r.pivot=pivot;r.anchoredPosition=pos;r.sizeDelta=size;r.localScale=scale;});
        // Keep the native parent and listeners: CloseUI can reuse this hierarchy.
        // Reparenting its controls makes path discovery fail on the next open.
        r.anchorMin=r.anchorMax=r.pivot=new(1,1);r.anchoredPosition=new(x+44-1600,-22);r.sizeDelta=new(44,44);r.localScale=Vector3.one;
    }
    private static void Refresh()
    {
        var locale=GameLanguage.Current.LocaleCode;
        if(locale!=_locale){var manager=_manager!;Restore();Bind(manager);return;}
        _current=ReadCurrent();var query=_search!.text??"";
        var filtered=_current.Where(x=>FactionUiRules.Matches(x,query,_locale)).ToArray();
        if(!filtered.Any(x=>x.Definition.Id==_selected))_selected=filtered.FirstOrDefault()?.Definition.Id;
        var selected=filtered.FirstOrDefault(x=>x.Definition.Id==_selected);
        var fingerprint=System.Text.Json.JsonSerializer.Serialize(new{query,_selected,rows=filtered.Select(x=>new{x.Definition,x.State.Amount,x.State.Relationship}),selected});
        if(fingerprint==_fingerprint)return;
        bool filterChanged=query!=_query;_query=query;_fingerprint=fingerprint;
        float listPosition=filterChanged?1:_listScroll!.verticalNormalizedPosition;
        string? retainedEffect=_hovered;HideHover();Clear(Rows);Clear(Marks);
        for(int i=0;i<filtered.Length;i++){
            var row=filtered[i];bool chosen=row.Definition.Id==_selected;
            var rect=Node(Rows,"Row."+row.Definition.Id,_listContent!,0,i*92,348,86,chosen?Selected:new Color(1,1,1,.025f));
            if(chosen)Node(Rows,"Selected",rect,0,0,6,86,ColorOf(row.Definition.Color));
            Button(Rows,rect,()=>{_selected=row.Definition.Id;_fingerprint=null;});
            Picture(Rows,rect,"Icon",RelationIcon(row.State.Amount),14,25,32);
            Label(Rows,rect,"Name",Text(row.Definition.Name),60,7,174,38,25,ColorOf(row.Definition.Color));
            var number=Label(Rows,rect,"Value",N(row.State.Amount)+" / "+N(row.Definition.Maximum),230,8,112,32,20);number.alignment=TextAlignmentOptions.MidlineRight;
            var status=Label(Rows,rect,"Status",Relationship(row),60,44,280,30,20);status.alignment=TextAlignmentOptions.MidlineRight;
            Node(Rows,"RowLine",rect,0,85,348,1,Line);
        }
        _listContent!.sizeDelta=new(348,Math.Max(578,filtered.Length*92));
        _known!.text=L("已知阵营：","Known factions: ")+_current.Count+(filtered.Length==_current.Count?"":"  · "+filtered.Length);
        _marker!.gameObject.SetActive(selected is not null);_bar!.gameObject.SetActive(selected is not null);
        if(selected is null){_title!.text=L("没有匹配的阵营","No matching factions");_relation!.text=_amount!.text=_minimum!.text=_maximum!.text="";_empty!.text=L("修改筛选条件后重试","Change the filter to see factions");_empty.gameObject.SetActive(true);RestoreScroll(listPosition);return;}
        _title!.text=Text(selected.Definition.Name);_title.color=ColorOf(selected.Definition.Color);
        _relation!.text=L("当前关系：","Relationship: ")+Relationship(selected);
        _amount!.text=L("当前声望：","Reputation: ")+N(selected.State.Amount)+" / "+N(selected.Definition.Maximum);
        _minimum!.text=N(selected.Definition.Minimum);_maximum!.text="+"+N(selected.Definition.Maximum);
        float position=(float)FactionUiRules.Position(selected.State.Amount,selected.Definition.Minimum,selected.Definition.Maximum);
        float zero=(float)FactionUiRules.Position(0,selected.Definition.Minimum,selected.Definition.Maximum);
        _marker!.anchoredPosition=new(position*1000-2,34);_valueCaption!.text=N(selected.State.Amount);_bar!.anchoredPosition=new(Math.Min(position,zero)*1000,1);_bar.sizeDelta=new(Math.Abs(position-zero)*1000,6);
        _bar.GetComponent<Image>().color=selected.State.Amount<0?new(.65f,.16f,.17f,1):new(.34f,.50f,.19f,1);
        _empty!.gameObject.SetActive(selected.State.Perks.Count==0);_empty.text=L("该阵营尚无声望权益","This faction has no reputation benefits");
        _empty!.gameObject.SetActive(true);
        _empty.text=selected.State.Perks.Count==0?L("该阵营尚无声望权益","This faction has no reputation benefits"):L("将鼠标放到上方表情，查看对应权益与效果。","Hover over a face above to see its benefit and effects.");
        foreach(var group in FactionUiRules.Markers(selected.State.Perks,selected.Definition.Minimum,selected.Definition.Maximum)){
            var entries=group.Perks.ToArray();var perk=entries[0];float x=(float)FactionUiRules.Position(group.Threshold,selected.Definition.Minimum,selected.Definition.Maximum)*1000;
            var icon=PerkIcon(Marks,_ruler!,selected,perk,"Marker."+perk.Id,x-16,18,32,()=>ShowGroup(selected,entries,0));
            if(entries.Length>1)Label(Marks,icon,"Count","×"+entries.Length,21,8,54,30,17);
            Button(Marks,icon,()=>ShowGroup(selected,entries,_group==entries?(_effectIndex+1)%entries.Length:0));
            if(retainedEffect is not null){int index=Array.FindIndex(entries,p=>p.Id==retainedEffect);if(index>=0)ShowGroup(selected,entries,index);}
        }
        RestoreScroll(listPosition);
    }
    private static void RestoreScroll(float list)
    {Canvas.ForceUpdateCanvases();_listScroll!.StopMovement();_listScroll.verticalNormalizedPosition=list;}
    private static void DetailButton(Transform parent,string name,string caption,float x,Action action)
    {var rect=Node(Shell,name,parent,x,0,80,40,new Color(.4f,.43f,.37f,.25f));var label=Label(Shell,rect,name+"Text",caption,0,0,80,40,28);label.alignment=TextAlignmentOptions.Center;Button(Shell,rect,action);}
    private static void ShowGroup(FactionUiSnapshot faction,FactionPerkView[] group,int index)
    {_group=group;_effectIndex=index;_page=0;ShowHover(faction,group[index]);_effectControls!.gameObject.SetActive(group.Length>1);_effectCount!.text=L("此处权益 ","Benefits here ")+(index+1)+" / "+group.Length;
        _effectTick!.anchoredPosition=new((float)FactionUiRules.Position(group[index].Threshold,faction.Definition.Minimum,faction.Definition.Maximum)*1000,12);_effectTick.gameObject.SetActive(true);}
    private static void CycleEffect(int step)
    {var faction=_current.FirstOrDefault(f=>f.Definition.Id==_selected);if(faction is not null&&_group.Length>0)ShowGroup(faction,_group,(_effectIndex+step+_group.Length)%_group.Length);}
    private static void CyclePage(int step)
    {_page=(_page+step+_pages)%_pages;foreach(var label in new[]{_hoverTitle!,_hoverDescription!,_hoverFlavor!})label.pageToDisplay=Math.Min(_page+1,Math.Max(1,label.textInfo.pageCount));_pageCount!.text=L("说明 ","Text ")+(_page+1)+" / "+_pages;}
    private static string Relationship(FactionUiSnapshot row)=>Text(row.State.Relationship) is {Length:>0} text?text:
        row.State.Amount<-50?L("敌对","Hostile"):row.State.Amount>50?L("友好","Friendly"):L("中立","Neutral");
    private static string RelationIcon(double amount)=>amount<=-100?"skull":amount<-50?"angry":amount<0?"sad":amount>=100?"star":amount>50?"happy":"smile";
    private static StoreReputationPerk? NativePerk(FactionUiSnapshot faction,FactionPerkView perk)
    {
        if(!faction.Native||_run is null||!GameSession.TryResolve(_run.Epoch,out var store)||GameSession.Identity!=_run)return null;
        foreach(var f in store.storeReputations)if(f.factionId==faction.Definition.Id)
            foreach(var p in f.perks)if(p.perkId==perk.Id)return p;
        return null;
    }
    private static RectTransform PerkIcon(Part part,Transform parent,FactionUiSnapshot faction,FactionPerkView perk,string name,float x,float y,float size,Action show)
    {
        RectTransform rect;var native=NativePerk(faction,perk);
        if(native is not null&&Alive(_manager!.reputationPerkPrefab)){
            var go=Object.Instantiate(_manager.reputationPerkPrefab);part.Nodes.Add(go);go.name="EZapi.Faction."+name;go.transform.SetParent(parent,false);
            var element=go.GetComponent<PerkElement>();
            try{
                if(!Alive(element))throw new InvalidOperationException("Native reputation perk prefab has no PerkElement.");
                element.Init(native);rect=go.GetComponent<RectTransform>();Place(rect,x,y,size,size);go.SetActive(true);element.image.raycastTarget=true;
                Hover(part,rect,show,()=>{});return rect;
            }catch(Exception e){NativeErrors[faction.Definition.Id+"."+perk.Id+".icon"]=e.GetType().Name;go.SetActive(false);Object.Destroy(go);}
        }
        {
            rect=Node(part,name,parent,x,y,size,size);var graphic=rect.gameObject.AddComponent<Image>();graphic.sprite=Icon(perk.Icon);graphic.preserveAspect=true;graphic.raycastTarget=true;
        }
        Hover(part,rect,show,()=>{});return rect;
    }
    private static void Hover(Part part,RectTransform rect,Action enter,Action leave)
    {
        var trigger=rect.gameObject.AddComponent<EventTrigger>();var generation=_generation;
        foreach(var item in new[]{(EventTriggerType.PointerEnter,enter),(EventTriggerType.PointerExit,leave)}){
            var entry=new EventTrigger.Entry{eventID=item.Item1};Action<BaseEventData> action=_=>{if(generation==_generation&&Alive(_panel)&&_panel!.gameObject.activeInHierarchy)item.Item2();};
            var native=DelegateSupport.ConvertDelegate<UnityAction<BaseEventData>>(action)!;part.Delegates.Add(action);part.Delegates.Add(native);entry.callback.AddListener(native);trigger.triggers.Add(entry);
        }
    }
    private static void ShowHover(FactionUiSnapshot faction,FactionPerkView perk)
    {
        if(_run is null||!GameSession.TryResolve(_run.Epoch,out _)||GameSession.Identity!=_run)return;
        _hoverTitle!.richText=_hoverDescription!.richText=_hoverFlavor!.richText=faction.Native;
        var native=NativePerk(faction,perk);
        if(native is not null){try{
            // Preserve the native formatter and any other Mod's UpdateToolTipContent postfix.
            _manager!.UpdateToolTipContent(native);
            _hoverTitle!.text=_manager.perkTitle.text;_hoverDescription!.text=_manager.perkdescription.text;_hoverFlavor!.text=_manager.perkFlavorText.text;
            if(Alive(_manager.reputationTooltip))_manager.reputationTooltip.SetActive(false);
        }catch(Exception e){NativeErrors[faction.Definition.Id+"."+perk.Id+".tooltip"]=e.GetType().Name;native=null;}}
        if(native is null){
            _hoverTitle!.text=Text(perk.Name)+"  ["+(perk.Active?L("已激活","Active"):L("未激活","Inactive"))+"]  "+(perk.AlwaysActive?L("始终生效","Always active"):N(perk.Threshold));
            _hoverDescription!.text=Text(perk.Description);_hoverFlavor!.text=Text(perk.Flavor);
        }
        _hovered=perk.Id;_hover!.gameObject.SetActive(true);_empty!.gameObject.SetActive(false);
        _pages=1;
        foreach(var label in new[]{_hoverTitle!,_hoverDescription!,_hoverFlavor!}){label.pageToDisplay=1;label.ForceMeshUpdate(true,false);_pages=Math.Max(_pages,label.textInfo.pageCount);}
        _pageControls!.gameObject.SetActive(_pages>1);CyclePage(0);
    }
    private static void HideHover()
    {if(Alive(_hover))_hover!.gameObject.SetActive(false);if(Alive(_effectTick))_effectTick!.gameObject.SetActive(false);if(Alive(_effectControls))_effectControls!.gameObject.SetActive(false);if(Alive(_pageControls))_pageControls!.gameObject.SetActive(false);_hovered=null;_group=Array.Empty<FactionPerkView>();_page=0;_pages=1;}
    private static void Fit()
    {var canvas=_panel!.GetComponentInParent<Canvas>();if(!Alive(canvas))return;var rect=canvas.GetComponent<RectTransform>().rect;_panel.localScale=Vector3.one*Mathf.Clamp(Mathf.Min((rect.width-36)/1600,(rect.height-36)/930),.1f,1);}
    private static void Clear(Part part)
    {
        foreach(var go in part.Nodes)if(Alive(go)){foreach(var button in go.GetComponents<Button>())button.onClick=new Button.ButtonClickedEvent();foreach(var trigger in go.GetComponents<EventTrigger>())trigger.triggers.Clear();go.SetActive(false);Object.Destroy(go);}
        part.Nodes.Clear();part.Delegates.Clear();
    }
    private static void Restore()
    {
        _generation++;HideHover();Clear(Rows);Clear(Marks);Clear(Shell);
        foreach(var entry in Hidden)if(Alive(entry.Object))entry.Object.SetActive(entry.Active);Hidden.Clear();
        for(int i=Undo.Count-1;i>=0;i--)try{Undo[i]();}catch(Exception e){MelonLogger.Warning("[EZapi] EZAPI_FACTION_RESTORE: "+e.Message);}Undo.Clear();
        _manager=null;_panel=_listContent=_marker=_bar=_ruler=_hover=_effectTick=null;_search=null;_listScroll=null;_title=_relation=_amount=_known=_empty=_minimum=_maximum=_valueCaption=_hoverTitle=_hoverDescription=_hoverFlavor=null;
        _effectControls=_pageControls=null;_effectCount=_pageCount=null;
        _run=null;_selected=_fingerprint=null;_query="";_current=Array.Empty<FactionUiSnapshot>();_next=0;
    }
    internal static void SceneChanged(){DebugFactions.Clear();Restore();_failedPanel=IntPtr.Zero;_closing=false;}
    internal static void Stop(){SceneChanged();Hooks.Stop();_installed=false;}
}
