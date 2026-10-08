using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Collections.Generic;
using TMPro;
using UdonSharp;
using UdonSharpEditor;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using VRC.SDK3.Components;
using Object = UnityEngine.Object;

namespace KominkaDogRun.Editor
{
    /// <summary>IMP-DOG-033. Setup preserves placement; Layout is explicit; Verify never repairs.</summary>
    public static partial class KD_DogBookEditor   // 裏山改変 (DES-DOG-009): partial (KD_DogBookEditor.Hillside.cs = 裏山の入口 Tools/KominkaHillside/DogBook/0〜5)
    {
        public const string BoardName = "KD_DogBook", TagName = "KD_PlayerDogTag";
        const string TextureDir = "Assets/KominkaHillside/Imported/KominkaDogRun/Textures/DogBook";
        // Nicknames use the shared Static font (no TTF in the build). Setup bakes every character
        // KD_DogBook.Filter accepts, so a name can never show a blank glyph.
        public static string NameCharset()
        {
            var sb=new StringBuilder();
            for(int c=0x20;c<=0x7E;c++)if(c!='|')sb.Append((char)c);
            for(int c=0x3041;c<=0x3096;c++)sb.Append((char)c);
            for(int c=0x30A1;c<=0x30F6;c++)sb.Append((char)c);
            sb.Append('\u30FC');
            return sb.ToString();
        }
        const string Menu = "Tools/KominkaHillside/Menu/Imported-Unused/Dog Book/";
        static readonly Vector2 Size = new Vector2(1050,710);
        static readonly string[] Breeds = {"Corgi","Pome","Lab","Dachs","Shiba","Samoyed"};
        static readonly List<TMP_Text> Labels = new List<TMP_Text>();
        static readonly List<string> Keys = new List<string>();
        static KD_LocalizationEditor.Catalog catalog;

