using Il2Cpp;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Il2CppTMPro;
using MelonLoader;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace EZapi;

/// <summary>
/// Presentation adapter for the live Wild window. Keeps native elements, events,
/// category roots and purchase controls. Never unlocks, pays, saves or writes a run.
/// All changes are journalled and rolled back before native close/open callbacks.
/// </summary>
internal static class WildNetworkBrowser
{
    private static readonly NativeHookGroup Hooks = new("ezapi.presentation.wild-browser");
    private static readonly List<Action> Undo = new();
    private static readonly List<GameObject> Owned = new();
    private static readonly List<object> Delegates = new();
    private static readonly Dictionary<IntPtr, Category> Categories = new();
    private static readonly Dictionary<IntPtr, Card> Cards = new();
    private static WildUIManager? _manager;
    private static RectTransform? _panel, _tabs;
    private static RectTransform? _tabTrack;
    private static ScrollRect? _tabScroll;
    private static TMP_InputField? _search;
    private static TextMeshProUGUI? _hint;
    private static GameRunIdentity? _run;
    private static IntPtr _failedPanel;
    private static string? _error;
    private static float _next;
    private static int _lastOpenFrame = -1, _lastBindFrame = -1;
    private static string? _lastBindTrigger;
    private static bool _installed, _busy;
    private static bool _stressPending, _stressActive;
    private static long _generation;
    private static IntPtr _selected;
    private static WildFilter _filter;
    private static readonly List<(Button Button, WildFilter Filter)> Filters = new();
    private static readonly Color Background = new(.15f,.12f,.17f,1), Accent = new(.92f,.36f,.14f,1), Ink = new(.87f,.80f,.64f,1);

    private sealed class Category
    {
        internal RectTransform Root = null!, Content = null!, Viewport = null!;
        internal TextMeshProUGUI? Heading;
        internal string Fallback = "";
        internal ScrollRect Scroll = null!;
        internal CanvasGroup Visibility = null!;
        internal Button Tab = null!;
        internal TextMeshProUGUI Label = null!;
        internal int Order, Count;
    }
    private sealed record Card(NetworkElement Element, Category Category, CanvasGroup Visibility);
    private static bool Alive(Object? v) => v is not null && v.Pointer != IntPtr.Zero && v;
    private static string L(string zh,string en) => GameLanguage.Current.LocaleCode.StartsWith("zh",StringComparison.OrdinalIgnoreCase)?zh:en;

    internal static object Report() => new { hooks=Hooks.Snapshot(), visible=Alive(_panel), categories=Categories.Values.OrderBy(c=>c.Order).Select(c=>new {label=Alive(c.Label)?c.Label.text:"",count=c.Count}).ToArray(),
        items=Cards.Count,stressFixture=_stressActive,verticalScroll=Categories.Values.Where(c=>Alive(c.Root)).Select(c=>new{selected=c.Root.Pointer==_selected,contentHeight=c.Content.rect.height,viewportHeight=c.Viewport.rect.height,position=c.Scroll.verticalNormalizedPosition,scrollbarVisible=Alive(c.Scroll.verticalScrollbar)&&c.Scroll.verticalScrollbar.gameObject.activeInHierarchy}).ToArray(),
        selectedCategory=_selected.ToString(),filter=_filter.ToString(),query=Alive(_search)?_search!.text:"",selectedId=Alive(_manager)?_manager!.currentSelectedId:null,
        opening=new{openFrame=_lastOpenFrame,bindFrame=_lastBindFrame,trigger=_lastBindTrigger,boundInOpenCall=_lastOpenFrame>=0&&_lastOpenFrame==_lastBindFrame&&_lastBindTrigger=="OpenUI.postfix"},error=_error };

