// Generated from the saved magazine placement and the donor idle pose.
using System.Collections.Generic;
using UnityEngine;
namespace R6PortClient {
internal static class ApprovedMagazinePose {
internal sealed class Pose { public Vector3 Position; public Quaternion Rotation; }
static readonly Dictionary<string,Pose> Poses=new Dictionary<string,Pose> {
{"030bec3a83a996bece613c24",new Pose {Position=new Vector3(-0.000140320961f,0.0401462308f,-0.0208722551f),Rotation=new Quaternion(1.8544616e-06f,-8e-14f,2.10726726e-14f,1f)}},
{"630d6f07fb53dc078c826e0a",new Pose {Position=new Vector3(-0.00224031481f,0.0454462508f,-0.0370722058f),Rotation=new Quaternion(-1.92295712e-15f,2e-14f,-9.36208618e-15f,1f)}},
{"b115d39dfd6fcef8957315b5",new Pose {Position=new Vector3(0.0140130781f,0.0307190892f,0.0702033284f),Rotation=new Quaternion(2.5288116e-07f,2.60769736e-08f,4.47034449e-08f,1f)}},
{"ecca3208fa94a5ecb969c454",new Pose {Position=new Vector3(3.04200416e-07f,0.00433700119f,2.14163954e-05f),Rotation=new Quaternion(0f,-6.827373e-20f,-1.6081781e-14f,1f)}},
{"b05b9c1653f766b87db942c7",new Pose {Position=new Vector3(4.91217802e-05f,0.0452370308f,-4.86658408e-05f),Rotation=new Quaternion(0f,0f,0f,1f)}},
{"85bb9848ccbced908225d5df",new Pose {Position=new Vector3(0.000149110162f,0.0053371802f,-0.0538485775f),Rotation=new Quaternion(-1.8250912e-14f,-3.9063495e-20f,2.0750135e-15f,1f)}},
{"e2af38c3f8b7b8e1ab8ec840",new Pose {Position=new Vector3(-0.000140329602f,0.039246231f,-0.0189721808f),Rotation=new Quaternion(-1.92295712e-15f,2e-14f,-9.36208618e-15f,1f)}},
{"7fd0f3a01d8790c09e1241c0",new Pose {Position=new Vector3(-0.000140312914f,0.32174636f,-0.055572236f),Rotation=new Quaternion(-1.92295712e-15f,2e-14f,-9.36208618e-15f,1f)}},
{"f0a8f11c636eeac8bc8be0a6",new Pose {Position=new Vector3(0.00606036711f,0.0209310384f,0.027223628f),Rotation=new Quaternion(1.85446141e-06f,-8.03889459e-14f,-1.60819215e-14f,1f)}},
{"ccdb99af4f6f60f7fb978a1b",new Pose {Position=new Vector3(-0.00131532462f,0.0247625f,-0.0105640845f),Rotation=new Quaternion(0.0823135562f,9.65871216e-05f,8.99500949e-05f,0.996606496f)}},
{"f4da10138e9c719409c7c53b",new Pose {Position=new Vector3(-4.03201682e-05f,0.00924621059f,-0.0663721905f),Rotation=new Quaternion(-1.92295712e-15f,2e-14f,-9.36208618e-15f,1f)}},
{"893842be5ba6a981ae086630",new Pose {Position=new Vector3(6.88797169e-06f,0.017189261f,0.010371745f),Rotation=new Quaternion(0f,-5.853287e-20f,-1.6081778e-14f,1f)}},
{"fbef8beb7b56954316d60559",new Pose {Position=new Vector3(0.016635289f,-0.00359981242f,0.0871606725f),Rotation=new Quaternion(9.27230703e-07f,-1.57520877e-20f,-1.6081774e-14f,1f)}},
{"bb89ede381716cf080ce560d",new Pose {Position=new Vector3(3.52768909e-05f,0.0187003702f,0.0216606873f),Rotation=new Quaternion(9.27230703e-07f,-1.57520877e-20f,-1.6081774e-14f,1f)}},
{"34ef3e363e754a4d86299211",new Pose {Position=new Vector3(8.2545459e-05f,-0.0377251186f,-0.11028892f),Rotation=new Quaternion(1.43299287e-06f,-1.36660871e-12f,9.53674262e-07f,1f)}},
{"b57f593be9a9a8a224b65406",new Pose {Position=new Vector3(3.58565048e-05f,0.0222424042f,-0.0115990897f),Rotation=new Quaternion(4.2146848e-07f,-8.03888412e-14f,2.07511734e-15f,1f)}},
{"481e45931e1c7f34baea1180",new Pose {Position=new Vector3(0.00333526969f,0.000500143949f,0.110460733f),Rotation=new Quaternion(9.27230703e-07f,-1.57520877e-20f,-1.6081774e-14f,1f)}},
{"a7adaeb8412ccb9b67b0eb99",new Pose {Position=new Vector3(2.48127068e-05f,0.00324746511f,-0.0945029476f),Rotation=new Quaternion(1.85446141e-06f,-8.03887432e-14f,2.84215215e-14f,1f)}},
};
internal static bool Apply(string id,Transform item) { if(!Poses.TryGetValue(id,out var pose))return false;item.localPosition=pose.Position;item.localRotation=pose.Rotation;return true; }
}}
