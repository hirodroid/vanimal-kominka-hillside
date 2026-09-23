using System;
using Object = UnityEngine.Object;
using System.IO;
using System.Text;
using TMPro;
using UdonSharpEditor;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace KominkaDogRun.Editor
{
    public static class KD_HandMenuScrollEditor
    {
        // 7 枚 (P2、2026-09-16: あそぶを右端に)。幅の和 1064 + 間隔 6×6 = 1100 = 板の内寸。文字は 24 → 22pt
        internal static readonly int[] Order = { 5, 0, 3, 1, 4, 2, 6 };
        static readonly float[] Widths = { 145, 150, 185, 145, 145, 145, 149 };
        const float TabPoints = 22;
        // 2026-09-17: 上段の余白 (ユーザー指摘「ヘッダ・帯・タブが詰まっている」)。上余白 14 / ヘッダ 44 / 隙間 7 / 帯 30 / 隙間 8 / タブ 46 / 一覧まで 15
        const float HeaderY = 314, HeaderHeight = 44, TabY = 224, TabHeight = 46;
        internal const float ContentTop = 186, ContentTopWithRow = 140, TopRowY = 164;
        static readonly string[] Prefixes = { "Row", "ToyRow", "FxRow", "SleepRow", "LiveRow", "AccRow" };
        const float RowHeight = 112;
        const float RowWidth = 508;

        /// <summary>Refresh only the existing Live tab after registering products/stages.
        /// Preserves all other tabs, panel placement and existing row objects. Does not save.</summary>
        public static void RefreshLiveCatalog(KD_TreatPouch p, StringBuilder log)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || p == null
                || p.gameObject.scene != SceneManager.GetActiveScene()
                || p.tabScrolls == null || p.tabScrolls.Length <= KD_TreatPouch.TabLive
                || p.catalog == null || p.catalog.items == null)
                throw new InvalidOperationException("Stopped active scene and configured hand menu/catalog required");
            var scroll = p.tabScrolls[KD_TreatPouch.TabLive];
            var content = scroll == null ? null : scroll.content;
            var template = content == null ? null : content.Find("LiveRow5");
            var target = UdonSharpEditorUtility.GetBackingUdonBehaviour(p);
            var shows = KD_DogDefs.LiveShows;
            if (template == null || target == null || shows.Length > 9
                || PrefabUtility.IsPartOfPrefabInstance(template.gameObject)
                || content.GetComponent<GridLayoutGroup>() == null
                || content.GetComponent<ContentSizeFitter>() == null)
                throw new InvalidOperationException("Existing custom Live grid and row template required");
            var items = new KD_ShopItem[shows.Length];
            for (int i = 0; i < shows.Length; i++)
            {
                int matches = 0;
                foreach (var item in p.catalog.items)
                    if (item != null && item.productKey == shows[i].productKey)
                    { items[i] = item; matches++; }
                if (matches != 1 || items[i].categoryKey != "live")
                    throw new InvalidOperationException("Missing/duplicate live product " + shows[i].productKey);
                var row = content.Find("LiveRow" + i) ?? template;
                if (row.Find("Label") == null || row.Find("Sub") == null || row.Find("Button/Label") == null
                    || row.Find("Button").GetComponent<Button>() == null)
                    throw new InvalidOperationException("Incomplete live row " + i);
            }
            // P7: 一覧の正本 liveProducts も同じカタログから引き直す (Codex 指摘 2026-09-17)
            var products = new KD_ShopItem[KD_DogDefs.LiveProducts.Length];
            for (int i = 0; i < products.Length; i++)
            {
                foreach (var item in p.catalog.items)
                    if (item != null && item.productKey == KD_DogDefs.LiveProducts[i].productKey) { products[i] = item; break; }
                if (products[i] == null) throw new InvalidOperationException("Missing live product " + KD_DogDefs.LiveProducts[i].productKey);
            }
            Undo.RecordObject(p, "Refresh live catalog");
            p.liveItems = items;
            p.liveProducts = products;
            p.liveTitles = new string[shows.Length]; p.liveTitleKeys = new string[shows.Length];
            p.liveLabels = new TextMeshProUGUI[shows.Length]; p.liveSubLabels = new TextMeshProUGUI[shows.Length];
            p.liveButtons = new GameObject[shows.Length]; p.liveButtonComponents = new Button[shows.Length];
            p.liveButtonImages = new Image[shows.Length]; p.liveButtonLabels = new TextMeshProUGUI[shows.Length];
            p.liveGiftButtons = new GameObject[shows.Length]; p.liveGiftLabels = new TextMeshProUGUI[shows.Length];
            for (int i = 0; i < shows.Length; i++)
            {
                var row = content.Find("LiveRow" + i);
                if (row == null)
                {
                    var created = Object.Instantiate(template.gameObject, content);
                    created.name = "LiveRow" + i; Undo.RegisterCreatedObjectUndo(created, "Add live row");
                    row = created.transform;
                }
                row.SetAsLastSibling(); row.gameObject.SetActive(true);
                var label = row.Find("Label").GetComponent<TextMeshProUGUI>();
                var sub = row.Find("Sub").GetComponent<TextMeshProUGUI>();
                var button = row.Find("Button").GetComponent<Button>();
                Undo.RecordObject(button, "Bind live action");
                button.onClick = new Button.ButtonClickedEvent();
                UnityEventTools.AddStringPersistentListener(button.onClick, target.SendCustomEvent, "_Live" + i);
                button.gameObject.SetActive(true); button.interactable = false;
                label.text = shows[i].title;
                sub.text = items[i].IsCE() ? KD_ShopShelf.WordSpecial : "";
                p.liveTitles[i] = shows[i].title; p.liveTitleKeys[i] = "KD_TreatPouch.liveTitleKeys." + i;
                p.liveLabels[i] = label; p.liveSubLabels[i] = sub;
                p.liveButtons[i] = button.gameObject; p.liveButtonComponents[i] = button;
                p.liveButtonImages[i] = button.GetComponent<Image>();
                p.liveButtonLabels[i] = row.Find("Button/Label").GetComponent<TextMeshProUGUI>();
                // [ギフト] (IMP-PRG-012): テンプレを複製した行も口を自分の添字へ結び直す。無い (古い Setup) なら Treat Setup を先に流す
                var giftT = row.Find("Gift");
                if (giftT == null) throw new InvalidOperationException("Live row " + i + " has no Gift button - run Treat Setup first (IMP-PRG-012)");
                var gift = giftT.GetComponent<Button>();
                Undo.RecordObject(gift, "Bind live gift");
                gift.onClick = new Button.ButtonClickedEvent();
                UnityEventTools.AddStringPersistentListener(gift.onClick, target.SendCustomEvent, "_LiveGift" + i);
                gift.gameObject.SetActive(false);
                p.liveGiftButtons[i] = gift.gameObject; p.liveGiftLabels[i] = giftT.Find("Label").GetComponent<TextMeshProUGUI>();
                EditorUtility.SetDirty(gift);
                EditorUtility.SetDirty(button); EditorUtility.SetDirty(label); EditorUtility.SetDirty(sub);
            }
            LayoutRebuilder.ForceRebuildLayoutImmediate(content);
            scroll.verticalNormalizedPosition = 1;
            EditorUtility.SetDirty(p); UdonSharpEditorUtility.CopyProxyToUdon(p);
            EditorSceneManager.MarkSceneDirty(p.gameObject.scene);
            log.AppendLine("Live tab refreshed: " + shows.Length + " rows; other tabs preserved. Run localization Bind/Verify and Treat/Scroll Verify before saving.");
        }

        [MenuItem("Tools/KominkaHillside/Menu/12 Rebuild Panel")]
        public static void RebuildPanel()
        {
            if(EditorApplication.isPlaying)throw new InvalidOperationException("Edit mode only");
            var p=KD_SceneObjects.Find(KD_DogDefs.TreatPouchName).GetComponent<KD_TreatPouch>();
            var log=new StringBuilder(); var buttons=p.panelRoot.GetComponentsInChildren<Button>(true);
            KD_TreatSetupEditor.BuildPouchPanel(p.gameObject,p,p.items,p.fxItems,p.sectionTitleLabel.font,log);
            if(KD_SceneObjects.Find("KD_Activities") != null) KD_ActivitiesEditor.Build();
            int preserved=0;foreach(var button in buttons)if(button!=null)preserved++;
            log.AppendLine("Preserved original buttons="+preserved+"/"+buttons.Length+" after="+p.panelRoot.GetComponentsInChildren<Button>(true).Length);
            UdonSharpEditorUtility.CopyProxyToUdon(p);EditorUtility.SetDirty(p);EditorSceneManager.MarkSceneDirty(p.gameObject.scene);
            File.WriteAllText("Temp/hand-menu-scroll-rebuild.txt",log.ToString());
        }

        [MenuItem("Tools/KominkaHillside/Menu/13 Apply Scroll Layout")]
        public static void Apply()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Edit mode only");
            var p = KD_SceneObjects.Find(KD_DogDefs.TreatPouchName).GetComponent<KD_TreatPouch>();
            if (p.tabPages == null || p.tabPages.Length != KD_TreatPouch.TabCount) throw new InvalidOperationException(KD_TreatPouch.TabCount + " hand menu pages required");
            var log = new StringBuilder();
            KD_ShopSetupEditor.AddGlyphs(KD_TreatPouch.WordTabPetFx, log);
            Build(p, log);
            EditorUtility.SetDirty(p); UdonSharpEditorUtility.CopyProxyToUdon(p);
            EditorSceneManager.MarkSceneDirty(p.gameObject.scene); AssetDatabase.SaveAssets();
            File.WriteAllText("Temp/hand-menu-scroll-apply.txt", log.ToString());
        }

        // The original builders resolve direct children. Return the existing rows before rebuilding,
        // then reapply this layout. This preserves objects, references and persistent event targets.
        internal static void Unwrap(Transform panel)
        {
            foreach (Transform page in panel)
            {
                if (!page.name.StartsWith("Page_")) continue;
                var content = page.Find("Scroll/Viewport/Content");
                if (content == null) continue;
                while (content.childCount > 0) content.GetChild(0).SetParent(page, false);
            }
        }

        static RectTransform Rect(Transform parent, string name, Vector2 pos, Vector2 size)
        { return (RectTransform)KD_ShopSetupEditor.EnsureUiChild(parent, name, pos, size).transform; }

        static void Place(Transform t, Vector2 pos, Vector2 size)
        {
            if (t == null) return;
            var r = (RectTransform)t; r.anchorMin = r.anchorMax = r.pivot = new Vector2(.5f,.5f);
            r.anchoredPosition = pos; r.sizeDelta = size; r.localScale = Vector3.one;
        }

        static Image Image(RectTransform r, Color color, bool rounded)
        {
            var i = r.GetComponent<Image>(); if (i == null) i = r.gameObject.AddComponent<Image>();
            i.color = color; i.raycastTarget = true;
            if (rounded) KD_UiSpriteEditor.ApplyRoundSkin(i, KD_UiSpriteEditor.LoadRound()); else KD_UiSpriteEditor.ApplyFlatSkin(i);
            return i;
        }

        static void Text(Transform row, string name, Vector2 pos, Vector2 size, float points)
        {
            var t = row.Find(name); if(t == null) return; Place(t,pos,size);
            var label = t.GetComponent<TextMeshProUGUI>(); if(label != null) { label.fontSize = points; label.enableWordWrapping = false; }
        }

        static void Action(Transform row, string name, float x, float y, float width)
        {
            Action(row, name, x, y, width, 44);
        }

        static void Action(Transform row, string name, float x, float y, float width, float height)
        {
            var t = row.Find(name); if(t == null) return;
            Place(t,new Vector2(x,y),new Vector2(width,height));
            Text(t,"Label",Vector2.zero,new Vector2(width-12,height-4),20);
        }

        // Shared by the panel and settings builders so either rebuild keeps the same frame.
        internal static void LayoutFrame(KD_TreatPouch p)
        {
            if(p.panelRoot == null) return;
            var panel=p.panelRoot.transform;
            var size=new Vector2(KD_TreatSetupEditor.PanelWidth,KD_TreatSetupEditor.PanelHeight);
            ((RectTransform)panel).sizeDelta=size;
            Place(panel.Find("Bg"),Vector2.zero,size);
            // P10 (2026-09-16): ヘッダ 310 → 318、タブ 248 → 238 にして間に帯 (Y=278.5、28px) を入れる
            Text(panel,"SectionTitle",new Vector2(-335,HeaderY),new Vector2(430,HeaderHeight),24);
            // 2026-09-17: 文字の箱 232 / 枠 284 → 左右 26px の余白 (数字が枠線に触れていた)
            Text(panel,"Balance",new Vector2(150,HeaderY),new Vector2(232,HeaderHeight),22);
            { var points=panel.Find(KD_GiftCodeSetupEditor.PointsButtonName); if(points!=null)Place(points,new Vector2(150,HeaderY),new Vector2(284,HeaderHeight+6)); }
            Action(panel,"SettingsButton",370,HeaderY,120);
            Action(panel,"CloseButton",500,HeaderY,110);
            { var band=panel.Find(KD_TreatSetupEditor.EventBandName); if(band!=null){ Place(band,new Vector2(0,KD_TreatSetupEditor.EventBandY),new Vector2(1100,KD_TreatSetupEditor.EventBandHeight)); Text(band,"Label",Vector2.zero,new Vector2(1060,KD_TreatSetupEditor.EventBandHeight),18); } }
            Text(panel,"CloseHint",new Vector2(-400,-328),new Vector2(300,28),18);
            Text(panel,"Feedback",new Vector2(0,-285),new Vector2(1100,44),20);
        }

        internal static void Build(KD_TreatPouch p, StringBuilder log)
        {
            if(p.panelRoot == null || p.tabPages == null) return;
            LayoutFrame(p);
            float left = -550;
            foreach (int id in Order)
            {
                var tab = p.tabImages[id].transform;
                float width = Widths[id]; Place(tab,new Vector2(left+width/2,TabY),new Vector2(width,TabHeight));
                Text(tab,"Label",Vector2.zero,new Vector2(width-8,TabHeight-6),TabPoints);
                if(id == KD_TreatPouch.TabPetFx) tab.Find("Label").GetComponent<TextMeshProUGUI>().text = KD_TreatPouch.WordTabPetFx;
                var line = (RectTransform)p.tabUnderlines[id].transform;
                Place(line,new Vector2(0,-(TabHeight/2-3)),new Vector2(width,5));
                p.tabImages[id].color = id == KD_TreatPouch.TabAcc ? p.tabOnColor : p.tabOffColor;
                p.tabUnderlines[id].SetActive(id == KD_TreatPouch.TabAcc);
                left += width + 6;
            }
            // Scroll を持つのは先頭 6 枚。あそぶ (TabPlay) は KD_ActivitiesMenu の固定配置
            p.tabScrolls = new ScrollRect[6];
            for(int id=0; id<6; id++)
            {
                var page = p.tabPages[id].transform;
                Place(page,Vector2.zero,new Vector2(KD_TreatSetupEditor.PanelWidth,KD_TreatSetupEditor.PanelHeight));
                float top = id == 3 || id == 5 || id == 4 ? ContentTopWithRow : ContentTop;
                float bottom = -250;
                var root = Rect(page,"Scroll",new Vector2(0,(top+bottom)/2),new Vector2(1100,top-bottom));
                var view = Rect(root,"Viewport",new Vector2(-25,0),new Vector2(1044,top-bottom));
                Image(view,new Color(0,0,0,0),false);
                if(view.GetComponent<RectMask2D>() == null) view.gameObject.AddComponent<RectMask2D>();
                var content = Rect(view,"Content",Vector2.zero,new Vector2(0,0));
                content.anchorMin = new Vector2(0,1); content.anchorMax = new Vector2(1,1); content.pivot = new Vector2(.5f,1);
                var oldGroup=content.GetComponent<VerticalLayoutGroup>();
                if(oldGroup!=null)UnityEngine.Object.DestroyImmediate(oldGroup);
                var group = content.GetComponent<GridLayoutGroup>(); if(group == null) group = content.gameObject.AddComponent<GridLayoutGroup>();
                group.padding = new RectOffset(8,8,8,8); group.spacing = new Vector2(12,12); group.childAlignment = TextAnchor.UpperLeft;
                group.cellSize=new Vector2(RowWidth,RowHeight);group.constraint=GridLayoutGroup.Constraint.FixedColumnCount;group.constraintCount=2;
                group.startCorner=GridLayoutGroup.Corner.UpperLeft;group.startAxis=GridLayoutGroup.Axis.Horizontal;
                var fitter = content.GetComponent<ContentSizeFitter>(); if(fitter == null) fitter = content.gameObject.AddComponent<ContentSizeFitter>();
                fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained; fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
                int count=0;
                // かざりは 16 行 (KD_AccessorySetsEditor.MenuCount。一覧は 14 品 + セット面の予備)。
                // 2026-09-16 まで 6 で止めていたので AccRow6〜15 が Page_Acc 直下 (Y=-272 以下) に残り、7 品目以降が板の外だった (P9 の並び替えで発覚)
                int rowCount = id == KD_TreatPouch.TabLive ? KD_DogDefs.LiveShows.Length
                    : id == KD_TreatPouch.TabPetFx ? KD_ShopSetupEditor.PetFxProductCount
                    : id == KD_TreatPouch.TabAcc ? KD_AccessorySetsEditor.MenuCount : 6;
                for(int rowIndex=0; rowIndex<rowCount; rowIndex++)
                {
                    string name = Prefixes[id]+rowIndex;
                    var row = page.Find(name); if(row == null) row = content.Find(name); if(row == null) continue;
                    row.SetParent(content,false); row.SetAsLastSibling(); count++;
                    var element = row.GetComponent<LayoutElement>(); if(element == null) element = row.gameObject.AddComponent<LayoutElement>();
                    element.minHeight = element.preferredHeight = RowHeight; element.flexibleHeight = 0;
                    Place(row,Vector2.zero,new Vector2(RowWidth,RowHeight));
                    Text(row,"Label",new Vector2(0,32),new Vector2(480,36),22);
                    foreach(string detail in new[]{"Hint","Reason","Sub"})
                    {
                        Text(row,detail,new Vector2(-76,-18),new Vector2(328,60),18);
                        var t=row.Find(detail);if(t!=null)t.GetComponent<TextMeshProUGUI>().enableWordWrapping=true;
                    }
                    Action(row,"Button",178,-18,136);
                    if(id == KD_TreatPouch.TabLive)
                    {
                        // [ギフト] (IMP-PRG-012) は名前の行の右端 (x 110..246、高さ 40)。名前の幅は実行時 (RedrawLive) が出る行だけ 340 に詰める
                        Action(row,"Gift",178,32,136,40);
                    }
                    if(id == 5)
                    {
                        Text(row,"Sub",new Vector2(-96,-18),new Vector2(288,60),18);
                        row.Find("Sub").GetComponent<TextMeshProUGUI>().enableWordWrapping=true;
                        Action(row,"Button",158,-18,176);
                        // [ギフト] (IMP-PRG-012): 右端 x 86..246。名前の幅は実行時 (RedrawAcc) が出る行だけ 300 に詰める
                        Action(row,"Gift",166,32,160,40);
                    }
                    if(id == 1)
                    {
                        Place(row.Find("Swatch"),new Vector2(-230,32),new Vector2(22,22));
                        Text(row,"Label",new Vector2(15,32),new Vector2(435,36),22);
                    }
                    if(id == 3)
                    {
                        Place(row.Find("Swatch"),new Vector2(-230,35),new Vector2(22,22));
                        Text(row,"Label",new Vector2(15,35),new Vector2(435,30),22);
                        Text(row,"Status",new Vector2(0,4),new Vector2(480,28),18);
                        Action(row,"Button",0,-34,300);
                    }
                }
                if(id == 0)
                {
                    // IMP-TRT-007: remove the retired shared-slot display in either legacy layout.
                    var oldSlots=page.Find("Slots");
                    if(oldSlots!=null)UnityEngine.Object.DestroyImmediate(oldSlots.gameObject);
                    oldSlots=content.Find("Slots");
                    if(oldSlots!=null)UnityEngine.Object.DestroyImmediate(oldSlots.gameObject);
                    foreach(string name in new[]{"Status"})
                    {
                        var t=page.Find(name); if(t==null)t=content.Find(name); if(t==null)continue;
                        t.SetParent(page,false);
                        var el=t.GetComponent<LayoutElement>();if(el!=null)UnityEngine.Object.DestroyImmediate(el);
                        Text(page,name,new Vector2(-145,-285),new Vector2(810,44),20);
                    }
                    Action(page,"PutAwayButton",430,-285,220);
                }
                if(id == 3) { Text(page,"Note",new Vector2(150,-328),new Vector2(700,28),18); }
                if(id == 4) Text(page,"Status",new Vector2(0,TopRowY),new Vector2(1080,36),20);
                if(id == 5) { Text(page,"Header",new Vector2(-100,TopRowY),new Vector2(900,36),22);Action(page,"BackButton",470,TopRowY,140); }
                var bar = Rect(root,"Scrollbar",new Vector2(530,0),new Vector2(40,top-bottom));
                Image(bar,KD_UiSpriteEditor.SurfaceRaised,false);
                var track=Rect(bar,"SlidingArea",Vector2.zero,new Vector2(32,top-bottom-8));
                var handle=Rect(track,"Handle",Vector2.zero,new Vector2(32,40));
                // Scrollbar drives both anchor ranges; a nonzero sizeDelta enlarges the handle past the track.
                handle.sizeDelta=Vector2.zero;
                var handleImage=Image(handle,KD_UiSpriteEditor.ActionNeutral,true);
                var scrollbar=bar.GetComponent<Scrollbar>();if(scrollbar==null)scrollbar=bar.gameObject.AddComponent<Scrollbar>();
                scrollbar.handleRect=handle;scrollbar.targetGraphic=handleImage;scrollbar.direction=Scrollbar.Direction.BottomToTop;
                scrollbar.navigation=new Navigation{mode=Navigation.Mode.None};
                var scroll=root.GetComponent<ScrollRect>();if(scroll==null)scroll=root.gameObject.AddComponent<ScrollRect>();
                scroll.viewport=view;scroll.content=content;scroll.horizontal=false;scroll.vertical=true;
                scroll.movementType=ScrollRect.MovementType.Clamped;scroll.inertia=false;scroll.scrollSensitivity=35;
                scroll.verticalScrollbar=scrollbar;scroll.verticalScrollbarVisibility=ScrollRect.ScrollbarVisibility.Permanent;
                scroll.verticalNormalizedPosition=1; p.tabScrolls[id]=scroll;
                p.tabPages[id].SetActive(id==KD_TreatPouch.TabAcc);
                log.AppendLine("tab="+id+" rows="+count+" viewport="+view.sizeDelta+" grid=2 columns scroll=vertical/permanent");
            }
            p.sectionTitleLabel.text=KD_TreatPouch.WordAccTitle;
            EditorUtility.SetDirty(p);
        }

        [MenuItem("Tools/KominkaHillside/Menu/21 Verify Scroll Layout")]
        public static void Verify()
        {
            var p=KD_SceneObjects.Find(KD_DogDefs.TreatPouchName).GetComponent<KD_TreatPouch>();
            var log=new StringBuilder();int fail=0,pass=0;
            void Check(bool ok,string what){if(ok)pass++;else fail++;log.AppendLine((ok?"PASS ":"FAIL ")+what);}
            Check(((RectTransform)p.panelRoot.transform).sizeDelta==new Vector2(1140,700),"board 1140x700");
            float previous=-570;
            foreach(int id in Order)
            {
                var r=(RectTransform)p.tabImages[id].transform;
                Check(r.anchoredPosition.x-r.rect.width/2>=previous,"tab order/spacing "+id);previous=r.anchoredPosition.x+r.rect.width/2;
                var text=r.Find("Label").GetComponent<TextMeshProUGUI>();
                Check(text.GetPreferredValues(text.text,10000,0).x<=text.rectTransform.rect.width+1,"tab text "+text.text);
                Check(previous<=570,"tab within board "+id);
            }
            for(int id=0;id<6;id++)
            {
                var s=p.tabScrolls[id];
                Check(s!=null && s.vertical && !s.horizontal && s.content!=null && s.viewport!=null,"scroll wiring "+id);
                Check(s.viewport.GetComponent<RectMask2D>()!=null && s.viewport.GetComponent<Image>().raycastTarget,"mask and drag surface "+id);
                Check(s.verticalScrollbar!=null && s.verticalScrollbarVisibility==ScrollRect.ScrollbarVisibility.Permanent,"permanent scrollbar "+id);
                Check(s.verticalScrollbar.handleRect.sizeDelta==Vector2.zero,"scrollbar handle stays within track "+id);
                Check(p.tabPages[id].activeSelf==(id==KD_TreatPouch.TabAcc),"initial active page "+id);
                var grid=s.content.GetComponent<GridLayoutGroup>();
                Check(grid!=null && grid.constraint==GridLayoutGroup.Constraint.FixedColumnCount && grid.constraintCount==2,"two columns "+id);
                Check(s.content.GetComponent<VerticalLayoutGroup>()==null,"single layout driver "+id);
                Check(grid.cellSize.x*2+grid.spacing.x+grid.padding.horizontal<=s.viewport.rect.width,"two columns fit width "+id);
                Check(grid.cellSize.y*3+grid.spacing.y*2+grid.padding.vertical<=s.viewport.rect.height,"six choices fit height "+id);
                if (id == KD_TreatPouch.TabLive)
                    Check(s.content.childCount == KD_DogDefs.LiveShows.Length, "all live rows in scroll content");
                if (id == KD_TreatPouch.TabAcc)
                    Check(s.content.childCount == KD_AccessorySetsEditor.MenuCount && System.Linq.Enumerable.All(p.accRows, r => r.transform.parent == s.content),
                        "all " + KD_AccessorySetsEditor.MenuCount + " accessory rows in scroll content (7th item and later were off the panel until 2026-09-16)");
                foreach(Transform row in s.content)
                {
                    Check(row.GetComponent<LayoutElement>()!=null,"layout element "+row.name);
                    foreach(var b in row.GetComponentsInChildren<Button>(true))
                    {
                        Check(b.onClick.GetPersistentEventCount()==1 && b.onClick.GetPersistentTarget(0)==UdonSharpEditorUtility.GetBackingUdonBehaviour(p),"preserved action "+row.name+"/"+b.name);
                        string index=row.name.Substring(Prefixes[id].Length);
                        string evt="_"+new[]{"Row","Toy","Fx","Sleep","Live","Acc"}[id]+(b.name=="Gift"?"Gift":"")+index;   // [ギフト] は _LiveGift{i} / _AccGift{i} (IMP-PRG-012)
                        var calls=new SerializedObject(b).FindProperty("m_OnClick.m_PersistentCalls.m_Calls");
                        Check(calls.arraySize==1 && calls.GetArrayElementAtIndex(0).FindPropertyRelative("m_MethodName").stringValue=="SendCustomEvent"
                            && calls.GetArrayElementAtIndex(0).FindPropertyRelative("m_Arguments.m_StringArgument").stringValue==evt,"action event "+evt);
                    }
                }
                Check(p.tabPages[id].transform.Find(Prefixes[id]+"0")==null,"no duplicate unscrolled rows "+id);
            }
            Check(p.confirmRoot!=null && p.confirmRoot.transform.parent==p.panelRoot.transform && p.confirmRoot.transform.GetSiblingIndex()==p.panelRoot.transform.childCount-1 && !p.confirmRoot.activeSelf,"purchase confirmation is one shared card, last sibling, closed by default (P13)");
            Check(p.fxConfirmRoot==p.confirmRoot && p.liveConfirmRoot==p.confirmRoot && p.accConfirmRoot==p.confirmRoot,"fx/live/acc confirmations all point at the shared card");
            Check(p.tabPages[2].transform.Find("FxConfirm")==null && p.tabPages[4].transform.Find("LiveConfirm")==null && p.tabPages[5].transform.Find("AccConfirm")==null,"legacy confirm bands removed");
            Check(((RectTransform)p.tabPages[5].transform.Find("BackButton")).anchoredPosition==new Vector2(470,TopRowY),"accessory back above grid");
            // 2026-09-17: 上段の隙間 — ヘッダ / 帯 / タブが重ならず、各 6px 以上あく。一覧の上端はタブの下端より下
            {
                var title=(RectTransform)p.sectionTitleLabel.transform; var band=p.eventBandRoot==null?null:(RectTransform)p.eventBandRoot.transform; var tab0=(RectTransform)p.tabImages[0].transform;
                float titleBottom=title.anchoredPosition.y-title.sizeDelta.y/2, tabTop=tab0.anchoredPosition.y+tab0.sizeDelta.y/2, tabBottom=tab0.anchoredPosition.y-tab0.sizeDelta.y/2;
                Check(KD_TreatSetupEditor.PanelHeight/2-(title.anchoredPosition.y+title.sizeDelta.y/2)>=12,"header top margin >= 12");
                if(band!=null){ float bandTop=band.anchoredPosition.y+band.sizeDelta.y/2, bandBottom=band.anchoredPosition.y-band.sizeDelta.y/2;
                    Check(titleBottom-bandTop>=6 && bandBottom-tabTop>=6,"header / band / tabs keep >= 6px gaps ("+(titleBottom-bandTop)+" / "+(bandBottom-tabTop)+")"); }
                for(int id=0;id<6;id++){ var sc=p.tabScrolls[id]; var r=(RectTransform)sc.transform; Check(r.anchoredPosition.y+r.sizeDelta.y/2<=tabBottom-8,"scroll top below tabs "+id); }
            }
            Check(p.statusLabel.transform.parent==p.tabPages[0].transform,"treat status outside grid");
            Check(p.tabPages[0].transform.Find("Slots")==null && p.tabScrolls[0].content.Find("Slots")==null,"retired shared treat counter absent");
            Check(KD_ShopSetupEditor.MissingGlyphs(KD_TreatPouch.WordTabPetFx).Length==0,"particle glyphs");
            log.AppendLine("RESULT pass="+pass+" fail="+fail);File.WriteAllText("Temp/hand-menu-scroll-verify.txt",log.ToString());
            if(fail>0)Debug.LogError(log.ToString());
        }
    
        // ---- KD_HandMenuScrollPreviewEditor から併合 (IMP-INF-019 C3、2026-09-13) ----
        [MenuItem("Tools/KominkaHillside/Menu/21a Preview Scroll Pages")]
        public static void Preview()
        { PreviewPages(false); }

        [MenuItem("Tools/KominkaHillside/Menu/21b Preview Live Catalog Only")]
        public static void PreviewLiveCatalogOnly()
        { PreviewPages(true); }

        [MenuItem("Tools/KominkaHillside/Menu/21c Preview Particle Catalog Only")]
        public static void PreviewParticleCatalogOnly() { PreviewPages(false, KD_TreatPouch.TabPetFx); }

        static void PreviewPages(bool liveOnly, int onlyTab = -1)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Stop Play before capturing menu previews");
            var p=KD_SceneObjects.Find(KD_DogDefs.TreatPouchName).GetComponent<KD_TreatPouch>();
            var scene=EditorSceneManager.NewPreviewScene();var previous=RenderTexture.active;
            int width=(int)KD_TreatSetupEditor.PanelWidth,height=(int)KD_TreatSetupEditor.PanelHeight;
            var rt=new RenderTexture(width,height,24);Texture2D image=null;
            var fit=new StringBuilder();int overflow=0;
            try
            {
                var clone=Object.Instantiate(p.panelRoot);clone.SetActive(false);SceneManager.MoveGameObjectToScene(clone,scene);
                foreach(var b in clone.GetComponentsInChildren<UdonSharp.UdonSharpBehaviour>(true))Object.DestroyImmediate(b);
                foreach(var b in clone.GetComponentsInChildren<VRC.Udon.UdonBehaviour>(true))Object.DestroyImmediate(b);
                foreach(var t in clone.GetComponentsInChildren<Transform>(true))t.gameObject.layer=31;
                clone.transform.SetPositionAndRotation(Vector3.zero,Quaternion.identity);clone.transform.localScale=Vector3.one*.001f;
                clone.GetComponent<Canvas>().renderMode=RenderMode.WorldSpace;
                clone.transform.Find("Balance").GetComponent<TextMeshProUGUI>().text="ポイント 100 pt";
                var cameraGo=new GameObject("PreviewCamera");SceneManager.MoveGameObjectToScene(cameraGo,scene);cameraGo.transform.position=new Vector3(0,0,-2);
                var camera=cameraGo.AddComponent<Camera>();camera.scene=scene;camera.orthographic=true;camera.orthographicSize=height*.0005f;camera.aspect=(float)width/height;
                camera.cullingMask=1<<31;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.1f,.1f,.1f);camera.targetTexture=rt;
                image=new Texture2D(width,height,TextureFormat.RGB24,false);clone.SetActive(true);
                for(int capture=0;capture<p.language.languageCodes.Length*7;capture++)
                {
                    int id=capture%7;bool dogs=id==6;if(dogs)id=5;
                    if(liveOnly && id!=KD_TreatPouch.TabLive)continue;
                    if(onlyTab >= 0 && id != onlyTab)continue;
                    string lang=p.language.languageCodes[capture/7];
                    string L(string key){return p.language._TextForLanguage(key,lang);}
                    // Each page gets a fresh canvas: edit-mode captures do not run the normal
                    // frame lifecycle that invalidates TMP/RectMask culling when tabs change.
                    if(capture>0)
                    {
                        Object.DestroyImmediate(clone);
                        clone=Object.Instantiate(p.panelRoot);clone.SetActive(false);SceneManager.MoveGameObjectToScene(clone,scene);
                        foreach(var b in clone.GetComponentsInChildren<UdonSharp.UdonSharpBehaviour>(true))Object.DestroyImmediate(b);
                        foreach(var b in clone.GetComponentsInChildren<VRC.Udon.UdonBehaviour>(true))Object.DestroyImmediate(b);
                        foreach(var t in clone.GetComponentsInChildren<Transform>(true))t.gameObject.layer=31;
                        clone.transform.SetPositionAndRotation(Vector3.zero,Quaternion.identity);clone.transform.localScale=Vector3.one*.001f;
                        clone.GetComponent<Canvas>().renderMode=RenderMode.WorldSpace;
                        clone.transform.Find("Balance").GetComponent<TextMeshProUGUI>().text="ポイント 100 pt";
                    }
                    for(int i=0;i<p.language.fixedLabels.Length;i++)
                    {
                        var original=p.language.fixedLabels[i];
                        if(original==null || !original.transform.IsChildOf(p.panelRoot.transform))continue;
                        var target=clone.transform.Find(Relative(p.panelRoot.transform,original.transform));
                        if(target!=null)target.GetComponent<TMP_Text>().text=L(p.language.fixedKeys[i]);
                    }
                    clone.transform.Find("Balance").GetComponent<TMP_Text>().text=L("KD_ShopShelf.WordRemain")+" 100 pt";
                    // P10: インフォメーション帯を販売中の姿で撮る
                    { var band=clone.transform.Find(KD_TreatSetupEditor.EventBandName); if(band!=null){ band.gameObject.SetActive(true); band.Find("Label").GetComponent<TMP_Text>().text=L("event.halloween.text"); } }
                    for(int j=0;j<6;j++)
                    {
                        clone.transform.Find(p.tabPages[j].name).gameObject.SetActive(j==id);
                        var tab=clone.transform.Find("Tab"+j);tab.GetComponent<Image>().color=j==id?p.tabOnColor:p.tabOffColor;
                        tab.Find(p.tabUnderlines[j].name).gameObject.SetActive(j==id);
                    }
                    clone.transform.Find("Page_Settings").gameObject.SetActive(false);
                    string title=clone.transform.Find("Tab"+id+"/Label").GetComponent<TextMeshProUGUI>().text;
                    clone.transform.Find("SectionTitle").GetComponent<TextMeshProUGUI>().text=title;
                    var page=clone.transform.Find(p.tabPages[id].name);var s=page.Find("Scroll").GetComponent<ScrollRect>();
                    // Accessory metadata includes set members; the top-level menu does not.
                    var accessoryRows=new System.Collections.Generic.List<int>();
                    if(id==5 && !dogs)
                        for(int a=0;p.accProductIds!=null && a<p.accProductIds.Length;a++)
                        {
                            bool member=false;
                            for(int set=0;p.accessories!=null && p.accessories.setIds!=null && set<p.accessories.setIds.Length;set++)
                                if(p.accessories.SelectionContains(p.accessories.setIds[set],p.accProductIds[a])){member=true;break;}
                            if(!member)accessoryRows.Add(a);
                        }
                    int rowCount=id==0?p.items.Length:id==2?p.fxItems.Length:id==4?(p.liveProducts==null?p.liveTitleKeys.Length:p.liveProducts.Length)
                        :id==5&&!dogs?accessoryRows.Count:p.toyNameKeys.Length;
                    int rowIndex=0;
                    foreach(Transform row in s.content)
                    {
                        var label=row.Find("Label");if(label==null)continue;
                        int index=rowIndex++;
                        // Shared row templates can outnumber the current products or six dogs.
                        row.gameObject.SetActive(index<rowCount);
                        if(index>=rowCount)continue;
                        int metadata=id==5&&!dogs?accessoryRows[index]:index;
                        string name=id==0?L(p.items[index].displayNameKey)+" / 99"+L("KD_ShopShelf.WordCount")
                            :id==2?L(p.fxItems[index].displayNameKey)+"  "+L("KD_TreatPouch.WordAccWaitPrefix")+" 100pt"
                            :id==4?(p.liveProducts==null?L(p.liveTitleKeys[index]):L(p.liveProducts[index].displayNameKey))
                            :id==5&&!dogs?L(p.accNameKeys[metadata]):L(p.toyNameKeys[index]);
                        label.GetComponent<TextMeshProUGUI>().text=name;
                        label.gameObject.SetActive(true);
                        foreach(var button in row.GetComponentsInChildren<Button>(true))button.gameObject.SetActive(true);
                        void Set(string path,string value){var t=row.Find(path);if(t!=null)t.GetComponent<TMP_Text>().text=value;}
                        if(id==1)Set("Reason",index%2==0?L("KD_TreatPouch.WordHeldByOther"):"WWWWW"+L("KD_TreatPouch.WordSleepingSuffix"));
                        if(id==4 && p.liveProducts!=null)
                        {
                            // P7: 一覧の行 = 商品。2 行目は 札 + 価格、ボタンは 未所持 [くわしく]
                            var item=p.liveProducts[index];
                            bool set=item.productKey=="live.candy_step_set"||item.productKey=="live.pumpkin_parade_set";
                            string tag=set?L("live.tag.set"):item.productKey=="live.kominka_pack"?L("live.tag.pack"):"";
                            Set("Sub",(tag.Length>0?tag+"  ":"")+(item.IsCE()?item.ceCredits+" C":item.price+" pt"));
                            Set("Button/Label",L("KD_TreatPouch.WordLiveMore"));
                        }
                        if(id==2)
                        {
                            var item=p.fxItems[index];
                            string bundleKey=item.productKey=="petfx.candy"?"fx.bundle.candy":item.productKey=="petfx.pumpkin"?"fx.bundle.pumpkin":"fx.bundle.livePack";
                            Set("Label",L(item.displayNameKey)+(item.IsCE()?"  "+item.ceCredits+" C":"  "+item.price+" pt"));
                            Set("Sub",item.IsCE()?L(bundleKey):"");
                            Set("Button/Label",L("KD_TreatPouch.WordBuyHere"));
                        }
                        if(id==3)Set("Status",new string('W',16)+L("KD_TreatPouch.WordSleepingSuffix"));
                        if(id==4 && p.liveProducts==null)Set("Sub",liveOnly?(p.liveItems[index].IsCE()?L("KD_ShopShelf.WordSpecial"):p.liveItems[index].price+" pt")
                            :L("KD_TreatPouch.WordAccWaitPrefix")+" 100pt");
                        if(id==5)
                        {
                            Set("Sub",dogs?L(p.accNameKeys[1]):L(p.accSlotNameKeys[metadata]));
                            Set("Button/Label",L(dogs?"KD_TreatPouch.WordAccReplaces":"KD_TreatPouch.WordAccNotOwned"));
                        }
                    }
                    if(id==5)
                    {
                        page.Find("BackButton").gameObject.SetActive(dogs);
                        page.Find("Header").GetComponent<TMP_Text>().text=dogs?L(p.accNameKeys[1]):L("KD_TreatPouch.WordAccTitle");
                    }
                    // P13: 確認カードをライブ / パーティクルの撮影で開いた状態にして、文の収まりを見る
                    if(id==4 || (id==2 && p.fxItems.Length>1))
                    {
                        var confirm=clone.transform.Find(KD_TreatSetupEditor.ConfirmPanelName);
                        if(confirm!=null)
                        {
                            confirm.gameObject.SetActive(!liveOnly);
                            string itemName=id==4?L("KD_ShopItem_Live_26.displayNameKey.0"):L(p.fxItems[p.fxItems.Length>7?7:1].displayNameKey);
                            confirm.Find("Card/Title").GetComponent<TMP_Text>().text=L("confirm.storeTitle").Replace("{0}",itemName);
                            confirm.Find("Card/Body").GetComponent<TMP_Text>().text=id==4?L("live.tag.pack"):L(p.fxItems.Length>7?"fx.bundle.pumpkin":"fx.bundle.livePack");
                            confirm.Find("Card/Note").GetComponent<TMP_Text>().text="200 Credits · "+L("confirm.storeNote");
                            confirm.Find("Card/Yes/Label").GetComponent<TMP_Text>().text=L("confirm.storeButton");
                            confirm.Find("Card/No/Label").GetComponent<TMP_Text>().text=L("call.cancelPurchase");
                        }
                    }
                    clone.SetActive(true);
                    Canvas.ForceUpdateCanvases();LayoutRebuilder.ForceRebuildLayoutImmediate(s.content);s.verticalNormalizedPosition=1;
                    foreach(var t in clone.GetComponentsInChildren<TextMeshProUGUI>())
                    {
                        t.isOrthographic=true;
                        var preferred=t.GetPreferredValues(t.text,t.rectTransform.rect.width,Mathf.Infinity);
                        if(preferred.y>t.rectTransform.rect.height+2 || (!t.enableWordWrapping && preferred.x>t.rectTransform.rect.width+2))
                        { overflow++;fit.AppendLine(lang+" page="+id+" dogs="+dogs+" "+Relative(clone.transform,t.transform)+" rect="+t.rectTransform.rect.size+" preferred="+preferred+" text="+t.text); }
                    }
                    void Capture(string suffix)
                    {
                        Canvas.ForceUpdateCanvases();foreach(var t in clone.GetComponentsInChildren<TextMeshProUGUI>(true))t.ForceMeshUpdate(true);
                        Canvas.ForceUpdateCanvases();
                        foreach(var mask in clone.GetComponentsInChildren<RectMask2D>())mask.PerformClipping();
                        s.verticalScrollbar.SetValueWithoutNotify(s.verticalNormalizedPosition);
                        camera.Render();RenderTexture.active=rt;image.ReadPixels(new Rect(0,0,width,height),0,0);image.Apply();
                        File.WriteAllBytes("Temp/hand-scroll-"+lang+"-"+id+(dogs?"-dogs":"")+suffix+".png",image.EncodeToPNG());
                    }
                    Capture("-top");s.verticalNormalizedPosition=0;Capture("-bottom");
                    // P8 (2026-09-16): ライブの詳細の面 (専用セット = パンプキン / Pack) を同じ撮影で見る
                    if(id==4 && p.liveDetailRoot!=null && !liveOnly)
                    {
                        var detail=clone.transform.Find(Relative(p.panelRoot.transform,p.liveDetailRoot.transform));
                        var confirmGo=clone.transform.Find(KD_TreatSetupEditor.ConfirmPanelName);if(confirmGo!=null)confirmGo.gameObject.SetActive(false);
                        s.gameObject.SetActive(false);detail.gameObject.SetActive(true);
                        page.Find("LiveBack").gameObject.SetActive(true);page.Find("LiveAction").gameObject.SetActive(true);
                        foreach(var variant in new[]{"pumpkin_parade_set","kominka_pack"})
                        {
                            bool set=variant=="pumpkin_parade_set";string k="live.detail."+variant+".";
                            clone.transform.Find("SectionTitle").GetComponent<TextMeshProUGUI>().text=L(set?"KD_ShopItem_Live_PumpkinParadeSet.displayNameKey.0":"KD_ShopItem_Live_26.displayNameKey.0");
                            detail.Find("Title").GetComponent<TMP_Text>().text=L(k+"jp");
                            detail.Find("Meta").GetComponent<TMP_Text>().text=L(k+"meta")+"  "+(set?"300 C":"200 C");
                            { int pi=System.Array.FindIndex(p.liveProducts,x=>x!=null&&x.productKey=="live."+variant); var thumb=pi>=0&&p.liveThumbs!=null&&pi<p.liveThumbs.Length?p.liveThumbs[pi]:null;
                              var posterImg=detail.Find("Poster").GetComponent<Image>(); posterImg.sprite=thumb; posterImg.color=thumb!=null?Color.white:new Color(.3f,.26f,.2f,1f);
                              detail.Find("Poster/Label").gameObject.SetActive(thumb==null); detail.Find("Poster/Label").GetComponent<TMP_Text>().text=L(k+"jp")+"\n"+L("live.detail.poster"); }
                            detail.Find("Mood").GetComponent<TMP_Text>().text=L(k+"mood");
                            detail.Find("Scene").GetComponent<TMP_Text>().text=L(k+"scene");
                            detail.Find("SongsRow").gameObject.SetActive(!set);
                            detail.Find("SongsRow/Label").GetComponent<TMP_Text>().text=L("live.detail.songs").Replace("{0}","3")+": "+L("KD_TreatPouch.liveTitleKeys.2")+" / "+L("KD_TreatPouch.liveTitleKeys.3")+" / "+L("KD_TreatPouch.liveTitleKeys.6");
                            detail.Find("FxRow/Label").GetComponent<TMP_Text>().text=L("live.detail.fx")+": "+(set?L("fx.pumpkin"):L("fx.lantern")+"・"+L("fx.meteor")+"・"+L("fx.confetti"))+"\n"+L("live.detail.fxNote");
                            detail.Find("AccRow").gameObject.SetActive(set);
                            detail.Find("AccRow/Label").GetComponent<TMP_Text>().text=L("live.detail.acc")+": "+L("accessory.PumpkinWitchHat")+"・"+L("accessory.PumpkinBrooch")+"・"+L("accessory.PumpkinBatTail")+"\n"+L("live.detail.accNote");
                            detail.Find("Note").GetComponent<TMP_Text>().text=L("live.detail.shared")+"\n"+L(set?"live.detail.limited":"live.detail.buyNoteCe");
                            page.Find("LiveAction/Label").GetComponent<TMP_Text>().text=L("KD_TreatPouch.WordBuyHere");
                            page.Find("LiveBack/Label").GetComponent<TMP_Text>().text=L("KD_TreatPouch.WordAccBack");
                            detail.Find("FxRow/Button/Label").GetComponent<TMP_Text>().text=L("KD_TreatPouch.WordLiveSeeFx");
                            detail.Find("AccRow/Button/Label").GetComponent<TMP_Text>().text=L("KD_TreatPouch.WordLiveSeeAcc");
                            Canvas.ForceUpdateCanvases();
                            foreach(var t in detail.GetComponentsInChildren<TextMeshProUGUI>())
                            {
                                t.isOrthographic=true;
                                var preferred=t.GetPreferredValues(t.text,t.rectTransform.rect.width,Mathf.Infinity);
                                if(preferred.y>t.rectTransform.rect.height+2 || (!t.enableWordWrapping && preferred.x>t.rectTransform.rect.width+2))
                                { overflow++;fit.AppendLine(lang+" live-detail "+variant+" "+Relative(clone.transform,t.transform)+" rect="+t.rectTransform.rect.size+" preferred="+preferred+" text="+t.text); }
                            }
                            Capture("-detail-"+(set?"set":"pack"));
                        }
                    }
                }
                camera.targetTexture=null;
                fit.AppendLine("FIT_OVERFLOWS="+overflow);File.WriteAllText("Temp/hand-menu-scroll-preview-fit.txt",fit.ToString());
            }
            finally{RenderTexture.active=previous;EditorSceneManager.ClosePreviewScene(scene);Object.DestroyImmediate(rt);if(image!=null)Object.DestroyImmediate(image);}
        }
        static string Relative(Transform root,Transform t)
        { string path=t.name;while(t.parent!=root){t=t.parent;path=t.name+"/"+path;}return path; }
}
}