        [MenuItem(Menu + "0 - Create Program Assets")]
        public static void Programs()
        {
            foreach (var name in new[]{BoardName,TagName})
            {
                string path = "Assets/KominkaHillside/Imported/KominkaDogRun/Scripts/" + name;
                if (AssetDatabase.LoadAssetAtPath<UdonSharpProgramAsset>(path+".asset") != null) continue;
                var asset = ScriptableObject.CreateInstance<UdonSharpProgramAsset>();
                asset.sourceCsScript = AssetDatabase.LoadAssetAtPath<MonoScript>(path+".cs");
                if (asset.sourceCsScript == null) throw new InvalidOperationException(path);
                AssetDatabase.CreateAsset(asset,path+".asset");
            }
            AssetDatabase.SaveAssets();
        }
        [MenuItem(Menu + "1 - Setup")]
        public static void Setup()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Edit mode only");
            var log = new StringBuilder(); catalog = KD_LocalizationEditor.Load();
            KD_ShopSetupEditor.AddGlyphs(KD_LocalizationEditor.BundledCharacters(catalog),log);
            KD_ShopSetupEditor.AddGlyphs(NameCharset(),log);
            var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(KD_ShopSetupEditor.SharedFontPath);
            if (font == null) throw new InvalidOperationException("Shared UI font missing: " + KD_ShopSetupEditor.SharedFontPath);
            var absent=NameCharset().Where(c=>!KD_LocalizationEditor.HasGlyph(font,c)).ToArray();
            if(absent.Length>0)throw new InvalidOperationException("Shared font lacks name characters: "+new string(absent));
            var nameFont = font;
            var pack = KD_ProductsEditor.SceneComponents<KD_DogPack>().Single();
            var language = KD_ProductsEditor.SceneComponents<KD_Language>().Single();
            var existing = KD_SceneObjects.Find(BoardName);
            var host = existing != null ? existing : new GameObject(BoardName,typeof(RectTransform));
            if(existing == null) host.transform.SetParent(KD_SceneObjects.Find("KD_Places").transform,false);
            var book = host.GetComponent<KD_DogBook>() ?? host.AddUdonSharpComponent<KD_DogBook>();
            UdonSharpEditorUtility.GetBackingUdonBehaviour(book).SyncMethod = VRC.SDKBase.Networking.SyncType.None;
            book.pack=pack;book.language=language;book.hideDistance=4f;
            book.breedKeys=Breeds.Select(k=>"activities.dog."+k).ToArray();
            book.portraits=KD_DogDefs.All.Select(d=>AssetDatabase.LoadAssetAtPath<Texture2D>(TextureDir+"/"+d.key+".png")).ToArray();
            if(book.portraits.Any(t=>t==null))throw new InvalidOperationException("Run Bake Portraits first");
            Labels.Clear();Keys.Clear();
            var index=Canvas(host.transform,"Index",Size);
            book.index=index.parent.gameObject;book.index.SetActive(true);
            Plate(index,KD_UiSpriteEditor.SurfacePanel);
            Label(index,"Heading",new Vector2(0,303),new Vector2(950,60),"dogbook.title",font,36);
            Label(index,"Intro",new Vector2(0,251),new Vector2(950,45),"dogbook.intro",font,24);
            book.cardNames=new TMP_Text[6];book.cardBreeds=new TMP_Text[6];book.cardSurfaces=new Image[6];
            var udon=UdonSharpEditorUtility.GetBackingUdonBehaviour(book);
            for(int i=0;i<6;i++)
            {
                var pos=new Vector2((i%3-1)*328,108-(i/3)*252);
                var button=Button(index,"Dog"+i,pos,new Vector2(306,238),null,font,udon,"_Select"+i);
                book.cardSurfaces[i]=(Image)button.targetGraphic;
                var picture=Child(button.targetGraphic.transform,"Portrait",new Vector2(0,37),new Vector2(190,138)).gameObject;
                var raw=Ensure<RawImage>(picture);raw.texture=book.portraits[i];raw.raycastTarget=false;
                book.cardNames[i]=Label(button.targetGraphic.transform,"Name",new Vector2(0,-54),new Vector2(282,38),null,nameFont,26);
                book.cardNames[i].text=KD_LocalizationEditor.Text(catalog,"ja",book.breedKeys[i]);
                book.cardNames[i].richText=false;
                book.cardBreeds[i]=Label(button.targetGraphic.transform,"Breed",new Vector2(0,-93),new Vector2(290,35),null,font,20);
                book.cardBreeds[i].text="";
            }
            Label(index,"Privacy",new Vector2(0,-314),new Vector2(970,42),"dogbook.private",font,22);
            // IMP-DOG-034: the "?" at the heading's right edge opens one private explanation page.
            var mark=Button(index,"Help",new Vector2(478,303),new Vector2(64,60),"dogbook.help.mark",font,udon,"_OpenHelp");
            mark.targetGraphic.transform.Find("Label").GetComponent<TMP_Text>().fontSize=36;

            var helpPage=Canvas(host.transform,"Help",Size); helpPage.parent.localPosition=new Vector3(0,0,-40);
            Plate(helpPage,KD_UiSpriteEditor.SurfacePanel);
            book.help=helpPage.parent.gameObject;
            Label(helpPage,"Title",new Vector2(0,303),new Vector2(950,60),"dogbook.help.title",font,34);
            var body=Label(helpPage,"Body",new Vector2(0,-2),new Vector2(950,530),"dogbook.help.body",font,23);
            body.enableWordWrapping=true;body.alignment=TextAlignmentOptions.TopLeft;body.overflowMode=TextOverflowModes.Truncate;
            body.paragraphSpacing=60;
            Button(helpPage,"Close",new Vector2(350,-309),new Vector2(275,62),"dogbook.help.close",font,udon,"_CloseHelp").targetGraphic.color=KD_UiSpriteEditor.ActionClose;
            helpPage.parent.gameObject.SetActive(false);