    internal static void Maintain()
    {
        if (_busy || !InventoryRuntime.CatalogReady || !GameSession.OnThread || !GameBuildFingerprint.IsKnown) return;
        if (!_installed) {
            _installed=true;
            Hooks.Install(typeof(WildNetworkBrowser),
                new(typeof(WildUIManager),"OpenUI",typeof(void),new[]{typeof(bool)},Prefix:nameof(BeforeOpen),Postfix:nameof(AfterOpen)),
                new(typeof(WildUIManager),"CloseUI",typeof(void),Type.EmptyTypes,Prefix:nameof(BeforeClose)));
        }
        if (!Hooks.Ready) return;
        RefreshOpenWindow(WildUIManager.Instance,"Update");
    }
    private static void RefreshOpenWindow(WildUIManager manager,string trigger)
    {
        if (_busy || !Hooks.Ready || !InventoryRuntime.CatalogReady || !GameSession.OnThread || !GameBuildFingerprint.IsKnown) return;
        _busy=true;
        try {
            if (!GameSession.TryResolve(null,out _) || !Alive(manager) || !Alive(manager.panel) || !manager.panel.activeInHierarchy) { Restore(); return; }
            if (_failedPanel==manager.panel.Pointer) return;
            if (!Alive(_manager) || _manager!.Pointer!=manager.Pointer || !Alive(_panel) || _panel!.gameObject.Pointer!=manager.panel.Pointer || _run!=GameSession.Identity) {
                bool requested=_stressPending;Restore();_stressPending=requested;Bind(manager,trigger);
            }
            if(Time.unscaledTime<_next)return;
            _next=Time.unscaledTime+.35f;
            if(_stressPending){_stressPending=false;CreateStressFixture();}
            Discover(); Refresh(); Fit();
        } catch(Exception e) {
            var pointer=Alive(_panel)?_panel!.gameObject.Pointer:IntPtr.Zero;
            _error=e.ToString(); MelonLogger.Warning("[EZapi] EZAPI_WILD_BROWSER: restored native window: "+e);
            Restore();_failedPanel=pointer;
        } finally {_busy=false;}
    }
    private static void BeforeOpen(WildUIManager __instance) { if(Hooks.Ready){Restore();_failedPanel=IntPtr.Zero;_lastOpenFrame=Time.frameCount;_lastBindFrame=-1;_lastBindTrigger=null;} }
    // Run after native initialization and normal Mod postfixes (Priority.Last in
    // NativeHookGroup), inside OpenUI's call stack and before its first render.
    // Waiting for MelonLoader.OnUpdate lets the restored vanilla window render
    // first when OpenUI runs later in the frame. Keep polling for late Mod rows.
    private static void AfterOpen(WildUIManager __instance) => RefreshOpenWindow(__instance,"OpenUI.postfix");
    private static void BeforeClose(WildUIManager __instance) { if(Hooks.Ready)Restore(); }
    internal static void SceneChanged() { Restore();_failedPanel=IntPtr.Zero; }
    internal static void Stop() { SceneChanged();Hooks.Stop();_installed=false; }

    // Explicit, disposable developer probe: UI duplicates only, never add an
    // upgrade to PlayerStore/networkElements or request a save/purchase.
    internal static string DebugStress()
    {
        if(!DeveloperToolsSettings.Current.Enabled||!GameSession.TryResolve(null,out _)||!Hooks.Ready)return "人脉界面尚未就绪";
        var manager=WildUIManager.Instance;if(!Alive(manager))return "人脉界面不存在";
        // Reuse an already open native window; repeated OpenUI has side effects
        // owned by other Mods and is unnecessary for a presentation-only probe.
        if(!Alive(manager.panel)||!manager.panel.activeInHierarchy)manager.OpenUI(false);
        _stressPending=true;_next=0;return "已创建临时人脉压力检查；关闭窗口清除";
    }
    private static void CreateStressFixture()
    {
        if(_stressActive||Cards.Count==0)return;
        var sample=Cards.Values.First();
        // A category unknown to EZapi is added *after* the browser has bound.
        var root=Node("ProbeCategory",_panel!,500,950,944,180);
        var heading=Label(Node("ProbeHeading",_panel!,500,893,944,36),L("扩展分类测试","Extension category probe"));
        // Heading discovery uses siblings just as the game does.
        heading.transform.SetParent(_panel,false);Place(heading.rectTransform,500,893,944,36);
        for(int i=0;i<84;i++){
            var clone=Object.Instantiate(sample.Element.gameObject,root,false);Owned.Add(clone);clone.name="EZapi.Wild.Probe."+i;clone.SetActive(true);
        }
        _stressActive=true;Discover();_selected=root.Pointer;Refresh();EZapiBootstrap.WriteDiagnostics();
        MelonLogger.Msg("[EZapi] Wild browser disposable probe: 84 native UI duplicates, one new category; no run data changes.");
    }

