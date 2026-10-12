using UnityEngine;
namespace R6PortClient
{
    // The eyelet pivots at its mount on the weapon, the charm follows a cached point on the link.
    // No rigidbodies inside the weapon hierarchy.
    [DefaultExecutionOrder(-100)]
    public class CharmLinkMotion:MonoBehaviour
    {
        Quaternion rest;float angle,velocity;
        void Awake(){rest=transform.localRotation;}
        void OnDisable(){transform.localRotation=rest;angle=velocity=0;}
        void LateUpdate()
        {
            if(transform.parent==null)return;
            float dt=Mathf.Min(Time.deltaTime,.033f);if(dt<=0)return;
            var gravity=Quaternion.Inverse(rest)*transform.parent.InverseTransformDirection(Vector3.down);
            // the visual is Y-up (rotated 90 degrees under the weapon bone), pitch around local X
            float goal=Mathf.Clamp(Mathf.Atan2(-gravity.z,-gravity.y)*Mathf.Rad2Deg,-55,55);
            velocity+=(Mathf.DeltaAngle(angle,goal)*36-velocity*8)*dt;
            angle=Mathf.Clamp(angle+velocity*dt,-55,55);
            transform.localRotation=rest*Quaternion.AngleAxis(angle,Vector3.right);
        }
    }
}