            var detail=Canvas(host.transform,"Detail",Size); detail.parent.localPosition=new Vector3(0,0,-40);
            Plate(detail,KD_UiSpriteEditor.SurfacePanel);
            book.detail=detail.parent.gameObject;
            book.title=Label(detail,"Name",new Vector2(140,267),new Vector2(660,65),null,nameFont,36); book.title.richText=false;
            book.title.text=book.cardNames[0].text;
            book.breed=Label(detail,"Breed",new Vector2(140,207),new Vector2(660,46),null,font,28);
            book.status=Label(detail,"Status",new Vector2(140,133),new Vector2(660,55),null,font,28);
            book.status.text=KD_LocalizationEditor.Text(catalog,"ja","dogbook.state.free");
            var portraitGo=Child(detail,"Portrait",new Vector2(-365,177),new Vector2(240,190)).gameObject;
            book.portrait=Ensure<RawImage>(portraitGo);book.portrait.texture=book.portraits[0];book.portrait.raycastTarget=false;
            Label(detail,"NameLabel",new Vector2(0,39),new Vector2(950,45),"dogbook.name.label",font,28);
            book.nickname=Input(detail,font,nameFont);
            book.saveButton=Button(detail,"Save",new Vector2(351,-147),new Vector2(270,66),"dogbook.save",font,udon,"_SaveName");
            book.saveButton.targetGraphic.color=KD_UiSpriteEditor.ActionPrimary;
            book.favoriteButton=Button(detail,"Favorite",new Vector2(-155,-147),new Vector2(640,66),null,font,udon,"_ToggleFavorite");
            book.favoriteLabel=Label(book.favoriteButton.targetGraphic.transform,"Label",Vector2.zero,new Vector2(614,62),null,font,28);
            book.favoriteLabel.text=KD_LocalizationEditor.Text(catalog,"ja","dogbook.favorite.add");
            book.message=Label(detail,"Message",new Vector2(0,-228),new Vector2(970,66),null,font,24);
            book.message.text=KD_LocalizationEditor.Text(catalog,"ja","dogbook.private");
            Button(detail,"Close",new Vector2(350,-309),new Vector2(275,62),"dogbook.close",font,udon,"_Close").targetGraphic.color=KD_UiSpriteEditor.ActionClose;
            book.staticLabels=Labels.ToArray();book.staticKeys=Keys.ToArray();detail.parent.gameObject.SetActive(false);
            foreach(var c in host.GetComponentsInChildren<Canvas>(true)) if(c.name=="Visual") foreach(var t in c.GetComponentsInChildren<Transform>(true))t.gameObject.layer=5;
            var tagGo=KD_SceneObjects.Find(TagName);
            if(tagGo==null){tagGo=new GameObject(TagName);tagGo.transform.SetParent(KD_SceneObjects.Find("KD_Systems").transform,false);}
            if(tagGo.GetComponent<VRCPlayerObject>()==null)tagGo.AddComponent<VRCPlayerObject>();
            var tag=tagGo.GetComponent<KD_PlayerDogTag>()??tagGo.AddUdonSharpComponent<KD_PlayerDogTag>();tag.pack=pack;
            UdonSharpEditorUtility.GetBackingUdonBehaviour(tag).SyncMethod=VRC.SDKBase.Networking.SyncType.Manual;
            foreach(var w in pack.dogWander){w.dogBook=book;KD_ProductsEditor.Copy(w);}
            log.AppendLine("drop-by: chance "+pack.dogWander[0].dropByChance+" / dog "+pack.dogWander[0].dropByDogInterval+"s / player "+pack.dogWander[0].dropByPlayerInterval+"s / after greeting "+pack.dogWander[0].dropByAfterGreeting+"s");
            KD_LocalizationEditor.Configure(language,catalog,log);
            foreach(var b in new UdonSharpBehaviour[]{book,tag,language})KD_ProductsEditor.Copy(b);
            EditorSceneManager.MarkSceneDirty(host.scene);AssetDatabase.SaveAssets();
            log.AppendLine("six cards, personal detail, six dogs wired; Setup preserves existing placement");
            KD_VerifyLog.Write("Logs/dog-book-setup.txt",log);
        }
        [MenuItem(Menu + "2 - Layout Wall")]
        public static void Layout()
        {
            var root=KD_SceneObjects.Find(BoardName);
            if(root==null)throw new InvalidOperationException("Setup first");
            Undo.RecordObject(root.transform,"Place dog introduction board");
            root.transform.SetPositionAndRotation(new Vector3(-.35f,1.25f,-3.14f),Quaternion.identity);
            root.transform.localScale=Vector3.one*.001f;
            EditorSceneManager.MarkSceneDirty(root.scene);
        }
        static RectTransform Child(Transform parent,string name,Vector2 pos,Vector2 size)
        { return KD_ShopSetupEditor.EnsureUiChild(parent,name,pos,size).GetComponent<RectTransform>(); }
        static T Ensure<T>(GameObject go) where T:Component
        {var c=go.GetComponent<T>();if(c==null)c=go.AddComponent<T>();return c;}
        static RectTransform Canvas(Transform parent,string name,Vector2 size)
        {
            var group=Child(parent,name,Vector2.zero,size);
            var visual=Child(group,"Visual",Vector2.zero,size);visual.gameObject.layer=5;
            Ensure<Canvas>(visual.gameObject).renderMode=RenderMode.WorldSpace;
            var input=Child(group,"Input",Vector2.zero,size);var go=input.gameObject;go.layer=0;
            Ensure<Canvas>(go).renderMode=RenderMode.WorldSpace;
            Ensure<GraphicRaycaster>(go);Ensure<VRCUiShape>(go);
            var collider=Ensure<BoxCollider>(go);collider.size=new Vector3(size.x,size.y,2);collider.isTrigger=true;
            // Block clicks through the open detail, while all visible pixels stay out of automatic photos.
            var blocker=Ensure<Image>(go);blocker.color=Color.clear;blocker.raycastTarget=true;
            return visual;
        }
        static void Plate(RectTransform rect,Color color)
        {var image=Ensure<Image>(rect.gameObject);color.a=1f;image.color=color;image.raycastTarget=false;KD_UiSpriteEditor.ApplyFlatSkin(image);}
        static TextMeshProUGUI Label(Transform parent,string name,Vector2 pos,Vector2 size,string key,TMP_FontAsset font,float points)
        {
            var rect=Child(parent,name,pos,size);var t=Ensure<TextMeshProUGUI>(rect.gameObject);
            t.font=font;t.fontSize=points;t.color=Color.white;t.alignment=TextAlignmentOptions.Center;t.raycastTarget=false;
            t.enableAutoSizing=false;t.enableWordWrapping=false;t.overflowMode=TextOverflowModes.Ellipsis;t.richText=false;
            if(key!=null){t.text=KD_LocalizationEditor.Text(catalog,"ja",key);Labels.Add(t);Keys.Add(key);}
            return t;
        }
        static Button Button(Transform parent,string name,Vector2 pos,Vector2 size,string key,TMP_FontAsset font,VRC.Udon.UdonBehaviour target,string method)
        {
            var rect=Child(parent,name,pos,size);var image=Ensure<Image>(rect.gameObject);
            image.color=KD_UiSpriteEditor.ActionNeutral;image.raycastTarget=true;KD_UiSpriteEditor.ApplyRoundSkin(image,KD_UiSpriteEditor.LoadRound());
            image.raycastTarget=false;
            var hit=Child(parent.parent.Find("Input"),name,pos,size);
            var hitImage=Ensure<Image>(hit.gameObject);hitImage.color=Color.clear;hitImage.raycastTarget=true;
            var b=Ensure<Button>(hit.gameObject);b.targetGraphic=image;b.navigation=new Navigation{mode=Navigation.Mode.None};
            KD_UiSpriteEditor.ApplyButtonFeedback(b);
            while(b.onClick.GetPersistentEventCount()>0)UnityEventTools.RemovePersistentListener(b.onClick,0);
            UnityEventTools.AddStringPersistentListener(b.onClick,target.SendCustomEvent,method);
            if(key!=null)Label(rect,"Label",Vector2.zero,size-new Vector2(24,4),key,font,28);
            return b;
        }
        static TMP_InputField Input(Transform parent,TMP_FontAsset font,TMP_FontAsset nameFont)
        {
            var rect=Child(parent,"Nickname",new Vector2(0,-36),new Vector2(950,78));
            var bg=Ensure<Image>(rect.gameObject);bg.color=KD_UiSpriteEditor.SurfaceRaised;bg.raycastTarget=true;
            KD_UiSpriteEditor.ApplyRoundSkin(bg,KD_UiSpriteEditor.LoadRound());
            bg.raycastTarget=false;
            var hit=Child(parent.parent.Find("Input"),"Nickname",new Vector2(0,-36),new Vector2(950,78));
            var hitImage=Ensure<Image>(hit.gameObject);hitImage.color=Color.clear;hitImage.raycastTarget=true;
            var field=Ensure<TMP_InputField>(hit.gameObject);
            var area=Child(rect,"TextArea",Vector2.zero,new Vector2(914,68));if(area.GetComponent<RectMask2D>()==null)area.gameObject.AddComponent<RectMask2D>();
            var text=Label(area,"Text",Vector2.zero,new Vector2(914,68),null,nameFont,32);text.alignment=TextAlignmentOptions.MidlineLeft;
            var placeholder=Label(area,"Placeholder",Vector2.zero,new Vector2(914,68),"dogbook.name.placeholder",font,26);placeholder.alignment=TextAlignmentOptions.MidlineLeft;
            field.textViewport=area;field.textComponent=text;field.placeholder=placeholder;field.targetGraphic=bg;
            field.contentType=TMP_InputField.ContentType.Standard;field.lineType=TMP_InputField.LineType.SingleLine;
            field.characterLimit=KD_DogBook.NameLimit;field.richText=false;field.onFocusSelectAll=true;field.readOnly=false;field.SetTextWithoutNotify("");
            var udon=UdonSharpEditorUtility.GetBackingUdonBehaviour(parent.parent.parent.GetComponent<KD_DogBook>());
            while(field.onValueChanged.GetPersistentEventCount()>0)UnityEventTools.RemovePersistentListener(field.onValueChanged,0);
            UnityEventTools.AddStringPersistentListener(field.onValueChanged,udon.SendCustomEvent,"_ValidateName");
            return field;
        }
        [MenuItem(Menu + "3 - Verify")]
        public static void Verify()
        {
            var v=new KD_VerifyLog("Dog Book");var books=KD_ProductsEditor.SceneComponents<KD_DogBook>();v.Check(books.Length==1,"one dog book");
            if(books.Length!=1){v.Finish("Logs/dog-book-verify.txt");return;}
            var b=books[0];
            v.Check(b.pack!=null && b.pack.dogWander.Length==6 && b.pack.dogWander.All(w=>w!=null&&w.dogBook==b),"six dogs wired");
            v.Check(b.cardNames.Length==6&&b.cardBreeds.Length==6&&b.portraits.Length==6&&b.portraits.All(t=>t!=null),"six cards / portraits / breed labels");
            v.Check(b.detail!=null&&!b.detail.activeSelf,"detail closed by default");
            v.Check(b.index!=null&&b.index.activeSelf,"six-card index visible by default");
            v.Check(b.help!=null&&!b.help.activeSelf,"help page closed by default");
            var buttons=b.GetComponentsInChildren<Button>(true);
            v.Check(buttons.Count(x=>x.onClick.GetPersistentEventCount()==1&&x.onClick.GetPersistentMethodName(0)=="SendCustomEvent"&&PersistentArg(x)=="_OpenHelp")==1,"one ? button opens help");
            v.Check(buttons.Count(x=>x.onClick.GetPersistentEventCount()==1&&PersistentArg(x)=="_CloseHelp")==1,"help page has one close");
            v.Check(b.pack!=null&&b.pack.dogWander.All(w=>w.dropByChance>0f&&w.dropByChance<=1f&&w.dropByDogInterval>=45f&&w.dropByPlayerInterval>=w.dropByDogInterval&&w.dropByAfterGreeting>=45f),"drop-by intervals: chance in (0,1], dog >= 45s, player >= dog, after greeting >= 45s");
            v.Check(b.nickname!=null&&!b.nickname.richText&&b.nickname.lineType==TMP_InputField.LineType.SingleLine,"literal single-line nickname input");
            v.Check(b.staticLabels.Length==b.staticKeys.Length,"localized static labels");
            // 裏山改変 (DES-DOG-009): 裏山の Setup で組んだ板 (KD_DogBookEditor.Hillside.cs が判定) だけ置き場所を partial で見る。
            // それ以外 (古民家の Setup の板) は元の「KD_Places の下」のまま (KD_Places が無ければ FAIL。元は例外)
            bool hillside=false; ClassifyBoard(b,ref hillside);
            if(hillside) VerifyPlacement(v,b);
            else { var places=KD_SceneObjects.Find("KD_Places"); v.Check(places!=null&&b.transform.parent==places.transform,"board under places"); }
            v.Check(Mathf.Abs(b.transform.lossyScale.x-.001f)<.00001f,"1050px = 1.05m width");
            var tags=KD_ProductsEditor.SceneComponents<KD_PlayerDogTag>();
            v.Check(tags.Length==1&&tags[0].GetComponent<VRCPlayerObject>()!=null&&tags[0].pack==b.pack,"one player-object template with pack");
            if(tags.Length==1)v.Check(UdonSharpEditorUtility.GetBackingUdonBehaviour(tags[0]).SyncMethod==VRC.SDKBase.Networking.SyncType.Manual,"visit tag manual synchronization");
            foreach(var canvas in b.GetComponentsInChildren<Canvas>(true))
                v.Check(canvas.name=="Visual" ? canvas.gameObject.layer==5 : canvas.gameObject.layer==0&&canvas.GetComponent<VRCUiShape>()!=null&&canvas.GetComponent<BoxCollider>()!=null,"drawing / pointer surface "+canvas.name);
            v.Check(b.GetComponentsInChildren<Graphic>(true).All(g=>g.GetComponentInParent<Canvas>(true)!=null&&(g.GetComponentInParent<Canvas>(true).gameObject.layer==5||g.color.a==0)),"visible pixels excluded from automatic photos");
            foreach(var button in b.GetComponentsInChildren<Button>(true))v.Check(button.onClick.GetPersistentEventCount()==1&&button.onClick.GetPersistentTarget(0)!=null,"button wired "+button.name);
            var font=AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(KD_ShopSetupEditor.SharedFontPath);
            v.Check(font!=null&&b.nickname.textComponent.font==font&&b.title.font==font&&b.cardNames.All(t=>t!=null&&t.font==font)&&b.staticLabels.All(t=>t.font==font),"every label including names uses the shared UI font");
            v.Check(font!=null&&font.atlasPopulationMode==AtlasPopulationMode.Static&&font.sourceFontFile==null,"shared UI font stays Static without a source TTF (no font file in the build)");
            v.Check(AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/KominkaHillside/Font/KD_NotoSerif Nickname SDF.asset")==null,"no dedicated nickname font asset remains");
            v.Check(font!=null&&NameCharset().All(c=>KD_LocalizationEditor.HasGlyph(font,c)),"every accepted name character has a glyph (ASCII / hiragana / katakana / prolonged mark)");
            v.Check(b.nickname.characterLimit==KD_DogBook.NameLimit,"input limit equals the name limit");
            v.Check(b.nickname.onValueChanged.GetPersistentEventCount()==1&&b.nickname.onValueChanged.GetPersistentMethodName(0)=="SendCustomEvent","input validates as typed");
            catalog=KD_LocalizationEditor.Load();
            foreach(var lang in catalog.languages.Where(KD_LocalizationEditor.Bundled))
                foreach(var e in lang.texts.Where(t=>t.key.StartsWith("dogbook.")||t.key=="KD_ShopKeeper.InfoDogs"))
                    v.Check(e.text.Where(c=>!char.IsControl(c)).All(c=>KD_LocalizationEditor.HasGlyph(font,c)),"glyphs "+lang.code+" "+e.key);
            v.Check(b.CleanName("  ぽん|た\n  ")=="ぽんた","nickname strips delimiter and newline");
            var bodyText=b.help!=null?b.help.transform.Find("Visual/Body")?.GetComponent<TMP_Text>():null;
            v.Check(bodyText!=null&&bodyText.enableWordWrapping,"help body wraps");
            if(bodyText!=null)
            {
                // Measure with the page active: an inactive canvas can report a fit that Play then truncates.
                string keep=bodyText.text;bool wasActive=b.help.activeSelf;b.help.SetActive(true);UnityEngine.Canvas.ForceUpdateCanvases();
                foreach(var lang in catalog.languages.Where(l=>!KD_LocalizationEditor.Bundled(l)))v.Check(true,"CLIENT_UNMEASURED help body "+lang.code+" (verify on device)");
                foreach(var lang in catalog.languages.Where(KD_LocalizationEditor.Bundled)) // client 言語の高さは Editor で測れない (IMP-INF-010)
                {
                    bodyText.text=KD_LocalizationEditor.Text(catalog,lang.code,"dogbook.help.body");UnityEngine.Canvas.ForceUpdateCanvases();bodyText.ForceMeshUpdate(true,true);
                    v.Check(!bodyText.isTextTruncated&&bodyText.preferredHeight<=bodyText.rectTransform.rect.height,"help body fits "+lang.code+" ("+Mathf.RoundToInt(bodyText.preferredHeight)+"/"+Mathf.RoundToInt(bodyText.rectTransform.rect.height)+"px)");
                }
                bodyText.text=keep;bodyText.ForceMeshUpdate(true,true);b.help.SetActive(wasActive);
            }
            v.Check(b.CleanName("abcdefghijklmn")=="abcdefghijkl","nickname length limit");
            v.Check(b.CleanName("ぽん太Ａ1 ")=="ぽんA1","nickname drops kanji and folds full-width ASCII");
            v.Check(b.CleanName("ｱｲ😀🐶ヴォルフ")=="ヴォルフ","nickname drops half-width kana and emoji");
            if(hillside) VerifyHillside(v,b);    // 裏山改変 (DES-DOG-009): 見出し・状態の文言・毛色の行と KH_FurColor (KD_DogBookEditor.Hillside.cs)
            v.Finish("Logs/dog-book-verify.txt");
        }
        // 裏山改変 (DES-DOG-009): Verify の差し込み口。本体は KD_DogBookEditor.Hillside.cs (無ければ hillside=false で古民家の検査だけになる)
        static partial void ClassifyBoard(KD_DogBook b, ref bool hillside);
        static partial void VerifyPlacement(KD_VerifyLog v, KD_DogBook b);
        static partial void VerifyHillside(KD_VerifyLog v, KD_DogBook b);
        static string PersistentArg(Button x)
        {
            var so=new SerializedObject(x);
            var call=so.FindProperty("m_OnClick.m_PersistentCalls.m_Calls").GetArrayElementAtIndex(0);
            return call.FindPropertyRelative("m_Arguments.m_StringArgument").stringValue;
        }
        [MenuItem(Menu + "4 - Bake Portraits")]
        public static void Portraits()
        {
            Directory.CreateDirectory(TextureDir);
            var pouch=KD_ProductsEditor.SceneComponents<KD_TreatPouch>().Single();
            for(int i=0;i<KD_DogDefs.All.Length;i++)
            {
                var preview=new PreviewRenderUtility();GameObject clone=null;Texture2D texture=null;
                try
                {
                    clone=Object.Instantiate(pouch.accPreviewDogs[i]);clone.transform.SetPositionAndRotation(Vector3.zero,Quaternion.Euler(0,35,0));clone.transform.localScale=Vector3.one;
                    clone.SetActive(true);preview.AddSingleGO(clone);
                    var renderers=clone.GetComponentsInChildren<Renderer>().Where(r=>r.enabled).ToArray();
                    Bounds bounds=renderers[0].bounds;foreach(var r in renderers)bounds.Encapsulate(r.bounds);
                    preview.camera.orthographic=true;preview.camera.orthographicSize=Mathf.Max(bounds.extents.y,bounds.extents.x*.75f)*1.18f;
                    preview.camera.transform.position=bounds.center+new Vector3(0,bounds.extents.y*.12f,-5);preview.camera.transform.LookAt(bounds.center);
                    preview.camera.nearClipPlane=.01f;preview.camera.farClipPlane=20;
                    preview.camera.clearFlags=CameraClearFlags.SolidColor;preview.camera.backgroundColor=new Color(.25f,.28f,.23f,1);
                    preview.BeginStaticPreview(new Rect(0,0,384,288));preview.camera.Render();texture=preview.EndStaticPreview();
                    string path=TextureDir+"/"+KD_DogDefs.All[i].key+".png";File.WriteAllBytes(path,texture.EncodeToPNG());
                    AssetDatabase.ImportAsset(path);var importer=(TextureImporter)AssetImporter.GetAtPath(path);importer.mipmapEnabled=true;importer.maxTextureSize=512;importer.textureCompression=TextureImporterCompression.Compressed;importer.SaveAndReimport();
                }
                finally{if(texture!=null)Object.DestroyImmediate(texture);preview.Cleanup();}
            }
        }

        // Editor-only presentation check. Restore every previewed value; never copy preview names to Udon.
        public static void CapturePages()
        {
            const string output="docs/prototypes/dog-board-location-2026-09-16/";
            var b=KD_ProductsEditor.SceneComponents<KD_DogBook>().Single();
            var texts=b.GetComponentsInChildren<TMP_Text>(true);var strings=texts.Select(t=>t.text).ToArray();
            bool indexActive=b.index.activeSelf,detailActive=b.detail.activeSelf,helpActive=b.help!=null&&b.help.activeSelf;string draft=b.nickname.text;
            catalog=KD_LocalizationEditor.Load();Directory.CreateDirectory(output);
            try
            {
                foreach(string code in new[]{"ja","en"})
                {
                    for(int i=0;i<b.staticLabels.Length;i++)b.staticLabels[i].text=KD_LocalizationEditor.Text(catalog,code,b.staticKeys[i]);
                    for(int i=0;i<6;i++){b.cardNames[i].text=KD_LocalizationEditor.Text(catalog,code,b.breedKeys[i]);b.cardBreeds[i].text="";}
                    b.index.SetActive(true);b.detail.SetActive(false);UnityEngine.Canvas.ForceUpdateCanvases();
                    KD_DisplayGuidanceEditor.RenderPanel(b.index.transform,"09-"+code+"-index",output);
                    b.index.SetActive(false);b.detail.SetActive(true);
                    b.title.text=code=="ja"?"ぽんた":"Pon";b.breed.text=KD_LocalizationEditor.Text(catalog,code,b.breedKeys[0]);
                    b.nickname.SetTextWithoutNotify(b.title.text);
                    b.status.text=KD_LocalizationEditor.Text(catalog,code,"dogbook.state.free");
                    b.favoriteLabel.text=KD_LocalizationEditor.Text(catalog,code,"dogbook.favorite.add");
                    b.message.text=KD_LocalizationEditor.Text(catalog,code,"dogbook.private");
                    UnityEngine.Canvas.ForceUpdateCanvases();
                    KD_DisplayGuidanceEditor.RenderPanel(b.detail.transform,"10-"+code+"-detail",output);
                    if(b.help!=null)
                    {
                        b.detail.SetActive(false);b.help.SetActive(true);UnityEngine.Canvas.ForceUpdateCanvases();
                        KD_DisplayGuidanceEditor.RenderPanel(b.help.transform,"11-"+code+"-help",output);
                    }
                }
            }
            finally
            {
                b.nickname.SetTextWithoutNotify(draft);
                for(int i=0;i<texts.Length;i++)texts[i].text=strings[i];
                b.index.SetActive(indexActive);b.detail.SetActive(detailActive);if(b.help!=null)b.help.SetActive(helpActive);UnityEngine.Canvas.ForceUpdateCanvases();
            }
        }
    }
}