    // Snapshot each native rect exactly once per binding; reverse order restores
    // children before temporary parents are destroyed. Never resurrect a deleted GO.
    private static void Remember(RectTransform r)
    {
        var parent=r.parent;int sibling=r.GetSiblingIndex();var min=r.anchorMin;var max=r.anchorMax;var pivot=r.pivot;
        var size=r.sizeDelta;var pos=r.anchoredPosition3D;var scale=r.localScale;var rotation=r.localRotation;
        Undo.Add(()=>{if(!Alive(r)||!Alive(parent))return;r.SetParent(parent,false);r.SetSiblingIndex(sibling);r.anchorMin=min;r.anchorMax=max;r.pivot=pivot;r.sizeDelta=size;r.anchoredPosition3D=pos;r.localScale=scale;r.localRotation=rotation;});
    }
    private static CanvasGroup Visibility(GameObject go)
    {
        var group=go.GetComponent<CanvasGroup>();
        if(!Alive(group)){group=go.AddComponent<CanvasGroup>();var added=group;Undo.Add(()=>{if(Alive(added)){added.alpha=1;added.interactable=added.blocksRaycasts=true;Object.Destroy(added);}});}
        else {float a=group.alpha;bool i=group.interactable,b=group.blocksRaycasts;var existing=group;Undo.Add(()=>{if(Alive(existing)){existing.alpha=a;existing.interactable=i;existing.blocksRaycasts=b;}});}
        return group;
    }
    private static void Show(CanvasGroup group,bool show) {group.alpha=show?1:0;group.interactable=group.blocksRaycasts=show;}
    private static void Disable(Behaviour component)
    {bool enabled=component.enabled;Undo.Add(()=>{if(Alive(component))component.enabled=enabled;});component.enabled=false;}
    private static void Place(RectTransform r,float x,float y,float w,float h)
    {r.anchorMin=r.anchorMax=r.pivot=new Vector2(0,1);r.anchoredPosition=new Vector2(x,-y);r.sizeDelta=new Vector2(w,h);r.localScale=Vector3.one;}
    private static RectTransform Node(string name,Transform parent,float x,float y,float w,float h,Color? color=null)
    {
        var go=new GameObject("EZapi.Wild."+name,new Il2CppReferenceArray<Il2CppSystem.Type>(new[]{Il2CppType.Of<RectTransform>()}));Owned.Add(go);
        var r=go.GetComponent<RectTransform>();r.SetParent(parent,false);Place(r,x,y,w,h);
        if(color.HasValue){var image=go.AddComponent<Image>();image.color=color.Value;image.raycastTarget=true;}
        return r;
    }
    private static TextMeshProUGUI Label(Transform parent,string value,float size=22)
    {
        var r=Node("Label",parent,6,0,100,36);r.anchorMax=new Vector2(1,1);r.sizeDelta=new Vector2(-12,0);r.anchorMin=Vector2.zero;r.pivot=new Vector2(.5f,.5f);r.anchoredPosition=Vector2.zero;
        var text=r.gameObject.AddComponent<TextMeshProUGUI>();text.font=_manager!.title.font;text.fontSharedMaterial=_manager.title.fontSharedMaterial;text.fontSize=size;text.color=Ink;
        text.text=value;text.alignment=TextAlignmentOptions.Midline;text.raycastTarget=false;text.enableWordWrapping=false;text.overflowMode=TextOverflowModes.Ellipsis;return text;
    }
    private static Button Button(string name,Transform parent,float x,float y,float w,float h,string label,Action action,out TextMeshProUGUI text)
    {
        var r=Node(name,parent,x,y,w,h,Background);var button=r.gameObject.AddComponent<Button>();button.targetGraphic=r.GetComponent<Image>();
        button.transition=Selectable.Transition.ColorTint;
        var navigation=button.navigation;navigation.mode=Navigation.Mode.None;button.navigation=navigation;
        var generation=_generation;Action guarded=()=>{if(generation==_generation&&Alive(_panel)&&_panel!.gameObject.activeInHierarchy){action();_next=0;}};
        var native=DelegateSupport.ConvertDelegate<UnityAction>(guarded)??throw new InvalidOperationException("native UI delegate unavailable");Delegates.Add(guarded);Delegates.Add(native);button.onClick.AddListener(native);text=Label(r,label);return button;
    }
    private static ScrollRect Scroller(RectTransform root,RectTransform viewport,RectTransform content,bool horizontal=false)
    {
        viewport.gameObject.AddComponent<RectMask2D>();var scroll=root.gameObject.AddComponent<ScrollRect>();
        // This component is on an owned root, never on a native row.
        scroll.viewport=viewport;scroll.content=content;scroll.horizontal=horizontal;scroll.vertical=!horizontal;scroll.movementType=ScrollRect.MovementType.Clamped;scroll.scrollSensitivity=42;
        return scroll;
    }
    private static void AddScrollbar(RectTransform root,ScrollRect scroll,float x,float y,float height)
    {
        var track=Node("ScrollTrack",root,x,y,12,height,new Color(.08f,.07f,.09f,1));
        var handle=Node("Handle",track,0,0,0,0,Accent);handle.anchorMin=Vector2.zero;handle.anchorMax=Vector2.one;handle.offsetMin=handle.offsetMax=Vector2.zero;var bar=track.gameObject.AddComponent<Scrollbar>();bar.handleRect=handle;bar.targetGraphic=handle.GetComponent<Image>();bar.direction=Scrollbar.Direction.BottomToTop;
        // Keep a visible vertical track even when all cards fit on one page.
        // ScrollRect expands the handle to full height in that case.
        scroll.verticalScrollbar=bar;scroll.verticalScrollbarVisibility=ScrollRect.ScrollbarVisibility.Permanent;
    }
    private static void Bind(WildUIManager manager,string trigger)
    {
        _manager=manager;_run=GameSession.Identity;_panel=manager.panel.GetComponent<RectTransform>();Remember(_panel);_error=null;
        _panel.sizeDelta=new Vector2(1480,900);_panel.anchorMin=_panel.anchorMax=_panel.pivot=new Vector2(.5f,.5f);_panel.anchoredPosition=Vector2.zero;
        // Original title, balances and detail controls keep their localization,
        // tooltip, OnClick and native state writers.
        var detail=manager.title.transform.parent.GetComponent<RectTransform>();Remember(detail);Place(detail,30,148,440,710);
        void Move(RectTransform r,float x,float y,float w,float h){Remember(r);Place(r,x,y,w,h);}
        Move(manager.title.rectTransform,20,18,400,68);Move(manager.subtitle.rectTransform,28,91,384,32);
        Move(manager.description.rectTransform,30,148,380,405);
        Move(manager.cooldown.rectTransform,24,563,392,38);Move(manager.cost.rectTransform,24,604,392,38);
        Move(manager.unlockButton.GetComponent<RectTransform>(),120,652,200,34);Move(manager.alreadyMessageTMP.rectTransform,24,651,392,40);
        foreach(var child in detail.GetComponentsInChildren<Image>(true))if(child.transform.Pointer!=detail.Pointer&& !Alive(child.GetComponent<Button>()) && child.rectTransform.rect.height>350)Disable(child);
        // The heading is the direct-panel TMP above the detail (no fixed name).
        foreach(var text in manager.panel.GetComponentsInChildren<TextMeshProUGUI>(true))
            if(text.transform.parent.Pointer==_panel.Pointer && text.rectTransform.anchoredPosition.y>430)Move(text.rectTransform,30,18,1120,64);
        var credit=manager.creditCount.transform.parent.GetComponent<RectTransform>();var favor=manager.wildFavor.transform.parent.GetComponent<RectTransform>();
        Move(credit,30,94,300,38);if(favor.Pointer!=credit.Pointer)Move(favor,350,94,300,38);
        var shell=Node("Browser",_panel,490,148,960,710,Background);
        var tabRoot=Node("Tabs",shell,8,0,944,57,new Color(.1f,.08f,.12f,1));var tabViewport=Node("Viewport",tabRoot,0,0,944,49);
        _tabs=Node("TabContent",tabViewport,0,0,944,49);_tabScroll=Scroller(tabRoot,tabViewport,_tabs,true);
        // A horizontal scrollbar keeps arbitrarily many categories reachable.
        var track=Node("TabTrack",tabRoot,0,51,944,6,new Color(.08f,.07f,.09f,1));var handle=Node("TabHandle",track,0,0,0,0,Accent);handle.anchorMin=Vector2.zero;handle.anchorMax=Vector2.one;handle.offsetMin=handle.offsetMax=Vector2.zero;_tabTrack=track;
        var bar=track.gameObject.AddComponent<Scrollbar>();bar.handleRect=handle;bar.targetGraphic=handle.GetComponent<Image>();bar.direction=Scrollbar.Direction.LeftToRight;_tabScroll.horizontalScrollbar=bar;
        _tabScroll.horizontalScrollbarVisibility=ScrollRect.ScrollbarVisibility.AutoHide;
        foreach(var f in new[]{WildFilter.All,WildFilter.Purchased,WildFilter.Affordable}) {
            var choice=f;var button=Button("Filter",shell,8+(int)f*122,70,114,40,FilterName(f),()=>_filter=choice,out _);Filters.Add((button,f));
        }
        var input=Node("Search",shell,392,70,548,40,new Color(.10f,.09f,.12f,1));
        var inputViewport=Node("TextViewport",input,10,0,528,40);inputViewport.gameObject.AddComponent<RectMask2D>();
        var inputText=Label(inputViewport,"");inputText.alignment=TextAlignmentOptions.MidlineLeft;
        var placeholder=Label(inputViewport,L("搜索名称 / ID…","Search name / ID…"));placeholder.alignment=TextAlignmentOptions.MidlineLeft;placeholder.color=new Color(.56f,.52f,.51f,1);
        _search=input.gameObject.AddComponent<TMP_InputField>();_search.textViewport=inputViewport;_search.textComponent=inputText;_search.placeholder=placeholder;_search.characterLimit=160;
        _hint=Label(Node("Hint",_panel,495,862,950,26),"");_hint.fontSize=18;_hint.alignment=TextAlignmentOptions.MidlineRight;
        Discover();Refresh();Fit();_lastBindFrame=Time.frameCount;_lastBindTrigger=trigger;
        EZapiBootstrap.WriteDiagnostics();MelonLogger.Msg("[EZapi] Wild browser bound: "+Categories.Count+" live categories, "+Cards.Count+" native entries; trigger="+trigger+", frame="+_lastBindFrame+".");
    }
    private static string FilterName(WildFilter f)=>f switch {WildFilter.Purchased=>L("已购买","Purchased"),WildFilter.Affordable=>L("可负担","Affordable"),_=>L("全部","All")};

