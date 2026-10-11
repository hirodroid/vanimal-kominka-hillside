using UdonSharp;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using VRC.SDKBase;
namespace KominkaHillside
{
    // IMP-DOG-046 / 047 (2026-10-11): the tabs are the sets = the categories (foam / water / finish / station, then all). Only the
    // items in `order` are listed (dropped and unbuilt items are absent, Q4). A tool that is out here shows outColor; Take is refused
    // only while a tool of the chosen item's own category is held (Q3). Reset answers code 3 when a held tool stayed out (Q2).
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class KH_WashSelectionBoard : UdonSharpBehaviour
    {
        public KH_WashSelection selection;
        public KominkaDogRun.KD_Language language;
        public string[] languageCodes;
        public string[] words;
        public int wordCount=46;      // WashPremium/48 / 49 write the scene value (KH_WashSelectionEditor.WordCount)
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
        void Start(){_Refresh();SendCustomEventDelayedSeconds(nameof(_Tick),.25f);}
        public void _Tick()
        {
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
            SendCustomEventDelayedSeconds(nameof(_Tick),.25f);
        }
        public string _Word(int index){return W(index);}
        string W(int i){int k=_lang*wordCount+i;return words!=null&&k>=0&&k<words.Length?words[k]:"";}
        int NameWord(int id){return itemWords!=null&&id>=0&&id<itemWords.Length?itemWords[id]:20+id;}
        int Category(int id){return groups!=null&&id>=0&&id<groups.Length?groups[id]:-1;}
        int ResultWord(int code){return code==KH_WashSelection.ResultApplied?41:code==KH_WashSelection.ResultHeld?5:code==KH_WashSelection.ResultKeptHeld?42:7;}
        bool IsOut(int id){int c=Category(id);return selection!=null&&c>=0&&selection._Selected(c)==id;}
        bool Listed(int id){int g=Category(id);return g>=0&&(_tab==4||g==_tab);}
        public void _Tab0(){Tab(0);}public void _Tab1(){Tab(1);}public void _Tab2(){Tab(2);}public void _Tab3(){Tab(3);}public void _Tab4(){Tab(4);}
        void Tab(int i){_tab=i;_page=0;_chosen=-1;Click();_Refresh();}
        public void _Item0(){Pick(0);}public void _Item1(){Pick(1);}public void _Item2(){Pick(2);}public void _Item3(){Pick(3);}public void _Item4(){Pick(4);}public void _Item5(){Pick(5);}
        void Pick(int slot){if(_visible[slot]<0)return;_chosen=_visible[slot];Click();RefreshItems();}
        public void _Next(){_page++;_chosen=-1;Click();_Refresh();}
        public void _Take(){if(selection==null||_pending||_chosen<0)return;_beforeResult=selection.localResultSerial;_pending=true;_until=Time.time+5f;status.text=W(4);selection._Request(_chosen);}
        public void _Reset(){if(selection==null||_pending)return;_beforeResult=selection.localResultSerial;_pending=true;_until=Time.time+5f;status.text=W(4);selection._Request(-1);}
        void Click(){if(uiSound!=null)uiSound.Play();}
        public void _Refresh()
        {
            if(title==null||groups==null||order==null)return;
            title.text=W(0);setTitle.text=W(10+_tab);description.text=W(15+_tab);notice.text=W(9)+"  ·  "+W(8);resetLabel.text=W(3);nextLabel.text=W(39);
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
