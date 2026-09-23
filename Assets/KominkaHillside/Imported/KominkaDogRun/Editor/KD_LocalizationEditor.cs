using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using TMPro;
using UdonSharp;
using UdonSharpEditor;
using UnityEditor;
using UnityEngine;
using VRC.Udon;

namespace KominkaDogRun.Editor
{
    public static class KD_LocalizationEditor
    {
        public const string BindingsPath = "Assets/KominkaHillside/Imported/KominkaDogRun/Data/UILocalizationBindings.json";
        [Serializable] public class TextEntry { public string key; public string text; }
        /// <summary>glyphs: "self" は共通フォントへ字形を焼く言語、"client" は VRChat 内蔵 fallback に描画を委ねる言語 (IMP-INF-010)。
        /// aliases: 任意。VRChat の `GetCurrentLanguage()` が返しうる別表記 (zh-hk に対する zh-tw / zh-hant) を小文字で並べる。
        /// 省略時は JsonUtility が null か空配列にするのでどちらも「別名なし」として扱う。</summary>
        [Serializable] public class Language { public string code; public string displayName; public string glyphs; public string[] aliases; public TextEntry[] texts; }
        public const string GlyphsSelf="self",GlyphsClient="client";
        /// <summary>言語コードと別名の書式 (小文字の RFC 5646 相当)。check-localization.js と同じ規則。</summary>
        public const string CodePattern=@"^[a-z]{2,3}(-[a-z0-9]{2,8})*$";
        [Serializable] public class Catalog { public int schemaVersion; public string defaultLanguage; public string fallbackLanguage; public Language[] languages; }
        [Serializable] public class Binding { public string root; public string path; public string key; }
        [Serializable] public class Names { public string root; public string field; public string[] keys; }
        [Serializable] public class Manifest { public Binding[] labels; public Binding[] interactions; public Binding[] pickups; public Names[] names; }
        public static Catalog Load() { var c=JsonUtility.FromJson<Catalog>(File.ReadAllText(KD_MenuFinishEditor.CatalogPath)); Validate(c); return c; }
        public static void Validate(Catalog c)
        {
            if(c==null || c.schemaVersion!=2 || c.languages==null || c.languages.Length==0)throw new InvalidOperationException("Localization schemaVersion 2 and at least one language required");
            var codes=new HashSet<string>();
            foreach(var lang in c.languages)
            {
                if(lang==null || string.IsNullOrWhiteSpace(lang.displayName) || string.IsNullOrEmpty(lang.code) || !Regex.IsMatch(lang.code,CodePattern) || !codes.Add(lang.code) || lang.texts==null)
                    throw new InvalidOperationException("Invalid/duplicate language code or missing display name");
                if(lang.glyphs!=GlyphsSelf && lang.glyphs!=GlyphsClient)throw new InvalidOperationException("glyphs must be \"self\" or \"client\": "+lang.code);
                var keys=new HashSet<string>();
                foreach(var e in lang.texts)
                {
                    if(e==null || string.IsNullOrWhiteSpace(e.key) || !keys.Add(e.key))throw new InvalidOperationException("Invalid/duplicate key in "+lang.code);
                    // 空白だけの訳は表示が消えるのに未訳にも数えられない。空 (未訳) か本文のどちらかにする
                    if(e.text!=null && e.text.Length>0 && string.IsNullOrWhiteSpace(e.text))throw new InvalidOperationException("Whitespace-only text "+lang.code+":"+e.key);
                }
            }
            if(!codes.Contains(c.defaultLanguage)||!codes.Contains(c.fallbackLanguage))throw new InvalidOperationException("Default/fallback language is not registered");
            // 別名は全言語のコードを集め終わってから見る (後ろの言語のコードと衝突する別名も拒否する)
            var taken=new HashSet<string>(codes);
            foreach(var lang in c.languages)
            {
                if(lang.aliases==null)continue;
                foreach(var alias in lang.aliases)
                    if(string.IsNullOrEmpty(alias) || !Regex.IsMatch(alias,CodePattern) || !taken.Add(alias))
                        throw new InvalidOperationException("Invalid/duplicate language alias in "+lang.code+": "+alias);
            }
            // 既定・代替言語の文は他言語の未訳時にも描かれるので、内蔵 fallback 頼みにしない
            foreach(var code in new[]{c.defaultLanguage,c.fallbackLanguage})if(!Bundled(Array.Find(c.languages,l=>l.code==code)))throw new InvalidOperationException("Default/fallback language must bundle glyphs (glyphs=self): "+code);
            var fallback=Array.Find(c.languages,l=>l.code==c.fallbackLanguage);
            if(fallback.texts.Length==0 || fallback.texts.Any(e=>string.IsNullOrWhiteSpace(e.text)))throw new InvalidOperationException("Fallback language must have every text");
            var baseKeys=new HashSet<string>(fallback.texts.Select(e=>e.key));
            foreach(var lang in c.languages)foreach(var e in lang.texts)
            {
                if(!baseKeys.Contains(e.key))throw new InvalidOperationException("Unknown key "+lang.code+":"+e.key);
                if(!string.IsNullOrEmpty(e.text))
                {
                    var expected=Placeholders(Array.Find(fallback.texts,x=>x.key==e.key).text);
                    if(!expected.SetEquals(Placeholders(e.text)))throw new InvalidOperationException("Placeholder mismatch "+lang.code+":"+e.key);
                }
            }
        }
        static HashSet<string> Placeholders(string text) { return new HashSet<string>(Regex.Matches(text,@"\{\d+\}").Cast<Match>().Select(m=>m.Value)); }
        public static string Text(Catalog c,string code,string key)
        {
            var language=Array.Find(c.languages,l=>l.code==code);
            var e=language==null?null:Array.Find(language.texts,t=>t.key==key);
            if(e!=null && !string.IsNullOrEmpty(e.text))return e.text;
            var fallback=Array.Find(c.languages,l=>l.code==c.fallbackLanguage);
            e=Array.Find(fallback.texts,t=>t.key==key);
            if(e==null)throw new InvalidOperationException("Unknown localization key: "+key);
            return e.text;
        }
        public static bool Bundled(Language l) { return l!=null && l.glyphs==GlyphsSelf; }
        /// <summary>文言表の aliases を実行時の 2 本の配列 (別名 → 正規コード) へ展開する。Configure と ValidateScene が同じ結果を使う。
        /// 並びは languages の順、各言語の中は aliases の順。null と空配列はどちらも「別名なし」。</summary>
        public static void AliasTable(Catalog c,out string[] aliasCodes,out string[] aliasTargets)
        {
            var codes=new List<string>();var targets=new List<string>();
            foreach(var lang in c.languages)
            {
                if(lang.aliases==null)continue;
                foreach(var alias in lang.aliases){codes.Add(alias);targets.Add(lang.code);}
            }
            aliasCodes=codes.ToArray();aliasTargets=targets.ToArray();
        }
        /// <summary>描画対象の文字か (制御文字と ZWSP を除く)。字形の検査・焼き込みはこの述語で揃える。</summary>
        public static bool Drawn(char ch) { return !char.IsControl(ch) && ch!='\u200b'; }
        /// <summary>共通フォントへ焼く文字 = glyphs=self の言語の表示名と全文言。client 言語は含めない。
        /// アトラスの容量と収録数は AddGlyphs が毎回ログに出す (vrc-tmp-font §1)。満杯になると既存字まで missing で返る。</summary>
        public static string BundledCharacters(Catalog c)
        { var s=new StringBuilder();foreach(var lang in c.languages){if(!Bundled(lang))continue;s.Append(lang.displayName);foreach(var e in lang.texts)s.Append(e.text);}return s.ToString(); }
        /// <summary>self 言語の文字が共通フォント (fallback 表込み) に無ければ止める。client 言語は検査しない (実機で内蔵 fallback を確認する)。</summary>
        public static void ValidateGlyphs(Catalog c,TMP_FontAsset font)
        {
            if(font==null)throw new InvalidOperationException("Shared UI font is not wired (KD_Language.pouch.sectionTitleLabel). Run Menu Finish/2 - Setup.");
            foreach(var lang in c.languages)
            {
                if(!Bundled(lang))continue;
                foreach(char character in lang.displayName+string.Concat(lang.texts.Select(e=>e.text)))
                    if(Drawn(character) && !HasGlyph(font,character))throw new InvalidOperationException("Localization font missing U+"+((int)character).ToString("X4")+" in "+lang.code);
            }
        }
        /// <summary>VRChat クライアント UI だけが描くキー (interactions / pickups の InteractionText で、TMP ラベルには接続されていないもの)。
        /// ワールドの TMP フォントとは無関係なので字形の一覧から外す。ラベルにも使うキー (例: KD_PhotoViewer.InteractWord) は残す。</summary>
        static HashSet<string> PromptOnlyKeys()
        {
            var m=JsonUtility.FromJson<Manifest>(File.ReadAllText(BindingsPath));
            var labels=new HashSet<string>((m.labels??Array.Empty<Binding>()).Select(b=>b.key));
            return new HashSet<string>((m.interactions??Array.Empty<Binding>()).Concat(m.pickups??Array.Empty<Binding>()).Select(b=>b.key).Where(k=>!labels.Contains(k)));
        }
        /// <summary>TMP が描く文字列 = リッチテキストタグ (<color> <size> <b> 等) を除いた本文。</summary>
        static string Visible(string text) { return Regex.Replace(text??"","<[^<>]+>",""); }
        static IEnumerable<string> DisplayedTexts(Language lang,HashSet<string> promptOnly)
        { yield return Visible(lang.displayName);foreach(var e in lang.texts)if(!promptOnly.Contains(e.key))yield return Visible(e.text); }
        /// <summary>client 言語 1 つについて、共通フォントに無い文字 (= 内蔵 fallback が描く文字) を重複なしで返す。実機確認の対象一覧。self 言語は空。</summary>
        public static string ClientGlyphs(Catalog c,TMP_FontAsset font,string code)
        {
            var lang=Array.Find(c.languages,l=>l.code==code);
            if(lang==null || Bundled(lang))return "";
            var seen=new HashSet<char>();var s=new StringBuilder();
            foreach(string text in DisplayedTexts(lang,PromptOnlyKeys()))foreach(char ch in text)
                if(Drawn(ch) && seen.Add(ch) && !HasGlyph(font,ch))s.Append(ch);
            return s.ToString();
        }
        /// <summary>client 言語の混在の見え方: 共通フォントで描く字数 / 内蔵 fallback で描く字数 / 1 語の中で両方が混ざる語。
        /// 字形解決は文字単位なので、ASCII・数字・JP 由来の漢字は共通フォント (明朝)、それ以外はクライアント側フォントで描かれる。実機で見る箇所を絞るための一覧。</summary>
        public static string ClientGlyphReport(Catalog c,TMP_FontAsset font,string code)
        {
            var lang=Array.Find(c.languages,l=>l.code==code);
            if(lang==null || Bundled(lang))return "";
            var bundled=new HashSet<char>();var client=new HashSet<char>();var mixed=new List<string>();int mixedTotal=0;
            foreach(string text in DisplayedTexts(lang,PromptOnlyKeys()))
                foreach(string word in text.Split(new[]{' ','\n','\t'},StringSplitOptions.RemoveEmptyEntries))
                {
                    bool hasBundled=false,hasClient=false;
                    foreach(char ch in word)
                    {
                        if(!Drawn(ch))continue;
                        if(HasGlyph(font,ch)){bundled.Add(ch);if(char.IsLetterOrDigit(ch))hasBundled=true;}
                        else{client.Add(ch);hasClient=true;}
                    }
                    if(hasBundled && hasClient && !mixed.Contains(word)) { mixedTotal++; if(mixed.Count<20)mixed.Add(word); }
                }
            return "bundled="+bundled.Count+" client="+client.Count+" mixedWords="+mixedTotal+(mixed.Count==0?"":" [first "+mixed.Count+": "+string.Join(" ",mixed)+"]");
        }
        /// <summary>全キーが埋まっていない言語 (未訳あり)。部分訳の試験言語を公開ビルドへ混ぜないためのガードが使う。</summary>
        public static string[] IncompleteLanguages(Catalog c)
        {
            var fallback=Array.Find(c.languages,l=>l.code==c.fallbackLanguage);
            var result=new List<string>();
            foreach(var lang in c.languages)
            {
                int missing=0;
                foreach(var key in fallback.texts.Select(e=>e.key)){var e=Array.Find(lang.texts,t=>t.key==key);if(e==null||string.IsNullOrWhiteSpace(e.text))missing++;}
                if(missing>0)result.Add(lang.code+"("+missing+")");
            }
            return result.ToArray();
        }
        /// <summary>ギャラリーの文字表示数 = 列ラベル (カテゴリ数) + Caption 1。</summary>
        public const int GallerySigns = KD_PhotoDirector.GroupCount + 1;
        public static bool HasGlyph(TMP_FontAsset font,char character)
        {
            var visited=new HashSet<TMP_FontAsset>();var pending=new Stack<TMP_FontAsset>();if(font!=null)pending.Push(font);
            while(pending.Count>0)
            {
                var current=pending.Pop();if(!visited.Add(current))continue;
                if(current.HasCharacter(character))return true;
                if(current.fallbackFontAssetTable!=null)foreach(var f in current.fallbackFontAssetTable)if(f!=null)pending.Push(f);
            }
            return false;
        }
        public static void Configure(KD_Language target,Catalog c,StringBuilder log)
        {
            KD_PhotoSetupEditor.RemoveLegacyGalleryReferences(target);
            var fallback=Array.Find(c.languages,l=>l.code==c.fallbackLanguage);
            target.keys=fallback.texts.Select(e=>e.key).ToArray();target.languageCodes=c.languages.Select(l=>l.code).ToArray();target.languageNames=c.languages.Select(l=>l.displayName).ToArray();
            target.defaultLanguage=c.defaultLanguage;target.fallbackLanguage=c.fallbackLanguage;target.texts=new string[target.keys.Length*c.languages.Length];
            AliasTable(c,out var aliasCodes,out var aliasTargets);target.languageAliasCodes=aliasCodes;target.languageAliasTargets=aliasTargets;
            if(aliasCodes.Length>0)log.AppendLine("Language aliases: "+aliasCodes.Length+" ["+string.Join(" ",aliasCodes.Select((a,i)=>a+"="+aliasTargets[i]))+"]");
            typeof(KD_Language).GetField("_lookup",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).SetValue(target,null);
            var font=target.pouch?.sectionTitleLabel?.font;
            for(int l=0;l<c.languages.Length;l++)
            {
                int missing=0;
                for(int k=0;k<target.keys.Length;k++)
                {
                    var e=Array.Find(c.languages[l].texts,t=>t.key==target.keys[k]);
                    target.texts[l*target.keys.Length+k]=e==null?"":e.text;
                    if(e==null||string.IsNullOrEmpty(e.text))missing++;
                }
                // client 言語の一覧は参考値 (焼く前に数える)。正は Menu Finish/3 - Verify の CLIENT_GLYPHS 行
                string clientGlyphs=font==null||Bundled(c.languages[l])?null:ClientGlyphs(c,font,c.languages[l].code);
                log.AppendLine("Language "+c.languages[l].code+": keys="+target.keys.Length+" fallback="+missing+" glyphs="+c.languages[l].glyphs+(Bundled(c.languages[l])?" (baked)":"")
                    +(clientGlyphs==null?"":" clientGlyphs="+clientGlyphs.Length+(clientGlyphs.Length==0?"":" ["+clientGlyphs+"]")));
            }
        }
        public static Transform Resolve(Binding b)
        {
            var root=KD_SceneObjects.Find(b.root,out int count);
            if(root==null||count!=1)throw new InvalidOperationException("Localization anchor missing/ambiguous: "+b.root);
            var t=string.IsNullOrEmpty(b.path)?root.transform:root.transform.Find(b.path);
            if(t==null)throw new InvalidOperationException("Localization target missing: "+b.root+"/"+b.path);
            return t;
        }
        public static void ValidateScene(KD_Language target,Catalog c)
        {
            Validate(c);
            var fallback=Array.Find(c.languages,l=>l.code==c.fallbackLanguage);
            AliasTable(c,out var aliasCodes,out var aliasTargets);
            if(target!=null && (!(target.languageAliasCodes??Array.Empty<string>()).SequenceEqual(aliasCodes) || !(target.languageAliasTargets??Array.Empty<string>()).SequenceEqual(aliasTargets)))
                throw new InvalidOperationException("Localization aliases differ from catalog. Run Menu Finish/2 - Setup.");
            if(target==null || target.languageCodes==null || !target.languageCodes.SequenceEqual(c.languages.Select(l=>l.code))
                || !target.languageNames.SequenceEqual(c.languages.Select(l=>l.displayName)) || !target.keys.SequenceEqual(fallback.texts.Select(e=>e.key))
                || target.defaultLanguage!=c.defaultLanguage || target.fallbackLanguage!=c.fallbackLanguage
                || target.texts.Length!=target.keys.Length*c.languages.Length)
                throw new InvalidOperationException("Localization tables differ from catalog. Run Menu Finish/2 - Setup.");
            for(int l=0;l<c.languages.Length;l++)
            {
                for(int k=0;k<target.keys.Length;k++)
                {
                    var entry=Array.Find(c.languages[l].texts,t=>t.key==target.keys[k]);
                    if((target.texts[l*target.keys.Length+k]??"")!=(entry==null?"":entry.text??""))throw new InvalidOperationException("Stale localization text: "+c.languages[l].code+":"+target.keys[k]);
                }
            }
            var manifest=JsonUtility.FromJson<Manifest>(File.ReadAllText(BindingsPath));
            if(target.fixedLabels.Length!=manifest.labels.Length || target.fixedKeys.Length!=manifest.labels.Length || target.interactTargets.Length!=manifest.interactions.Length || target.interactKeys.Length!=manifest.interactions.Length)
                throw new InvalidOperationException("Localization bindings differ from manifest");
            for(int i=0;i<manifest.labels.Length;i++)if(target.fixedKeys[i]!=manifest.labels[i].key || target.fixedLabels[i]!=Resolve(manifest.labels[i]).GetComponent<TMP_Text>())throw new InvalidOperationException("Stale text binding "+i);
            // 裏山改変: 写真 (ギャラリー) は裏山で未採用。写真の器があるときだけ検査する
            if(KD_SceneObjects.Find(KD_DogDefs.PhotoSystemName)!=null)KD_PhotoSetupEditor.ValidateGalleryText(target);
            for(int i=0;i<manifest.interactions.Length;i++)if(target.interactKeys[i]!=manifest.interactions[i].key || target.interactTargets[i]!=Resolve(manifest.interactions[i]).GetComponent<UdonBehaviour>())throw new InvalidOperationException("Stale prompt binding "+i);
            var pickups = manifest.pickups ?? Array.Empty<Binding>();
            if (target.pickupTargets == null || target.pickupKeys == null || target.pickupTargets.Length != pickups.Length || target.pickupKeys.Length != pickups.Length)
                throw new InvalidOperationException("Pickup bindings differ from manifest");
            for (int i = 0; i < pickups.Length; i++)
                if (target.pickupKeys[i] != pickups[i].key || target.pickupTargets[i] != Resolve(pickups[i]).GetComponent<VRC.SDKBase.VRC_Pickup>())
                    throw new InvalidOperationException("Stale pickup binding " + i);
            foreach(var n in manifest.names)
            {
                var root=Resolve(new Binding{root=n.root});bool found=false;
                foreach(var component in root.GetComponents<UdonSharpBehaviour>())
                {
                    var field=component.GetType().GetField(n.field);if(field==null)continue;
                    var value=field.GetValue(component);found=field.FieldType==typeof(string)?(string)value==n.keys[0]:value!=null && ((string[])value).SequenceEqual(n.keys);break;
                }
                if(!found)throw new InvalidOperationException("Stale name binding "+n.root+":"+n.field);
            }
            ValidateGlyphs(c,target.pouch?.sectionTitleLabel?.font);
        }
        public static void Bind(KD_Language target,Catalog c,StringBuilder log)
        {
            var m=JsonUtility.FromJson<Manifest>(File.ReadAllText(BindingsPath));
            target.fixedLabels=new TMP_Text[m.labels.Length];target.fixedKeys=new string[m.labels.Length];
            var seen=new HashSet<TMP_Text>();
            for(int i=0;i<m.labels.Length;i++)
            {
                var b=m.labels[i];var label=Resolve(b).GetComponent<TMP_Text>();
                if(label==null||!seen.Add(label))throw new InvalidOperationException("Duplicate/missing text component: "+b.root+"/"+b.path);
                target.fixedLabels[i]=label;target.fixedKeys[i]=b.key;label.text=Text(c,c.fallbackLanguage,b.key);
            }
            target.interactTargets=new UdonBehaviour[m.interactions.Length];target.interactKeys=new string[m.interactions.Length];
            for(int i=0;i<m.interactions.Length;i++)
            {
                var b=m.interactions[i];var udon=Resolve(b).GetComponent<UdonBehaviour>();
                if(udon==null)throw new InvalidOperationException("Missing interaction component: "+b.root);
                target.interactTargets[i]=udon;target.interactKeys[i]=b.key;udon.interactText=Text(c,c.fallbackLanguage,b.key);EditorUtility.SetDirty(udon);
            }
            var pickups = m.pickups ?? Array.Empty<Binding>();
            target.pickupTargets = new VRC.SDKBase.VRC_Pickup[pickups.Length]; target.pickupKeys = new string[pickups.Length];
            var seenPickups = new HashSet<VRC.SDKBase.VRC_Pickup>();
            for (int i = 0; i < pickups.Length; i++)
            {
                var b = pickups[i]; var pickup = Resolve(b).GetComponent<VRC.SDKBase.VRC_Pickup>();
                if (pickup == null || !seenPickups.Add(pickup)) throw new InvalidOperationException("Duplicate/missing pickup: " + b.root);
                target.pickupTargets[i] = pickup; target.pickupKeys[i] = b.key;
                pickup.InteractionText = Text(c, c.fallbackLanguage, b.key); EditorUtility.SetDirty(pickup);
            }
            foreach(var n in m.names)
            {
                var root=Resolve(new Binding{root=n.root,path=""});UdonSharpBehaviour owner=null;
                foreach(var component in root.GetComponents<UdonSharpBehaviour>())if(component.GetType().GetField(n.field)!=null){owner=component;break;}
                if(owner==null)throw new InvalidOperationException("Name key field missing: "+n.root+"."+n.field);
                foreach(string key in n.keys)if(!string.IsNullOrEmpty(key))Text(c,c.fallbackLanguage,key);
                var field=owner.GetType().GetField(n.field);field.SetValue(owner,field.FieldType==typeof(string)?(object)n.keys[0]:n.keys);
                EditorUtility.SetDirty(owner);UdonSharpEditorUtility.CopyProxyToUdon(owner);
            }
            if(target.choiceLabel!=null)target.choiceLabel.text=Text(c,c.fallbackLanguage,"language.selection").Replace("{0}",Array.Find(c.languages,l=>l.code==c.fallbackLanguage).displayName);
            log.AppendLine("Stable bindings: labels="+m.labels.Length+" prompts="+m.interactions.Length+" pickups="+pickups.Length+" name groups="+m.names.Length);
            // 自動縮小は Bind の直後の共通処理にする。Bind を呼ぶ Setup は 19 か所あり、
            // 呼び出し側へ 1 行ずつ足すと足し忘れた面だけ枠溢れが残る (Menu Finish/2 と RebuildIfPresent も同じ経路を通る)
            ApplyButtonAutoSize(target,log);
        }
        /// <summary>短い 1 行ボタンのラベルは自動縮小の下限 14pt まで縮める (Quest・スマホの可読性の線。IMP-INF-010)。</summary>
        public const float ButtonFontSizeMin = 14f;
        /// <summary>自動縮小の対象か。「文言表に接続済み」「祖先 2 段以内に Button」「折返し無効 = 1 行ボタン」の 3 つで絞る。
        /// 本文・注記は折返し有効、ギャラリーは Button の外なので入らない (ギャラリーは自前の上限・下限で既に autosize)。
        /// **幅では絞らない。** 120 幅の [設定] だけでなく、320 幅の「ラジオ設定 (PCのみ)」「距離フェード (PCのみ)」が
        /// ru/uk で枠いっぱいになり、540 幅の許可ボタンは状態語 (: ON / OFF) を連結する。溢れ方は幅に依らず同じで、
        /// 閾値を置くとボタンを広げるたび・訳を入れるたびに調整が要るため、1 行ボタンであることだけを条件にする。
        /// world-ui-spec §3 の pt (type.action 20 等) は上限として残り、収まる限り従来どおりの見た目になる。</summary>
        public static bool ButtonAutoSizeTarget(TMP_Text label)
        {
            if(label==null || label.enableWordWrapping)return false;
            var t=label.transform;
            for(int depth=0;depth<2 && t.parent!=null;depth++){t=t.parent;if(t.GetComponent<UnityEngine.UI.Button>()!=null)return true;}
            return false;
        }
        /// <summary>接続済みラベルのうち 1 行ボタンのものへ autosize を付ける (上限 = 現在の pt、下限 = 14pt)。
        /// **既に autosize のものは上限を作り直さない。** autosize 中の fontSize には描画後の縮小値が入るので (TMP は m_fontSize を書き換える)、
        /// 再実行のたびに上限が下がってしまう。元サイズの正本は 1 度目に写した fontSizeMax。</summary>
        public static void ApplyButtonAutoSize(KD_Language target,StringBuilder log)
        {
            if(target==null || target.fixedLabels==null)return;
            int applied=0,kept=0;var list=new StringBuilder();
            for(int i=0;i<target.fixedLabels.Length;i++)
            {
                var label=target.fixedLabels[i];
                if(!ButtonAutoSizeTarget(label))continue;
                float max;
                if(label.enableAutoSizing){max=label.fontSizeMax;kept++;}
                else{max=label.fontSize;label.enableAutoSizing=true;label.fontSizeMax=max;applied++;}
                label.fontSizeMin=ButtonFontSizeMin;
                EditorUtility.SetDirty(label);
                list.AppendLine("  AUTOSIZE "+target.fixedKeys[i]+" "+ScenePath(label.transform)+" rect="+label.rectTransform.rect.size+" max="+max.ToString("0.#")+" min="+ButtonFontSizeMin.ToString("0.#"));
            }
            log.AppendLine("Button autosize: "+(applied+kept)+" one-line button labels (new="+applied+" kept="+kept+", min="+ButtonFontSizeMin.ToString("0.#")+"pt, width not filtered)");
            log.Append(list);
        }
        static string ScenePath(Transform t) { string value=t.name;while(t.parent!=null){t=t.parent;value=t.name+"/"+value;}return value; }
        /// <summary>ラベル 1 件の枠収まり。autosize なら fontSizeMax → fontSizeMin の 2 段で測る。
        /// 戻り値 0 = 上限のまま収まる / 1 = 下限までのどこかで収まる (AUTOSIZE_SHRINK) / 2 = 下限でも収まらない。
        /// vrc-tmp-font §4: オートサイズは「下限に張り付いたら入っていない」ので 2 を溢れとして扱う。
        /// 幅の測定は GetPreferredValues(枠幅) で行い、isOrthographic を立てて UI 単位で測る (未 Awake だと約 1/10 になる)。</summary>
        public static int MeasureLabelFit(TextMeshProUGUI sample,TMP_Text target,string value,out Vector2 preferred)
        {
            preferred=Preferred(sample,target,value,target.enableAutoSizing?target.fontSizeMax:target.fontSize);
            if(Fits(target,preferred))return 0;
            if(!target.enableAutoSizing)return 2;
            preferred=Preferred(sample,target,value,target.fontSizeMin);
            return Fits(target,preferred)?1:2;
        }
        static Vector2 Preferred(TextMeshProUGUI sample,TMP_Text target,string value,float points)
        {
            sample.isOrthographic=true;sample.enableAutoSizing=false;sample.font=target.font;sample.fontSize=points;
            sample.enableWordWrapping=target.enableWordWrapping;sample.margin=target.margin;sample.richText=target.richText;
            sample.rectTransform.sizeDelta=target.rectTransform.rect.size;
            return sample.GetPreferredValues(value,target.rectTransform.rect.width,Mathf.Infinity);
        }
        static bool Fits(TMP_Text target,Vector2 preferred)
        { return !(preferred.y>target.rectTransform.rect.height+2 || (!target.enableWordWrapping && preferred.x>target.rectTransform.rect.width+2)); }
    }
}
