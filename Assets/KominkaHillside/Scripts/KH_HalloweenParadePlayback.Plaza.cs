using UnityEngine;
using VRC.SDKBase;
namespace KominkaHillside {
 public partial class KH_HalloweenParadePlayback {
  public Renderer[] plazaRenderers;
  public int[] plazaRendererTool;
  public bool[] plazaRendererPlatform;
  public CanvasGroup[] plazaUiGroups;
  public Behaviour[] plazaRaycasters;
  public Collider[] plazaUiColliders;
  public VRC_Pickup[] plazaPickups;
  public bool[] plazaManagedPickups;
  public KH_WashSelection plazaWashSelection;
  public KH_WashStationCall plazaWashCall;
  private bool _plazaHidden, _plazaBusy;
  private bool[] _plazaRenderOff, _plazaInteract, _plazaBlocks, _plazaRayOn, _plazaColliderOn, _plazaPickupOn, _plazaHeld;
  private float[] _plazaAlpha;
  private float _nextPlazaHeld;
  private void SetPlazaHidden(bool hide) {
   if(plazaRenderers==null||plazaUiGroups==null||plazaRaycasters==null||plazaUiColliders==null||plazaPickups==null)return;
   if(hide!=_plazaHidden){
    if(hide){
     _plazaRenderOff=new bool[plazaRenderers.Length];for(int i=0;i<plazaRenderers.Length;i++)if(plazaRenderers[i]!=null)_plazaRenderOff[i]=plazaRenderers[i].forceRenderingOff;
     _plazaAlpha=new float[plazaUiGroups.Length];_plazaInteract=new bool[plazaUiGroups.Length];_plazaBlocks=new bool[plazaUiGroups.Length];
     for(int i=0;i<plazaUiGroups.Length;i++){var g=plazaUiGroups[i];if(g==null)continue;_plazaAlpha[i]=g.alpha;_plazaInteract[i]=g.interactable;_plazaBlocks[i]=g.blocksRaycasts;g.alpha=0;g.interactable=false;g.blocksRaycasts=false;}
     _plazaRayOn=new bool[plazaRaycasters.Length];for(int i=0;i<plazaRaycasters.Length;i++)if(plazaRaycasters[i]!=null){_plazaRayOn[i]=plazaRaycasters[i].enabled;plazaRaycasters[i].enabled=false;}
     _plazaColliderOn=new bool[plazaUiColliders.Length];for(int i=0;i<plazaUiColliders.Length;i++)if(plazaUiColliders[i]!=null){_plazaColliderOn[i]=plazaUiColliders[i].enabled;plazaUiColliders[i].enabled=false;}
     _plazaPickupOn=new bool[plazaPickups.Length];_plazaHeld=new bool[plazaPickups.Length];for(int i=0;i<plazaPickups.Length;i++)if(plazaPickups[i]!=null){_plazaPickupOn[i]=plazaPickups[i].pickupable;plazaPickups[i].pickupable=false;}
     if(plazaWashSelection!=null)plazaWashSelection._SetPresentationHidden(true);
    }else{
     for(int i=0;i<plazaRenderers.Length;i++)if(plazaRenderers[i]!=null)plazaRenderers[i].forceRenderingOff=_plazaRenderOff[i];
     for(int i=0;i<plazaUiGroups.Length;i++){var g=plazaUiGroups[i];if(g==null)continue;g.alpha=_plazaAlpha[i];g.interactable=_plazaInteract[i];g.blocksRaycasts=_plazaBlocks[i];}
     for(int i=0;i<plazaRaycasters.Length;i++)if(plazaRaycasters[i]!=null)plazaRaycasters[i].enabled=_plazaRayOn[i];
     for(int i=0;i<plazaUiColliders.Length;i++)if(plazaUiColliders[i]!=null)plazaUiColliders[i].enabled=_plazaColliderOn[i];
     for(int i=0;i<plazaPickups.Length;i++)if(plazaPickups[i]!=null&&!plazaManagedPickups[i])plazaPickups[i].pickupable=_plazaPickupOn[i];
     if(plazaWashSelection!=null)plazaWashSelection._SetPresentationHidden(false);
    }
    _plazaHidden=hide;_nextPlazaHeld=0;
    if(hide)ApplyPlazaVisibility();
   }
   if(!hide||Time.time<_nextPlazaHeld)return;
   _nextPlazaHeld=Time.time+.1f;
   bool changed=false;
   for(int i=0;i<plazaPickups.Length;i++){bool held=plazaPickups[i]!=null&&plazaPickups[i].IsHeld;if(held!=_plazaHeld[i]){_plazaHeld[i]=held;changed=true;}}
   bool busy=plazaWashCall!=null&&plazaWashCall._HeldDog()>=0;if(busy!=_plazaBusy){_plazaBusy=busy;changed=true;}
   if(changed)ApplyPlazaVisibility();
  }
  private void ApplyPlazaVisibility(){
   for(int i=0;i<plazaRenderers.Length;i++){var r=plazaRenderers[i];if(r==null)continue;int tool=plazaRendererTool[i];bool visible=tool>=0&&_plazaHeld[tool]||plazaRendererPlatform[i]&&_plazaBusy;r.forceRenderingOff=_plazaRenderOff[i]||!visible;}
  }
 }
}
