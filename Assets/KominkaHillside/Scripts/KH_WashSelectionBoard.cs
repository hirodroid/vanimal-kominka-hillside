using UdonSharp;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using VRC.SDKBase;
using VRC.Economy;
namespace KominkaHillside
{
    // IMP-DOG-046 / 047 (2026-10-11): the tabs are the sets = the categories (foam / water / finish / station, then all). Only the
    // items in `order` are listed (dropped and unbuilt items are absent, Q4). A tool that is out here shows outColor; Take is refused
    // only while a tool of the chosen item's own category is held (Q3). Reset answers code 3 when a held tool stayed out (Q2).
    // IMP-DOG-056 §3 (2026-10-14, minimal default until DES-UI-005): the notice line shows the chosen item's set as this client sees it
    // (Owned: usable in this instance, no purchase prompt even when someone else bought it / None: the price and [open the purchase page] /
    // Unknown: checking / still out after its owner left: until the wash ends). The Owner's gate decides; this board only reports it.
    // Buying is a separate button that opens the Storefront listing; opening it is never taken as a purchase (Store events bring it back).
    // User decision 2026-10-14: the button shows in every known state (Owned too: someone else may buy as well), hidden only while Unknown.
    // A set this player owns turns its button into "gift" (the same Listing; the gift itself is VRChat's page, as KominkaDogRun IMP-PRG-012),
    // drawn in giftColor (open-only = neutral). Second decision the same day: owning any set turns the complete set's button into gift too
    // (it stays possible to give it). A gift button takes two presses: the first opens nothing, shows the caution (53: you own a set, do not
    // buy this for yourself) and relabels it 54 (confirm and open) for GiftConfirmSeconds; the second within that opens the Listing. Expiry
    // or a tab change disarms it. The buy button (46) opens on the first press as before.
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class KH_WashSelectionBoard : UdonSharpBehaviour
    {
        public KH_WashSelection selection;
        public KominkaDogRun.KD_Language language;
        public string[] languageCodes;
        public string[] words;
        public int wordCount=55;      // WashPremium/48 / 49 / 54 write the scene value (KH_WashSelectionEditor.WordCount)
        public int[] groups;          // by item id: the tab (0 foam / 1 water / 2 finish / 3 station), -1 = not offered
        public int[] order;           // the offered item ids in display order
        public int[] itemWords;       // by item id: the word holding its name (20 + id for the first 17; new items append words)
        public Texture2D[] setPhotos;
        public Texture2D[] itemPhotos;
        public RawImage hero;
        public RawImage[] itemImages;
        public TextMeshProUGUI title, setTitle, description, notice, actionLabel, resetLabel, status, nextLabel;
        public TextMeshProUGUI[] setLabels, itemLabels;
        public Image[] setButtons, itemButtons;
        public Button actionButton, nextButton;
        public GameObject[] itemRows;
        public AudioSource uiSound;
        public Color normalColor, selectedColor, outColor;
        // IMP-DOG-056: the complete set's Listing (C-5: the four set Products in one Listing, no Product of its own) and its price, written by
        // WashPremium/54 from KD_ProductDefinitions.WashAllListing / WashAllCredits; the purchase button (54 builds and wires it, hidden).
        public string allListingId="";
        public int allCredits;
        public Button buyButton;
        public TextMeshProUGUI buyLabel;
        public Image buyImage;        // the purchase button's own Image (54): normalColor to buy, giftColor to gift
        public Color giftColor;       // 54 writes it (normalColor a little darker: the neutral "open only" colour)
        // Store display state (KD_ShopItem.Instance* plus StoreContinuing) and the category it belongs to; -1 / -2 = not read yet.
        public const int StoreContinuing=3;
        private int _storeState=-1, _storeCategory=-2;
        private string _storeLanguage="?";
        // This player's own sets (bit c = KD_ShopItem.Owned() of setItems[c]; false before the purchases load), re-read at most every
        // OwnRecheckSeconds in the tick (Owned() may ask the Store). _Refresh and the Edit-mode checks only read the cached mask.
        const float OwnRecheckSeconds=1f;
        private int _ownMask, _shownOwn=-1;
        private float _ownUntil;
        // The gift button's confirmation (local): armed by its first press, open on a second press within GiftConfirmSeconds.
        public const float GiftConfirmSeconds=8f;
        private bool _giftArmed;
        private float _giftArmedAt=-100f;
        // Checks only: >= 0 replaces the seconds since the gift button was armed (WashPremium/44); true = _Buy records the Listing it would
        // open (_lastListing) without Store.OpenListing (the ClientSim harness: no Store page there). 44 checks both at rest.
        public float debugGiftElapsedOverride=-1f;
        public bool debugNoStoreOpen;
        private string _lastListing="";
        private int _tab, _page, _chosen=-1, _lang;
        private bool _wasBusy;
        private int _labelKey=-1, _seenRevision=-1;
        private string _language="?", _pickupLanguage="?";
        private int[] _visible=new int[6];
        private bool _pending;
        private int _beforeResult;
        private float _until;
        // A result (applied / held / kept held / try again) stays readable this long before the held-tool hint may replace it,
        // as KH_WashStationBoard keeps its refusal (_refuseUntil). Reset while holding a tool answers code 3 AND is held at once.
        const float ResultSeconds=4f;
        private float _resultUntil=-100f;
        // 2026-10-12 (VER-ENV-002 perf): one _Tick chain, always re-armed at 0.25 s (Start arms it once, every _Tick path re-arms it
        // exactly once: never two outstanding, never none). Only the body is gated: while the local player is farther than NearMeters
        // (horizontal, as KH_WashStationBoard showMeters 12 m) and nothing waits (no request pending, no result on show), the body runs
        // about once a second (_farUntil). Every action handler calls Poke(), so a press is answered within 0.25 s from any distance.
        // Content unchanged; action response <= 0.25 s (Poke / pending), passive updates (language, pickup labels, held hint) <= ~1 s when far.
        const float NearMeters=12f, NearTickSeconds=.25f, FarBodySeconds=1f;
        private float _farUntil;   // far: the body waits until this time (0 = run it on the next tick)
        void Start(){_Refresh();SendCustomEventDelayedSeconds(nameof(_Tick),NearTickSeconds);}
        void Poke(){_farUntil=0f;}
        public void _Tick()
        {
            if(!_pending&&!_giftArmed&&Time.time<_farUntil&&Time.time>=_resultUntil){SendCustomEventDelayedSeconds(nameof(_Tick),NearTickSeconds);return;}
            string code=language!=null?language.CurrentLanguage:"ja";
            if(code!=_language){_language=code;_lang=0;for(int i=0;languageCodes!=null&&i<languageCodes.Length;i++)if(languageCodes[i]==code)_lang=i;_Refresh();}
            if(_pending&&selection!=null)
            {
                var lp=Networking.LocalPlayer;
                if(Utilities.IsValid(lp)&&selection.localResultSerial!=_beforeResult){_pending=false;status.text=W(ResultWord(selection.localResultCode));_resultUntil=Time.time+ResultSeconds;}
                else if(Time.time>_until){_pending=false;status.text=W(7);_resultUntil=Time.time+ResultSeconds;}
            }
            if(selection!=null&&selection.revision!=_seenRevision){_seenRevision=selection.revision;RefreshItems();}
            int category=Category(_chosen);
            // IMP-DOG-056: the set's state through KD_ShopItem's cache (cheap per tick); the line is rewritten only when it changes
            int store=StoreState(category);
            if(Time.time>=_ownUntil){_ownUntil=Time.time+OwnRecheckSeconds;_ownMask=OwnMask();}
            if(store!=_storeState||category!=_storeCategory||code!=_storeLanguage||_ownMask!=_shownOwn){_storeState=store;_storeCategory=category;_storeLanguage=code;_shownOwn=_ownMask;ShowStore();}
            if(_giftArmed&&!GiftArmed()){_giftArmed=false;ShowStore();}   // the confirmation ran out: back to 52 without the caution
            bool busy=selection!=null&&category>=0&&category<KH_WashSelection.Categories&&selection._CategoryHeld(category);
            if(!_pending&&Time.time>=_resultUntil){if(busy)status.text=W(5);else if(_wasBusy)status.text=W(40);}_wasBusy=busy;
            if(actionButton!=null)actionButton.interactable=!_pending&&selection!=null&&_chosen>=0&&!busy&&!(category==KH_WashSelection.CategoryBath&&IsOut(_chosen));
            if(selection!=null)
            {
                int key=0;for(int c=0;c<KH_WashSelection.Categories;c++)key=key*64+selection._ShownItem(c)+2;
                if(key!=_labelKey||code!=_pickupLanguage)
                {
                    _labelKey=key;_pickupLanguage=code;
                    for(int c=0;c<KH_WashSelection.Categories;c++)
                    {
                        int item=selection._ShownItem(c);if(item<0)continue;
                        var pickup=selection.pickups[selection.itemSlots[item]];pickup.InteractionText=W(NameWord(item));pickup.UseText=W(NameWord(item));
                    }
                }
            }
            _farUntil=IsFar()?Time.time+FarBodySeconds-.05f:0f;   // -.05: the 4th 0.25 s tick lands on it, not the 5th
            SendCustomEventDelayedSeconds(nameof(_Tick),NearTickSeconds);
        }
        bool IsFar()
        {
            var lp=Networking.LocalPlayer;
            if(!Utilities.IsValid(lp))return true;
            Vector3 d=lp.GetPosition()-transform.position;d.y=0f;
            return d.sqrMagnitude>NearMeters*NearMeters;
        }
        public string _Word(int index){return W(index);}
        string W(int i){int k=_lang*wordCount+i;return words!=null&&i>=0&&i<wordCount&&k<words.Length?words[k]:"";}   // never the next language's word
        int NameWord(int id){return itemWords!=null&&id>=0&&id<itemWords.Length?itemWords[id]:20+id;}
        int Category(int id){return groups!=null&&id>=0&&id<groups.Length?groups[id]:-1;}
        int ResultWord(int code){return code==KH_WashSelection.ResultApplied?41:code==KH_WashSelection.ResultHeld?5:code==KH_WashSelection.ResultKeptHeld?42:code==KH_WashSelection.ResultLocked?48:code==KH_WashSelection.ResultPending?49:7;}
        /// <summary>IMP-DOG-056: the chosen category's set here (KD_ShopItem.Instance*), StoreContinuing when it is None but its item is still
        /// out (the owner left; kept until the wash ends), -1 with no category.</summary>
        int StoreState(int category)
        {
            if(selection==null||category<0)return -1;
            int st=selection._SetState(category);
            if(st==KominkaDogRun.KD_ShopItem.InstanceNone&&selection._RevertPendingFor(category))return StoreContinuing;
            return st;
        }
        /// <summary>This player's own sets as a bit mask (KD_ShopItem.Owned: this player only, false until the purchases load).</summary>
        int OwnMask()
        {
            if(selection==null||selection.setItems==null)return 0;
            int m=0;
            for(int c=0;c<selection.setItems.Length&&c<4;c++)if(selection.setItems[c]!=null&&selection.setItems[c].Owned())m|=1<<c;
            return m;
        }
        /// <summary>The notice line and the purchase button from the cached state and own mask (no Store call: _Refresh and the Edit-mode
        /// menus use it). A state read for another category (a tab or item just changed) shows "checking" until the next tick.</summary>
        /// <summary>The button is shown: the chosen item's set is known here (Owned / None / continuing; Unknown never pushes to the page).</summary>
        bool BuyShown()
        {
            int c=Category(_chosen);
            int st=c>=0&&c==_storeCategory?_storeState:-1;
            return st==KominkaDogRun.KD_ShopItem.InstanceOwned||st==KominkaDogRun.KD_ShopItem.InstanceNone||st==StoreContinuing;
        }
        /// <summary>The shown button is a gift button: this tab's set is this player's own, or (complete-set tab) any set is.</summary>
        bool GiftNow()
        {
            if(!BuyShown())return false;
            if(_tab==4)return _ownMask!=0;
            int c=Category(_chosen);
            return c>=0&&(_ownMask&(1<<c))!=0;
        }
        bool GiftArmed()
        {
            if(!_giftArmed)return false;
            float e=debugGiftElapsedOverride>=0f?debugGiftElapsedOverride:Time.time-_giftArmedAt;
            return e<GiftConfirmSeconds;
        }
        void ShowStore()
        {
            if(notice==null)return;
            int c=Category(_chosen);
            int st=c>=0&&c==_storeCategory?_storeState:-1;
            bool buy=BuyShown();
            bool gift=GiftNow();
            if(!gift)_giftArmed=false;
            bool armed=gift&&GiftArmed();
            if(c<0)notice.text=W(8);
            else if(armed)notice.text=W(53);   // the caution, only between the two presses
            else if(st==StoreContinuing)notice.text=W(50)+"  ·  "+W(8);
            else if(st==KominkaDogRun.KD_ShopItem.InstanceOwned)notice.text=W(47)+"  ·  "+W(8);
            else if(st==KominkaDogRun.KD_ShopItem.InstanceNone)notice.text=Price(c)+"  ·  "+W(48);
            else notice.text=W(49)+"  ·  "+W(8);
            if(buyButton!=null)
            {
                if(buyButton.gameObject.activeSelf!=buy)buyButton.gameObject.SetActive(buy);
                if(!buyButton.interactable)buyButton.interactable=true;
            }
            if(buyImage!=null)buyImage.color=gift?giftColor:normalColor;
            if(buyLabel!=null)buyLabel.text=W(armed?54:gift?52:46);
        }
        /// <summary>The price the purchase button leads to: the complete set on the complete-set tab, else the chosen item's set.</summary>
        string Price(int c)
        {
            int credits=0;
            if(_tab==4)credits=allCredits;
            else if(selection!=null&&selection.setItems!=null&&c>=0&&c<selection.setItems.Length&&selection.setItems[c]!=null)credits=selection.setItems[c].ceCredits;
            return credits>0?credits.ToString()+" "+W(51):"";
        }
        /// <summary>IMP-DOG-056: opens the Storefront page of this tab's set (the complete set's Listing on its tab). Local only; ownership comes
        /// back through the Store events (KD_ShopItem / KH_WashSelection), never from this press.</summary>
        public void _Buy()
        {
            string id=BuyPress();
            Click();Poke();
            if(id.Length==0)return;
            _lastListing=id;
            if(!debugNoStoreOpen)Store.OpenListing(id);
        }
        /// <summary>One press of the purchase button: the Listing to open now, or "" (nothing to open, or a gift button's first press, which
        /// only arms it: caution 53, label 54). A gift button's second press within GiftConfirmSeconds opens and disarms.</summary>
        string BuyPress()
        {
            string id=BuyListing();
            if(id==null||id.Length==0)return "";
            if(GiftNow())
            {
                if(!GiftArmed()){_giftArmed=true;_giftArmedAt=Time.time;ShowStore();return "";}
                _giftArmed=false;ShowStore();
            }
            return id;
        }
        /// <summary>The Listing the button opens: this tab's set (buy or gift: the same Listing), the complete set's on its tab.</summary>
        string BuyListing()
        {
            if(_tab==4)return allListingId;
            if(selection!=null&&selection.setItems!=null&&_tab>=0&&_tab<selection.setItems.Length&&selection.setItems[_tab]!=null)return selection.setItems[_tab].ceListingId;
            return "";
        }
        bool IsOut(int id){int c=Category(id);return selection!=null&&c>=0&&selection._Selected(c)==id;}
        bool Listed(int id){int g=Category(id);return g>=0&&(_tab==4||g==_tab);}
        public void _Tab0(){Tab(0);}public void _Tab1(){Tab(1);}public void _Tab2(){Tab(2);}public void _Tab3(){Tab(3);}public void _Tab4(){Tab(4);}
        void Tab(int i){_tab=i;_page=0;_chosen=-1;_giftArmed=false;Click();_Refresh();Poke();}
        public void _Item0(){Pick(0);}public void _Item1(){Pick(1);}public void _Item2(){Pick(2);}public void _Item3(){Pick(3);}public void _Item4(){Pick(4);}public void _Item5(){Pick(5);}
        void Pick(int slot){if(_visible[slot]<0)return;_chosen=_visible[slot];Click();RefreshItems();Poke();}
        public void _Next(){_page++;_chosen=-1;Click();_Refresh();Poke();}
        public void _Take(){if(selection==null||_pending||_chosen<0)return;_beforeResult=selection.localResultSerial;_pending=true;_until=Time.time+5f;status.text=W(4);selection._Request(_chosen);Poke();}
        public void _Reset(){if(selection==null||_pending)return;_beforeResult=selection.localResultSerial;_pending=true;_until=Time.time+5f;status.text=W(4);selection._Request(-1);Poke();}
        void Click(){if(uiSound!=null)uiSound.Play();}
        public void _Refresh()
        {
            if(title==null||groups==null||order==null)return;
            title.text=W(0);setTitle.text=W(10+_tab);description.text=W(15+_tab);resetLabel.text=W(3);nextLabel.text=W(39);
            if(!_pending)status.text=W(40);
            hero.texture=setPhotos[_tab];
            for(int i=0;i<5;i++){setLabels[i].text=W(10+i);setButtons[i].color=i==_tab?selectedColor:normalColor;}
            int total=0;for(int k=0;k<order.Length;k++)if(Listed(order[k]))total++;
            int pages=(total+5)/6;if(_page>=pages)_page=0;nextButton.gameObject.SetActive(pages>1);
            int seen=0, shown=0;
            for(int s=0;s<6;s++)_visible[s]=-1;
            for(int k=0;k<order.Length;k++)
            {
                int id=order[k];
                if(!Listed(id))continue;
                if(seen++<_page*6)continue;if(shown>=6)break;
                _visible[shown]=id;if(_chosen<0)_chosen=id;
                itemImages[shown].texture=itemPhotos!=null&&id<itemPhotos.Length?itemPhotos[id]:null;itemLabels[shown].text=W(NameWord(id));shown++;
            }
            // a hidden row drops its photo too (no texture of an item that is not listed stays referenced in the scene)
            for(int s=0;s<6;s++){itemRows[s].SetActive(s<shown);if(s>=shown)itemImages[s].texture=null;}
            RefreshItems();
            ShowStore();   // IMP-DOG-056: the notice line (was W(9) "all available" + W(8)) follows the chosen item's set
        }
        // The out marks and the action label (also on a selection change: it must not overwrite a result in the status line).
        void RefreshItems()
        {
            if(itemButtons==null||actionLabel==null)return;
            for(int s=0;s<6;s++)if(_visible[s]>=0)itemButtons[s].color=_visible[s]==_chosen?selectedColor:IsOut(_visible[s])?outColor:normalColor;
            int c=Category(_chosen);
            actionLabel.text=W(c==KH_WashSelection.CategoryBath?(IsOut(_chosen)?38:2):1);
        }
    }
}
