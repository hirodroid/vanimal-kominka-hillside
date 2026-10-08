using UdonSharp;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using VRC.SDKBase;
namespace KominkaHillside
{
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class KH_WashSelectionBoard : UdonSharpBehaviour
    {
        public KH_WashSelection selection;
        public KominkaDogRun.KD_Language language;
        public string[] languageCodes;
        public string[] words;
        public int wordCount=42;
        public int[] groups;
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
        public Color normalColor, selectedColor;
        private int _tab, _page, _chosen=-1, _lang;
        private bool _wasBusy;
        private int _labelTool=-2;
        private string _language="?", _pickupLanguage="?";
        private int[] _visible=new int[6];
        private bool _pending;
        private int _beforeResult;
        private float _until;
        void Start(){_Refresh();SendCustomEventDelayedSeconds(nameof(_Tick),.25f);}
        public void _Tick()
        {
            string code=language!=null?language.CurrentLanguage:"ja";
            if(code!=_language){_language=code;_lang=0;for(int i=0;languageCodes!=null&&i<languageCodes.Length;i++)if(languageCodes[i]==code)_lang=i;_Refresh();}
            if(_pending&&selection!=null)
            {
                var lp=Networking.LocalPlayer;
                if(Utilities.IsValid(lp)&&selection.localResultSerial!=_beforeResult){_pending=false;status.text=W(selection.localResultCode==0?41:selection.localResultCode==1?5:7);}
                else if(Time.time>_until){_pending=false;status.text=W(7);}
            }
            bool busy=selection!=null && selection._Busy();
            if(busy && !_pending)status.text=W(5);else if(_wasBusy && !_pending)status.text=W(40);_wasBusy=busy;
            if(actionButton!=null)actionButton.interactable=!_pending && selection!=null && !busy;
            if(selection!=null && (_labelTool!=selection.selectedTool || code!=_pickupLanguage)){
                _labelTool=selection.selectedTool;_pickupLanguage=code;
                if(_labelTool>=0){var pickup=selection.pickups[selection.itemSlots[_labelTool]];pickup.InteractionText=W(20+_labelTool);pickup.UseText=W(20+_labelTool);}
            }
            SendCustomEventDelayedSeconds(nameof(_Tick),.25f);
        }
        public string _Word(int index){return W(index);}
        string W(int i){int k=_lang*wordCount+i;return words!=null&&k>=0&&k<words.Length?words[k]:"";}
        public void _Tab0(){Tab(0);}public void _Tab1(){Tab(1);}public void _Tab2(){Tab(2);}public void _Tab3(){Tab(3);}public void _Tab4(){Tab(4);}
        void Tab(int i){_tab=i;_page=0;_chosen=-1;Click();_Refresh();}
        public void _Item0(){Pick(0);}public void _Item1(){Pick(1);}public void _Item2(){Pick(2);}public void _Item3(){Pick(3);}public void _Item4(){Pick(4);}public void _Item5(){Pick(5);}
        void Pick(int slot){if(_visible[slot]<0)return;_chosen=_visible[slot];Click();_Refresh();}
        public void _Next(){_page++;_chosen=-1;Click();_Refresh();}
        public void _Take(){if(selection==null||_pending)return;_beforeResult=selection.localResultSerial;_pending=true;_until=Time.time+5f;status.text=W(4);selection._Request(_chosen);}
        public void _Reset(){if(selection==null||_pending)return;_beforeResult=selection.localResultSerial;_pending=true;_until=Time.time+5f;status.text=W(4);selection._Request(-1);}
        void Click(){if(uiSound!=null)uiSound.Play();}
        public void _Refresh()
        {
            if(title==null||groups==null)return;
            title.text=W(0);setTitle.text=W(10+_tab);description.text=W(15+_tab);notice.text=W(9)+"  ·  "+W(8);resetLabel.text=W(3);nextLabel.text=W(39);
            if(!_pending)status.text=W(40);
            hero.texture=setPhotos[_tab];
            for(int i=0;i<5;i++){setLabels[i].text=W(10+i);setButtons[i].color=i==_tab?selectedColor:normalColor;}
            int total=0;for(int i=0;i<groups.Length;i++)if(_tab==4||groups[i]==_tab)total++;
            int pages=(total+5)/6;if(_page>=pages)_page=0;nextButton.gameObject.SetActive(pages>1);
            int seen=0, shown=0;
            for(int s=0;s<6;s++)_visible[s]=-1;
            for(int id=0;id<groups.Length;id++)
            {
                if(_tab!=4&&groups[id]!=_tab)continue;
                if(seen++<_page*6)continue;if(shown>=6)break;
                _visible[shown]=id;if(_chosen<0)_chosen=id;
                itemImages[shown].texture=itemPhotos[id];itemLabels[shown].text=W(20+id);itemButtons[shown].color=id==_chosen?selectedColor:normalColor;shown++;
            }
            for(int s=0;s<6;s++)itemRows[s].SetActive(s<shown);
            actionLabel.text=W(_chosen==16&&selection!=null&&selection.duckOn?37:_chosen<13?1:2);
        }
    }
}
