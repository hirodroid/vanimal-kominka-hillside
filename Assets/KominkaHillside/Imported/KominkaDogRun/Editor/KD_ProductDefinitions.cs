using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace KominkaDogRun.Editor
{
    /// <summary>商品メタデータの正本 (shop-foundation §3)。価格・区分は business-strategy §5、
    /// CE の Product ID / Listing ID は docs/development/design/ce-listings.md の台帳と同じ値を持つ
    /// (台帳は貼る場所、ここはコードへ写した値。ずれたら Products/2 - Verify が止める)。
    ///
    /// **CE の 2 種類の ID**: `ceProductId` は所有判定 (UdonProduct アセットへ焼く)、
    /// `ceListingId` は `Store.OpenListing` に渡す。どちらも `prod_` 始まりで取り違えやすい。
    /// セット商品は複数の行が同じアセット / Listing を共有する (ぬいぐるみ 3 行、トレーナー 2 行)。</summary>
    public static class KD_ProductDefinitions
    {
        public sealed class Row
        {
            public readonly string key, categoryKey, sceneName, ceListingId, ceProductId, ceAsset;
            public readonly int price, packCount, initialStock, stockCap, ceCredits;
            public Row(string key,string category,string sceneName,int price,int pack,int initial,int cap,string listing,string productId="",int credits=0,string asset="")
            { this.key=key; categoryKey=category; this.sceneName=sceneName; this.price=price; packCount=pack; initialStock=initial; stockCap=cap; ceListingId=listing; ceProductId=productId; ceCredits=credits; ceAsset=asset; }
            public bool IsCE { get { return !string.IsNullOrEmpty(ceListingId); } }
        }
        public static readonly string[] Categories = { "toy", "petfx", "treat", "live", "accessory", "experience", "action" };
        public static readonly string[] Defaults = { "toy.basic_ball", "petfx.heart", "treat.bone_biscuit", "", "accessory.bandana", "", "" };

        // ---- CE の ID (ce-listings.md §2 と同じ値。2026-09-15 ユーザーが Storefront で作成。1〜6 の Listing は 09-16 に Permanent で作り直し) ----
        const string PlushProduct = "prod_de49c223-4170-4518-b0f3-4c69d6528339", PlushListing = "prod_bbd71ab8-65e9-42ab-9dec-7e566d43d007";
        const string RoyalProduct = "prod_f3c3f5b0-4b65-45a7-888d-51f2a7deff4f", RoyalListing = "prod_2a27af41-ed23-4b84-9a64-922316e72ed5";
        const string SummerProduct = "prod_0f10d010-671e-410c-b18d-104b3d59c8bd", SummerListing = "prod_5a41761e-3fff-4fb5-95cc-2b7aa8d214e1";
        const string LivePackProduct = "prod_3581e247-e676-498d-8e80-2f6a5aa51cc3", LivePackListing = "prod_cd8060fa-753e-4668-b55e-ec4a259a8d43";
        const string CandyProduct = "prod_1307763a-eefe-4575-9c38-27c861f121d6", CandyListing = "prod_74b5b602-bfbe-4230-8654-6d94ec45876f";
        const string PumpkinProduct = "prod_e98d15e4-ccdc-45f2-83a3-21598512a430", PumpkinListing = "prod_de900221-4711-403b-bd4a-1a93e6c5e195";
        // トレーナーは Subscription。Product は Listing に含めた「udon」種別の ID (Role 商品の ID を入れるとアップロードが Unrecognized udon product ID で失敗する)。Listing は Storefront 表記「Subscription ID」
        const string TrainerProduct = "prod_8c044b21-35f2-4ebb-80f9-e4f685f7bf9f", TrainerListing = "prod_81a5ac65-bd3d-40ad-8edb-2fc7d13b1cee";

        public static readonly Row[] All = {
            new Row("action.belly_up", "action", "KD_ShopItem_Action_BellyUp", 30, 0, 0, 0, ""),
            new Row("action.chin_rest", "action", "KD_ShopItem_Action_ChinRest", 30, 0, 0, 0, ""),
            new Row("action.give_paw", "action", "KD_ShopItem_Action_GivePaw", 30, 0, 0, 0, ""),
            new Row("action.stretch_yawn", "action", "KD_ShopItem_Action_StretchYawn", 30, 0, 0, 0, ""),
            // ばにまるトレーナー (500C / 月)。集合とみんなで遊ぶが同じ Subscription を見る。犬目線は Point (09-13 決定)
            new Row("experience.formation", "experience", "KD_ShopItem_Formation", 0, 0, 0, 0, TrainerListing, TrainerProduct, 500, "KD_Product_Trainer"),
            new Row("experience.hosting_play", "experience", "KD_ShopItem_HostingPlay", 0, 0, 0, 0, TrainerListing, TrainerProduct, 500, "KD_Product_Trainer"),
            new Row("experience.dog_pov", "experience", "KD_ShopItem_DogPov", 60, 0, 0, 0, ""),
            new Row("toy.basic_ball", "toy", "KD_ShopItem_Toy_0", 0, 0, 0, 0, ""),
            new Row("toy.tennis_ball", "toy", "KD_ShopItem_Toy_1", 10, 0, 0, 0, ""),
            new Row("toy.donut", "toy", "KD_ShopItem_Toy_2", 20, 0, 0, 0, ""),
            // Retired keys toy.paw_plush / toy.temari_codex are never reused.
            new Row("toy.cloth_ball", "toy", "KD_ShopItem_Toy_4", 40, 0, 0, 0, ""),
            new Row("toy.bone", "toy", "KD_ShopItem_Toy_5", 30, 0, 0, 0, ""),
            new Row("toy.temari_claude", "toy", "KD_ShopItem_Toy_6", 50, 0, 0, 0, ""),
            // ぬいぐるみ 3 点セット (200C)。3 行が 1 つの Product / Listing を共有する (09-13 決定)
            new Row("toy.paw_cushion", "toy", "KD_ShopItem_Toy_PawCushion", 0, 0, 0, 0, PlushListing, PlushProduct, 200, "KD_Product_PlushSet"),
            new Row("toy.taiyaki_plush", "toy", "KD_ShopItem_Toy_TaiyakiPlush", 0, 0, 0, 0, PlushListing, PlushProduct, 200, "KD_Product_PlushSet"),
            new Row("toy.ebiten_plush", "toy", "KD_ShopItem_Toy_EbiTenPlush", 0, 0, 0, 0, PlushListing, PlushProduct, 200, "KD_Product_PlushSet"),
            new Row("accessory.bandana", "accessory", "KD_ShopItem_Acc_8", 0, 0, 0, 0, ""),
            new Row("accessory.straw_hat", "accessory", "KD_ShopItem_Acc_9", 30, 0, 0, 0, ""),
            new Row("petfx.heart", "petfx", "KD_ShopItem_Fx_10", 0, 0, 0, 0, ""),
            new Row("petfx.paw_cookie", "petfx", "KD_ShopItem_Fx_11", 8, 0, 0, 0, ""),
            new Row("petfx.sparkle", "petfx", "KD_ShopItem_Fx_12", 16, 0, 0, 0, ""),
            new Row("petfx.petal", "petfx", "KD_ShopItem_Fx_13", 24, 0, 0, 0, ""),
            new Row("petfx.sakura_heart", "petfx", "KD_ShopItem_Fx_14", 32, 0, 0, 0, ""),
            new Row("petfx.gold_paw", "petfx", "KD_ShopItem_Fx_15", 40, 0, 0, 0, ""),
            new Row("accessory.bow_tie", "accessory", "KD_ShopItem_Acc_16", 20, 0, 0, 0, ""),
            new Row("accessory.tail_ribbon", "accessory", "KD_ShopItem_Acc_17", 10, 0, 0, 0, ""),
            new Row("treat.bone_biscuit", "treat", "KD_ShopItem_Treat_20", 1, 5, 5, 20, ""),
            new Row("treat.paw_cookie", "treat", "KD_ShopItem_Treat_21", 2, 5, 0, 20, ""),
            new Row("treat.star_cookie", "treat", "KD_ShopItem_Treat_22", 3, 5, 0, 20, ""),
            new Row("treat.jerky", "treat", "KD_ShopItem_Treat_23", 5, 5, 0, 20, ""),
            // Live: Point 単品 4 曲 (各 30pt、09-16確定) + CE Pack 3 曲 (200C)。曲と商品の対応は KD_DogDefs.LiveShows
            new Row("live.happy_tails", "live", "KD_ShopItem_Live_25", 30, 0, 0, 0, ""),
            new Row("live.garden_lights", "live", "KD_ShopItem_Live_GardenLights", 30, 0, 0, 0, ""),
            new Row("live.petal_waltz", "live", "KD_ShopItem_Live_PetalWaltz", 30, 0, 0, 0, ""),
            new Row("live.after_rain", "live", "KD_ShopItem_Live_AfterRain", 30, 0, 0, 0, ""),
            new Row("live.kominka_pack", "live", "KD_ShopItem_Live_26", 0, 0, 0, 0, LivePackListing, LivePackProduct, 200, "KD_Product_KominkaLivePack"),
            // One entitlement covers CandyStep hosting and all three everyday accessories (300C).
            new Row("live.candy_step_set", "live", "KD_ShopItem_Live_CandyStepSet", 0, 0, 0, 0, CandyListing, CandyProduct, 300, "KD_Product_CandyStep"),
            // 季節商品 (300C。販売は 2026-10-31 23:59 JST まで、購入後は期限後も使える)。期限は Storefront 側で閉じる
            new Row("live.pumpkin_parade_set", "live", "KD_ShopItem_Live_PumpkinParadeSet", 0, 0, 0, 0, PumpkinListing, PumpkinProduct, 300, "KD_Product_PumpkinParade"),
            new Row("accessory.royal_set", "accessory", "KD_ShopItem_Acc_27", 0, 0, 0, 0, RoyalListing, RoyalProduct, 200, "KD_Product_RoyalSet"),
            new Row("accessory.summer_set", "accessory", "KD_ShopItem_Acc_28", 0, 0, 0, 0, SummerListing, SummerProduct, 200, "KD_Product_SummerSet"),
            new Row("accessory.floral_set", "accessory", "KD_ShopItem_Acc_29", 60, 0, 0, 0, ""),
            new Row("accessory.top_hat", "accessory", "KD_ShopItem_Acc_TopHat", 30, 0, 0, 0, ""),
            new Row("accessory.cap", "accessory", "KD_ShopItem_Acc_Cap", 20, 0, 0, 0, ""),
            new Row("accessory.spiral_glasses", "accessory", "KD_ShopItem_Acc_SpiralGlasses", 20, 0, 0, 0, ""),
            new Row("accessory.star_glasses", "accessory", "KD_ShopItem_Acc_StarGlasses", 30, 0, 0, 0, ""),
            new Row("accessory.backpack", "accessory", "KD_ShopItem_Acc_Backpack", 40, 0, 0, 0, ""),
            // 同梱FX。Pointでは売らず、対応セットと所有権・購入先を共有する。
            new Row("petfx.candy", "petfx", "KD_ShopItem_Fx_Candy", 0, 0, 0, 0, CandyListing, CandyProduct, 300, "KD_Product_CandyStep"),
            new Row("petfx.pumpkin", "petfx", "KD_ShopItem_Fx_Pumpkin", 0, 0, 0, 0, PumpkinListing, PumpkinProduct, 300, "KD_Product_PumpkinParade"),
            new Row("petfx.lantern", "petfx", "KD_ShopItem_Fx_Lantern", 0, 0, 0, 0, LivePackListing, LivePackProduct, 200, "KD_Product_KominkaLivePack"),
            new Row("petfx.meteor", "petfx", "KD_ShopItem_Fx_Meteor", 0, 0, 0, 0, LivePackListing, LivePackProduct, 200, "KD_Product_KominkaLivePack"),
            new Row("petfx.confetti", "petfx", "KD_ShopItem_Fx_Confetti", 0, 0, 0, 0, LivePackListing, LivePackProduct, 200, "KD_Product_KominkaLivePack"),
            // 裏山改変 (DES-LIV-001 / 002): 裏山専用ライブ「紅葉が星になる」(Point 30pt の買い切り)。末尾に足す (既存行の並びを動かさない)。
            // 古民家のライブ 7 行は台帳の行として残す (画面に出るのは KD_DogDefs.LiveProducts に載せたものだけ)
            new Row("live.hillside_dusk", "live", "KD_ShopItem_Live_HillsideDusk", 30, 0, 0, 0, ""),
        };
        public static Row Get(string key)
        { var row=Array.Find(All,x=>x.key==key); if(row==null) throw new InvalidOperationException("Unknown product: "+key); return row; }
        public static string SceneName(string key) { return Get(key).sceneName; }

        // ---- UdonProduct アセット (所有判定の器。Product ID を焼く) ----
        public const string CeAssetFolder = "Assets/KominkaHillside/Data/CE";
        public static string CeAssetPath(Row d) { return string.IsNullOrEmpty(d.ceAsset) ? "" : CeAssetFolder + "/" + d.ceAsset + ".asset"; }
        public static VRC.Economy.UdonProduct LoadCeProduct(Row d)
        {
            string path=CeAssetPath(d); if(path.Length==0) return null;
            var asset=AssetDatabase.LoadAssetAtPath<VRC.Economy.UdonProduct>(path);
            if(asset==null) throw new InvalidOperationException("CE asset missing (run Products/1 - Apply Metadata): "+path);
            return asset;
        }
        /// <summary>無ければ作り、あれば ID / 名前を表に揃える (冪等。GUID は変えない)。戻り値は作った数。</summary>
        public static int EnsureCeAssets(StringBuilder log)
        {
            Validate(All,Categories);
            if(!AssetDatabase.IsValidFolder(CeAssetFolder)) AssetDatabase.CreateFolder("Assets/KominkaHillside/Imported/KominkaDogRun/Data","CE");
            int created=0; var done=new HashSet<string>();
            foreach(var d in All)
            {
                string path=CeAssetPath(d); if(path.Length==0 || !done.Add(path)) continue;
                var asset=AssetDatabase.LoadAssetAtPath<VRC.Economy.UdonProduct>(path);
                if(asset==null)
                {
                    asset=ScriptableObject.CreateInstance<VRC.Economy.UdonProduct>();
                    AssetDatabase.CreateAsset(asset,path); created++;
                    if(log!=null) log.AppendLine("created "+path);
                }
                var so=new SerializedObject(asset);
                var id=so.FindProperty("_ID"); var name=so.FindProperty("_Name");
                if(id==null || name==null) throw new InvalidOperationException("UdonProduct fields changed (SDK): "+path);
                if(id.stringValue!=d.ceProductId || name.stringValue!=d.ceAsset)
                { id.stringValue=d.ceProductId; name.stringValue=d.ceAsset; so.ApplyModifiedPropertiesWithoutUndo(); EditorUtility.SetDirty(asset); if(log!=null) log.AppendLine("updated "+path+" ID="+d.ceProductId); }
            }
            AssetDatabase.SaveAssets();
            return created;
        }

        public static void Apply(KD_ShopItem item,string key)
        {
            var d=Get(key); item.productKey=d.key; item.categoryKey=d.categoryKey; item.price=d.price;
            item.packCount=d.packCount; item.consumable=d.packCount>0; item.initialStock=d.initialStock; item.stockCap=d.stockCap;
            item.ceListingId=d.ceListingId; item.ceCredits=d.ceCredits; item.ceProduct=LoadCeProduct(d);
        }
        static readonly Regex ProdId=new Regex(@"^prod_[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$");
        public static void Validate(Row[] rows,string[] categories,bool test=false)
        {
            if(rows==null || categories==null || rows.Length==0 || categories.Length==0) throw new InvalidOperationException("Empty product catalog");
            var cats=new HashSet<string>();var keys=new HashSet<string>();var assets=new Dictionary<string,Row>();
            foreach(var c in categories) if(c==null || !Regex.IsMatch(c,@"^[a-z][a-z0-9_]*$") || !cats.Add(c)) throw new InvalidOperationException("Invalid category: "+c);
            foreach(var r in rows)
            {
                if(r==null || r.key==null || !Regex.IsMatch(r.key,@"^[a-z][a-z0-9_]*(\.[a-z][a-z0-9_]*)+$") || !keys.Add(r.key) || !cats.Contains(r.categoryKey) || r.key.StartsWith("test.")!=test) throw new InvalidOperationException("Invalid/duplicate/unregistered product key");
                if(r.price<0 || r.price>KD_ProgressLedger.MaxPoints || r.packCount<0 || r.initialStock<0 || r.stockCap<0 || (r.packCount==0 && (r.initialStock!=0 || r.stockCap!=0)) || (r.packCount>0 && r.price==0) || (r.IsCE && (r.price!=0 || r.packCount!=0))) throw new InvalidOperationException("Invalid product metadata: "+r.key);
                if(r.IsCE)
                {
                    // 本物の ID だけ (仮の TODO- は 09-15 で消えた)。Credits は表示用だが 0 は「値段が無い」に見えるので通さない
                    if(!ProdId.IsMatch(r.ceListingId) || !ProdId.IsMatch(r.ceProductId) || r.ceCredits<=0 || string.IsNullOrEmpty(r.ceAsset) || !Regex.IsMatch(r.ceAsset,@"^KD_Product_[A-Za-z0-9]+$"))
                        throw new InvalidOperationException("Invalid CE metadata: "+r.key);
                    // 同じアセットを共有する行 (セット商品) は Product / Listing / Credits も同じでなければならない
                    Row first;
                    if(assets.TryGetValue(r.ceAsset,out first)) { if(first.ceProductId!=r.ceProductId || first.ceListingId!=r.ceListingId || first.ceCredits!=r.ceCredits) throw new InvalidOperationException("CE set rows disagree: "+r.key); }
                    else assets[r.ceAsset]=r;
                }
                else if(!string.IsNullOrEmpty(r.ceProductId) || r.ceCredits!=0 || !string.IsNullOrEmpty(r.ceAsset)) throw new InvalidOperationException("Non-CE row carries CE fields: "+r.key);
            }
            // 1 つの Product ID は 1 つのアセット (別名で二重に作らない)
            if(assets.Values.Select(v=>v.ceProductId).Distinct().Count()!=assets.Count) throw new InvalidOperationException("One product id maps to two CE assets");
        }
        public static void ValidateItem(KD_ShopItem item)
        {
            var d=Get(item.productKey);
            if(!item.IsConfigured() || item.categoryKey!=d.categoryKey || item.price!=d.price || item.packCount!=d.packCount || item.initialStock!=d.initialStock || item.stockCap!=d.stockCap || item.ceListingId!=d.ceListingId || item.ceCredits!=d.ceCredits) throw new InvalidOperationException("Stale product: "+d.key);
            if(!d.IsCE) { if(item.ceProduct!=null) throw new InvalidOperationException("Non-CE item carries a product asset: "+d.key); return; }
            if(item.ceProduct==null || AssetDatabase.GetAssetPath(item.ceProduct)!=CeAssetPath(d) || item.ceProduct.ID!=d.ceProductId) throw new InvalidOperationException("Stale CE asset: "+d.key);
        }
    }
}
