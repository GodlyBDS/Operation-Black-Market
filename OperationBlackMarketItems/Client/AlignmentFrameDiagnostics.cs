using System.Linq;
using UnityEngine;
namespace R6PortClient {
// Logs the weapon frame once it settled, for checking alignment against screenshots.
// Read only, changes nothing.
public class AlignmentFrameDiagnostics : MonoBehaviour {
    static int remaining=28;
    int frames;
    void LateUpdate() {
        if(++frames!=90||remaining<=0)return;
        remaining--;
        var root=transform;while(root.parent!=null&&root.parent.GetComponent<WeaponPrefab>()==null)root=root.parent;
        var palm=root.GetComponentsInChildren<Transform>(true).FirstOrDefault(t=>t.name=="Base HumanRPalm");
        var body=transform.Find("OBM_SourceFrame");
        var scope=transform.Find("OBM_ApprovedSocket_mod_scope");
        var mag=transform.Find("OBM_ApprovedOwnMagazine");
        Plugin.Log.LogInfo("OBM settled alignment: model="+root.name+", body="+(body!=null?body.localPosition.ToString("F5"):"missing")+", rightPalmInWeapon="+(palm!=null?transform.InverseTransformPoint(palm.position).ToString("F5"):"simple/no palm")+", approvedScope="+(scope!=null?scope.localPosition.ToString("F5"):"missing")+", approvedMagazine="+(mag!=null?mag.localPosition.ToString("F5"):"internal"));
    }
}
}