    private static void Discover()
    {
        if(!Alive(_panel))return;
        // Scan only the open window, including hidden categories. Do not activate
        // inactive templates, or synthesize entries from an upgrade dictionary.
        foreach(var element in _panel!.GetComponentsInChildren<NetworkElement>(true)) {
            if(!Alive(element)||!element.gameObject.activeSelf||Cards.ContainsKey(element.Pointer)||string.IsNullOrWhiteSpace(element.id))continue;
            var nativeParent=element.transform.parent.GetComponent<RectTransform>();
            if(!Alive(nativeParent))continue;
            var category=Categories.Values.FirstOrDefault(c=>nativeParent.Pointer==c.Content.Pointer || nativeParent.Pointer==c.Root.Pointer);
            if(category is null) {
                if(nativeParent.Pointer==_panel.Pointer) {
                    category=Categories.Values.FirstOrDefault(c=>c.Fallback=="__unparented");
                    if(category is null){var extra=Node("OtherCategory",_panel,0,0,944,568);category=CreateCategory(extra,element);category.Heading=null;category.Fallback="__unparented";Categories.Add(extra.Pointer,category);}
                } else {category=CreateCategory(nativeParent,element);Categories.Add(nativeParent.Pointer,category);}
            }
            var rect=element.GetComponent<RectTransform>();Remember(rect);rect.SetParent(category.Content,false);
            Cards.Add(element.Pointer,new(element,category,Visibility(element.gameObject)));
        }
        foreach(var dead in Cards.Where(e=>!Alive(e.Value.Element)||!e.Value.Element.transform.IsChildOf(_panel)).Select(e=>e.Key).ToArray())Cards.Remove(dead);
        foreach(var dead in Categories.Where(e=>!Alive(e.Value.Root)).Select(e=>e.Key).ToArray()) {
            var c=Categories[dead];if(Alive(c.Tab))c.Tab.gameObject.SetActive(false);Categories.Remove(dead);
        }
        if(!Categories.ContainsKey(_selected))_selected=Categories.Keys.FirstOrDefault();
    }
    private static Category CreateCategory(RectTransform root,NetworkElement sample)
    {
        // Match the native row's nearby heading geometrically. This also works
        // for new mod rows; names and category counts are deliberately unknown.
        var y=_panel!.InverseTransformPoint(root.position).y;
        var heading=_panel.GetComponentsInChildren<TextMeshProUGUI>(true).Where(t=>t.transform.parent.Pointer==root.parent.Pointer&&t.gameObject.activeSelf)
            .Where(t=>{float dy=_panel.InverseTransformPoint(t.transform.position).y-y;return dy>0&&dy<100&&t.rectTransform.rect.width>200;})
            .OrderBy(t=>Mathf.Abs((_panel.InverseTransformPoint(t.transform.position).y-y)-57)).FirstOrDefault();
        string fallback="";try{fallback=NetworkUpgrade.GetUpgradeById(sample.id)?.GetLocalizedSubtitle()??"";}catch{ }
        if(string.IsNullOrWhiteSpace(fallback))fallback=root.name;
        var c=new Category{Root=root,Heading=heading,Fallback=fallback,Order=Categories.Count};
        Remember(root);root.SetParent(_panel,false);foreach(var layout in root.GetComponents<LayoutGroup>())Disable(layout);var fitter=root.GetComponent<ContentSizeFitter>();if(Alive(fitter))Disable(fitter);
        var image=root.GetComponent<Image>();if(Alive(image))Disable(image);
        if(Alive(heading))Disable(heading!);
        c.Visibility=Visibility(root.gameObject);Place(root,498,274,944,568);
        var ownRoot=Node("CategoryScroll",root,0,0,944,568,new Color(.12f,.10f,.14f,1));
        c.Viewport=Node("CategoryViewport",ownRoot,0,0,924,568);c.Content=Node("CategoryContent",c.Viewport,0,0,924,568);
        c.Scroll=Scroller(ownRoot,c.Viewport,c.Content);AddScrollbar(ownRoot,c.Scroll,932,0,568);
        // Old badges outside the row are now covered by the browser shell.
        string label=Alive(heading)?heading!.text:fallback;
        c.Tab=Button("CategoryTab",_tabs!,c.Order*150,0,146,49,label,()=>{_selected=root.Pointer;},out c.Label);
        if(_selected==IntPtr.Zero)_selected=root.Pointer;
        return c;
    }
    private static WildCard Read(Card card)
    {
        var element=card.Element;string title=element.id,subtitle=card.Category.Fallback;bool purchased=element.isUnlocked;bool? affordable=null;
        try {
            var upgrade=NetworkUpgrade.GetUpgradeById(element.id);
            if(upgrade is not null&&upgrade.Pointer!=IntPtr.Zero){title=upgrade.GetLocalizedTitle()??title;subtitle=upgrade.GetLocalizedSubtitle()??subtitle;purchased=upgrade.IsUnlocked();
                // Native GetCost and available credit text define affordability.
                // Read only; final eligibility/payment still belongs to OnUnlock.
                if(GameSession.TryResolve(_run?.Epoch,out var store))affordable=upgrade.GetCost()<=store.playerCash;
            }
        }catch { /* Custom elements with private IDs still remain in All/search. */ }
        return new(element.id,title,subtitle,purchased,affordable);
    }
    private static void Refresh()
    {
        var query=_search?.text??"";
        int tabIndex=0;
        foreach(var c in Categories.Values) {
            if(!Alive(c.Root))continue;
            Place(c.Tab.GetComponent<RectTransform>(),tabIndex++*150,0,146,49);
            var group=Cards.Values.Where(e=>ReferenceEquals(e.Category,c)&&Alive(e.Element)&&e.Element.gameObject.activeSelf).ToArray();int index=0;
            foreach(var card in group) {
                if(!Alive(card.Element))continue;
                bool visible=WildBrowserRules.Matches(Read(card),_filter,query);Show(card.Visibility,visible);
                if(!visible)continue;
                int columns=WildBrowserRules.Grid(924,568,group.Length).Columns;
                var rect=card.Element.GetComponent<RectTransform>();
                // Native hover/press uses Content.localPosition around a centred
                // card. A top-left pivot would shift it by half a card on hover.
                rect.anchorMin=rect.anchorMax=new Vector2(0,1);rect.pivot=new Vector2(.5f,.5f);rect.anchoredPosition=new Vector2(66+index%columns*132,-66-index/columns*132);rect.sizeDelta=new Vector2(100,100);rect.localScale=Vector3.one;index++;
            }
            c.Count=index;c.Content.sizeDelta=new Vector2(924,WildBrowserRules.Grid(924,568,index).Height);
            Show(c.Visibility,_selected==c.Root.Pointer);c.Label.text=(Alive(c.Heading)?c.Heading!.text:c.Fallback=="__unparented"?L("其他","Other"):c.Fallback)+"  "+index;
            c.Tab.GetComponent<Image>().color=_selected==c.Root.Pointer?Accent:Background;
        }
        _tabs!.sizeDelta=new Vector2(Math.Max(944,Categories.Count*150),49);
        if(Alive(_tabTrack))_tabTrack!.gameObject.SetActive(Categories.Count*150>944);
        foreach(var f in Filters){f.Button.GetComponent<Image>().color=f.Filter==_filter?Accent:Background;var label=f.Button.GetComponentInChildren<TextMeshProUGUI>();label.text=FilterName(f.Filter);}
        if(Alive(_hint))_hint!.text=L("滚轮浏览 · 点击查看详情","Scroll to browse · Select for details");
        // The preserved category roots must be above the owned browser background.
        foreach(var c in Categories.Values)if(Alive(c.Root))c.Root.SetAsLastSibling();
    }
    private static void Fit()
    {
        var canvas=_panel!.GetComponentInParent<Canvas>();var rect=canvas.GetComponent<RectTransform>().rect;
        float scale=Mathf.Min(1,Mathf.Min((rect.width-32)/1480,(rect.height-32)/900));_panel.localScale=Vector3.one*Mathf.Max(.1f,scale);
    }
    private static void Restore()
    {
        _generation++;
        foreach(var go in Owned)if(Alive(go)) {foreach(var button in go.GetComponents<Button>())button.onClick=new Button.ButtonClickedEvent();}
        for(int i=Undo.Count-1;i>=0;i--)try{Undo[i]();}catch(Exception e){MelonLogger.Warning("[EZapi] EZAPI_WILD_RESTORE: "+e.Message);}
        Undo.Clear();
        foreach(var go in Owned)if(Alive(go)){go.SetActive(false);Object.Destroy(go);}Owned.Clear();Delegates.Clear();Categories.Clear();Cards.Clear();Filters.Clear();
        _manager=null;_panel=_tabs=_tabTrack=null;_tabScroll=null;_search=null;_hint=null;_run=null;_selected=IntPtr.Zero;_filter=WildFilter.All;_next=0;_stressPending=_stressActive=false;
    }
}
