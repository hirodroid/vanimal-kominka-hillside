using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UdonSharpEditor;
using KominkaDogRun;
using KominkaDogRun.Editor;

namespace KominkaHillside.Editor
{
 public static partial class KH_HalloweenParadeEditor
 {
  const string PreviewRowName="HalloweenPreviewRow", PaidRowName="LiveRow1";
  // Retired public-preview entry point cannot re-enable free hosting.
  public static void EnableLivePreview(){ApplyCommerce();}
  [MenuItem(Menu+"55 - Apply Paid Menu")]
  public static void ApplyCommerce(){SceneTransaction("Parade paid menu",()=>{
   var pouch=Only<KD_TreatPouch>();var panel=Only<KH_HalloweenParadePanel>();var manager=panel.parade;
   var catalog=Only<KD_ShopCatalog>();var ledger=catalog.ledger;
   var d=KD_ProductDefinitions.Get(KH_HalloweenParade.ProductKey);
   KD_ProductDefinitions.Validate(KD_ProductDefinitions.All,KD_ProductDefinitions.Categories);
   var content=pouch.tabScrolls[KD_TreatPouch.TabLive].content;var template=content.Find("LiveRow0");
   if(template==null||content.GetComponent<GridLayoutGroup>()==null||template.GetComponentsInChildren<VRC.Udon.UdonBehaviour>(true).Length!=0)throw new InvalidOperationException("Expected plain Live row/grid");
   if(pouch.eventBandRoot==null||pouch.eventBandLabel==null||pouch.liveDetailRoot==null)throw new InvalidOperationException("Existing event band/detail UI required");
   Undo.RecordObject(pouch,"Parade menu");Undo.RecordObject(catalog,"Parade product");Undo.RecordObject(ledger,"Parade product");
   var item=All<KD_ShopItem>().SingleOrDefault(x=>x.productKey==d.key);
   if(item==null)item=U<KD_ShopItem>(Child(catalog.transform,d.sceneName,catalog.transform.position).gameObject,VRC.SDKBase.Networking.SyncType.None);
   Undo.RecordObject(item,"Parade product");KD_ProductDefinitions.EnsureCeAssets(null);KD_ProductDefinitions.Apply(item,d.key);
   item.ledger=ledger;item.menuOnly=true;item.displayNameKey=d.sceneName+".displayNameKey.0";
   var translations=KD_LocalizationEditor.Load();item.displayName=KD_LocalizationEditor.Text(translations,"ja",item.displayNameKey);Copy(item);
   if(!catalog.items.Contains(item))catalog.items=catalog.items.Concat(new[]{item}).ToArray();
   ledger.products=catalog.items;Copy(catalog);Copy(ledger);
   var old=content.Find(PreviewRowName);var board=panel.transform.parent.Find("ParadeBoard");
   var labels=new List<TextMeshProUGUI>();var words=new List<int>();
   for(int i=0;i<panel.buttonLabels.Length;i++){var label=panel.buttonLabels[i];if(label==null||(old!=null&&label.transform.IsChildOf(old))||(board!=null&&label.transform.IsChildOf(board)))continue;labels.Add(label);words.Add(panel.buttonWords[i]);}
   panel.buttonLabels=labels.ToArray();panel.buttonWords=words.ToArray();
   if(old!=null)Undo.DestroyObjectImmediate(old.gameObject);if(board!=null)Undo.DestroyObjectImmediate(board.gameObject);
   var row=content.Find(PaidRowName);
   if(row==null){var go=UnityEngine.Object.Instantiate(template.gameObject,content);go.name=PaidRowName;Undo.RegisterCreatedObjectUndo(go,"Parade paid row");row=go.transform;}
   Undo.RegisterFullObjectHierarchyUndo(row.gameObject,"Parade paid row");
   var button=row.Find("Button").GetComponent<Button>();var gift=row.Find("Gift").GetComponent<Button>();
   BindCommerceButton(button,pouch,"_Live1");BindCommerceButton(gift,pouch,"_LiveGift1");
   var title=row.Find("Label").GetComponent<TextMeshProUGUI>();var sub=row.Find("Sub").GetComponent<TextMeshProUGUI>();var action=button.transform.Find("Label").GetComponent<TextMeshProUGUI>();
   title.text=item.displayName;sub.text="300 C";action.text=KD_LocalizationEditor.Text(translations,"ja","KD_TreatPouch.WordLiveMore");
   row.gameObject.SetActive(true);button.gameObject.SetActive(true);button.interactable=true;gift.gameObject.SetActive(false);
   pouch.liveProducts=new[]{pouch.liveProducts[0],item};
   pouch.liveLabels=new[]{pouch.liveLabels[0],title};pouch.liveSubLabels=new[]{pouch.liveSubLabels[0],sub};
   pouch.liveButtons=new[]{pouch.liveButtons[0],button.gameObject};pouch.liveButtonComponents=new[]{pouch.liveButtonComponents[0],button};
   pouch.liveButtonImages=new[]{pouch.liveButtonImages[0],button.GetComponent<Image>()};pouch.liveButtonLabels=new[]{pouch.liveButtonLabels[0],action};
   pouch.liveGiftButtons=new[]{pouch.liveGiftButtons[0],gift.gameObject};pouch.liveGiftLabels=new[]{pouch.liveGiftLabels[0],gift.transform.Find("Label").GetComponent<TextMeshProUGUI>()};
   pouch.liveThumbs=new Sprite[]{pouch.liveThumbs!=null&&pouch.liveThumbs.Length>0?pouch.liveThumbs[0]:null,null};
   pouch.hillsideParadePanel=panel;manager.hostingProduct=item;manager.developmentEnabled=false;
   KD_LocalizationEditor.Configure(panel.language,translations,new StringBuilder());Copy(panel.language);
   pouch.eventBandLabel.text=KD_LocalizationEditor.Text(translations,"ja","event.halloween.text");pouch.eventBandRoot.SetActive(!pouch.HalloweenSaleClosedNow());
   BindCommerceButton(pouch.eventBandRoot.GetComponent<Button>(),pouch,"_EventBand");
   panel._Refresh();Copy(panel);Copy(manager);Copy(pouch);LayoutRebuilder.ForceRebuildLayoutImmediate(content);
   EditorSceneManager.MarkSceneDirty(manager.gameObject.scene);Debug.Log("PARADE_PAID_MENU_APPLIED");
  });}
  static void BindCommerceButton(Button b,KD_TreatPouch p,string evt){b.onClick=new Button.ButtonClickedEvent();UnityEventTools.AddStringPersistentListener(b.onClick,UdonSharpEditorUtility.GetBackingUdonBehaviour(p).SendCustomEvent,evt);}
  static void VerifyLivePreviewEntry(List<string> errors){VerifyCommerceEntry(errors);}
  static void VerifyCommerceEntry(List<string> errors){
   var pouch=Only<KD_TreatPouch>();var panel=Only<KH_HalloweenParadePanel>();var m=panel.parade;var item=m.hostingProduct;
   Action<bool,string> need=(ok,msg)=>{if(!ok)errors.Add(msg);};
   var content=pouch.tabScrolls[KD_TreatPouch.TabLive].content;var row=content.Find(PaidRowName);
   need(!m.developmentEnabled,"preview disabled");need(content.Find(PreviewRowName)==null,"preview row removed");need(panel.transform.parent.Find("ParadeBoard")==null,"plaza board removed");
   need(panel.surface!=null&&panel.approveButton!=null&&panel.declineButton!=null,"consent UI preserved");
   need(item!=null&&item.productKey==KH_HalloweenParade.ProductKey&&item.ceCredits==300&&item.IsCE()&&!item.IsFree(),"dedicated paid product");
   if(item!=null){KD_ProductDefinitions.ValidateItem(item);need(pouch.catalog.items.Contains(item)&&item.ledger.products.Contains(item),"catalog/ledger registration");}
   need(pouch.liveProducts.Length==2&&pouch.liveProducts[1]==item&&pouch.hillsideParadePanel==panel,"menu product/host references");need(pouch.liveItems.Length==1,"normal live mapping unchanged");
   need(row!=null&&pouch.liveLabels.Length==2&&pouch.liveLabels[1].transform.parent==row,"normal paid row");
   if(row!=null){VerifyCommerceClick(row.Find("Button").GetComponent<Button>(),pouch,"_Live1",errors);VerifyCommerceClick(row.Find("Gift").GetComponent<Button>(),pouch,"_LiveGift1",errors);}
   VerifyCommerceClick(pouch.eventBandRoot.GetComponent<Button>(),pouch,"_EventBand",errors);
   need(panel.buttonLabels.All(t=>t!=null),"no deleted labels retained");need(panel.resumeSurface==null,"no following resume menu");
   var backing=UdonSharpEditorUtility.GetBackingUdonBehaviour(m);
   need(backing.publicVariables.TryGetVariableValue("developmentEnabled",out bool dev)&&!dev,"backing preview disabled");
   need(backing.publicVariables.TryGetVariableValue("hostingProduct",out VRC.Udon.UdonBehaviour product)&&product==UdonSharpEditorUtility.GetBackingUdonBehaviour(item),"backing paid product");
   var source=File.ReadAllText(ScriptDir+"KH_HalloweenParade.cs");need(source.Contains("!_CanHost(caller) || phase != Idle")&&!source.Contains("if (developmentEnabled)"),"authenticated owner gate without bypass");
  }
  static void VerifyCommerceClick(Button b,KD_TreatPouch p,string evt,List<string> errors){
   var calls=new SerializedObject(b).FindProperty("m_OnClick.m_PersistentCalls.m_Calls");if(calls.arraySize!=1){errors.Add("event count "+evt);return;}var c=calls.GetArrayElementAtIndex(0);
   if(c.FindPropertyRelative("m_Target").objectReferenceValue!=UdonSharpEditorUtility.GetBackingUdonBehaviour(p)||c.FindPropertyRelative("m_MethodName").stringValue!="SendCustomEvent"||c.FindPropertyRelative("m_Arguments.m_StringArgument").stringValue!=evt||c.FindPropertyRelative("m_CallState").intValue==0)errors.Add("click binding "+evt);
  }
  [MenuItem(Menu+"56 - Verify Paid Menu")]
  public static void VerifyPublicPreview(){
   Guard(false);var errors=new List<string>();VerifyCommerceEntry(errors);Directory.CreateDirectory("Temp/halloween-parade");
   bool pending=KD_ProductDefinitions.PendingParade(KD_ProductDefinitions.Get(KH_HalloweenParade.ProductKey));
   File.WriteAllText("Temp/halloween-parade/commerce.txt",DateTime.UtcNow.ToString("o")+"\n"+(errors.Count==0?"PASS: paid menu wiring; no preview bypass; plaza board removed; authenticated owner gate; consent UI preserved.":string.Join("\n",errors))+"\n"+(pending?"PENDING: dedicated Product/Listing IDs; purchasing and hosting unavailable.":"IDs configured; real purchase/gift/network acceptance NOT_TESTED."));
   if(errors.Count>0)throw new InvalidOperationException(string.Join("; ",errors));Debug.Log("PARADE_PAID_MENU_VERIFIED");
  }
 }
}
