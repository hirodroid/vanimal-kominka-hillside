using UnityEngine;
using VRC.SDKBase.Editor.BuildPipeline;
namespace KominkaDogRun.Editor
{
    // Read-only VRChat build gate: changed catalog data must be applied to the scene.
    // IMP-INF-010: 未訳キーのある言語 (部分訳の試験言語) は、初回の自動言語解決でその言語の来場者へ
    // 代替言語の文が混ざって出るので、公開ビルドに混ぜない。このコールバックは Build & Test と
    // アップロードを区別できないので、Menu Finish/7 - Allow Partial Translations (Next Build Only) の許可は
    // **次の 1 回のビルドで消費する** (許可したまま忘れてアップロードする経路を残さない)。
    public class KD_LocalizationBuildGuard : IVRCSDKBuildRequestedCallback
    {
        public const string AllowPartialKey="KD.Localization.AllowPartial";
        public int callbackOrder { get { return 10; } }
        public bool OnBuildRequested(VRCSDKRequestedBuildType requestedBuildType)
        {
            if(requestedBuildType==VRCSDKRequestedBuildType.Avatar)return true;
            var root=KD_SceneObjects.Find("KD_Language");
            try
            {
                var catalog=KD_LocalizationEditor.Load();
                KD_LocalizationEditor.ValidateScene(root==null?null:root.GetComponent<KD_Language>(),catalog);
                KD_ShopKeeperSetupEditor.ValidateConstMatchesCatalog(catalog);   // IMP-ENV-020: const (字形宣言) と文言表 ja の一致
                var incomplete=KD_LocalizationEditor.IncompleteLanguages(catalog);
                if(incomplete.Length>0)
                {
                    if(!UnityEditor.SessionState.GetBool(AllowPartialKey,false))
                        throw new System.InvalidOperationException("Partial translations: "+string.Join(", ",incomplete)+" (missing keys). Finish them or, for one Build & Test only, run Tools/KominkaHillside/Menu/11a Allow Partial Translations (Next Build Only).");
                    UnityEditor.SessionState.SetBool(AllowPartialKey,false);
                    Debug.LogWarning("[KD_Localization] Building with partial translations (allowance consumed by this build): "+string.Join(", ",incomplete)+". Do not upload this build; the next build is blocked again.");
                }
                return true;
            }
            catch(System.Exception e)
            {
                // 部分訳の拒否は Setup で直るものではないので、Setup への誘導は配線不一致のときだけ付ける
                Debug.LogError("[KD_Localization] "+e.Message+(e.Message.StartsWith("Partial translations")?"":" Run Tools/KominkaHillside/Menu/11 Setup Localization."));
                return false;
            }
        }
    }
}
