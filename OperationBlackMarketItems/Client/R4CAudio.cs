using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using EFT;
using EFT.InventoryLogic;
using HarmonyLib;
using UnityEngine;

namespace R6PortClient
{
    internal static class R4CAudio
    {
        static readonly FieldInfo BridgeField=AccessTools.Field(typeof(BaseSoundPlayer),"playersBridge");
        internal static BaseSoundPlayer.IObserverToPlayerBridge Bridge(BaseSoundPlayer player)
            => (BaseSoundPlayer.IObserverToPlayerBridge)BridgeField.GetValue(player);
        internal static Weapon Weapon(BaseSoundPlayer player)
        {
            var weapon=Bridge(player)?.Weapon?.Item as Weapon;
            bool classicWeapon=weapon!=null&&weapon.TemplateId=="d5f8cd3ff7753fb324c2b2b2";
            bool collectionWeapon=weapon!=null&&CollectionWeapons.Contains(weapon.TemplateId.ToString());
            if((classicWeapon||collectionWeapon)&&player is WeaponSoundPlayer classic)
            {
                // R4-C clips are already mixed to the M4A1 level, the old x7 volume and gain would distort them,
                // so only the collection gets boosted
                if(!classicWeapon&&!IsMastered(classic))
                foreach(var bank in new[]{classic.Body,classic.BodySilenced,classic.Tail,classic.TailSilenced,classic.Doublet,classic.DoubletSilenced})
                    if(bank!=null&&(bank.name.StartsWith("r4c_",StringComparison.Ordinal)||bank.name.StartsWith("obm_",StringComparison.Ordinal))&&BoostedBanks.Add(bank))
                    {
                        if(classicWeapon)bank.BaseVolume*=7f;
                        // raising AudioSource volume past its range didn't make the quiet recordings louder,
                        // so amplify decoded copies and keep the originals
                        foreach(var environment in bank.Environments.Where(e=>e!=null))
                            foreach(var distance in environment.Clips.Where(d=>d!=null))
                                for(int i=0;i<distance.Clips.Length;i++)distance.Clips[i]=LouderShot(distance.Clips[i]);
                    }
                if(!IsMastered(classic)&&BoostedPlayers.Add(classic)&&collectionWeapon)
                    foreach(var sound in classic.AdditionalSounds.Where(s=>s.EventName=="R6ReloadLoaded"||s.EventName=="R6ReloadEmpty"))sound.Volume*=.25f;
            }
            return weapon!=null&&(weapon.TemplateId=="d5f8cd3ff7753fb324c2b2b2"||CollectionWeapons.Contains(weapon.TemplateId.ToString()))?weapon:null;
        }
        static bool IsMastered(WeaponSoundPlayer player)=>player.Body!=null&&player.Body.name.StartsWith("obm_siege_mixed_",StringComparison.Ordinal);
        static readonly HashSet<EFT.SoundBank> BoostedBanks=new HashSet<EFT.SoundBank>();
        static readonly HashSet<WeaponSoundPlayer> BoostedPlayers=new HashSet<WeaponSoundPlayer>();
        static readonly Dictionary<AudioClip,AudioClip> ShotCopies=new Dictionary<AudioClip,AudioClip>();
        static AudioClip LouderShot(AudioClip original)
        {
            if(original==null)return null;
            if(ShotCopies.TryGetValue(original,out var cached))return cached;
            original.LoadAudioData();var samples=new float[original.samples*original.channels];
            if(!original.GetData(samples,0))
            {Plugin.Log.LogWarning("OBM shot amplification could not decode "+original.name);return original;}
            double before=0,after=0;
            for(int i=0;i<samples.Length;i++)
            {
                float sample=samples[i];before+=sample*sample;
                // 4x input gain with a soft peak limiter, no hard clipping
                samples[i]=.98f*(float)Math.Tanh(sample*4f/.98f);after+=samples[i]*samples[i];
            }
            var copy=AudioClip.Create(original.name+"_obm_shot_gain4",original.samples,original.channels,original.frequency,false);
            if(!copy.SetData(samples,0)){UnityEngine.Object.Destroy(copy);return original;}
            ShotCopies[original]=copy;ShotCopies[copy]=copy;
            Plugin.Log.LogInfo("OBM decoded shot gain: "+original.name+", RMS multiplier="+(before>0?Math.Sqrt(after/before):0).ToString("F2"));
            return copy;
        }
        static readonly HashSet<string> CollectionWeapons=new HashSet<string>(new[]{"416-C-CARBINE","556XI","89QCB","9x19VSN","AK-12","BERYL762","BOSG122Name","C8-SFW","Enfield-L85A2","F2","JS9","K1A1","M14Name","M590A1","MAC11","MAC38Name","MP5","MP7","PM9","VEC45"}.Select(k=>Plugin.Id("weapon."+k)));
        internal sealed class ReloadState { public float Until; }
        static readonly ConditionalWeakTable<BaseSoundPlayer,ReloadState> Reloads=new ConditionalWeakTable<BaseSoundPlayer,ReloadState>();
        internal sealed class ReloadRequest { public float Until;public float SpeedUntil,Speed=1;public BaseSoundPlayer.SoundElement Continuation; }
        static readonly Dictionary<AudioClip,BaseSoundPlayer.SoundElement[]> SplitClips=new Dictionary<AudioClip,BaseSoundPlayer.SoundElement[]>();
        static readonly ConditionalWeakTable<FirearmsAnimator,BaseSoundPlayer> Players=new ConditionalWeakTable<FirearmsAnimator,BaseSoundPlayer>();
        internal static void BeginReload(FirearmsAnimator animator)
        {
            var request=Requests.GetOrCreateValue(animator);request.Until=Time.unscaledTime+4f;request.Continuation=null;
            if(!Players.TryGetValue(animator,out var player)||player==null)return;
            var weapon=Weapon(player);if(weapon==null)return;
            bool loaded=weapon.Chambers.Any(s=>s.ContainedItem is AmmoItemClass ammo&&!ammo.IsUsed);
            var sound=player.AdditionalSounds.FirstOrDefault(x=>x.EventName==(loaded?"R6ReloadLoaded":"R6ReloadEmpty"));
            var clip=sound?.RandomSoundClip;
            Plugin.Log.LogInfo($"OBM reload start: loaded={loaded}, event={(loaded?"R6ReloadLoaded":"R6ReloadEmpty")}, sound={(sound!=null)}, clip={(clip!=null?clip.name+" "+clip.channels+"ch "+clip.length.ToString("F2")+"s state="+clip.loadState:"null")}");
            if(clip==null)return;
            if(weapon.TemplateId.ToString()==Plugin.Id("weapon.JS9"))
            {
                var timing=player.GetComponentsInChildren<Transform>(true).FirstOrDefault(t=>t.name=="OBM_JS9ReloadTiming");
                if(timing!=null){request.Speed=Mathf.Clamp((loaded?timing.localPosition.x:timing.localPosition.y)/clip.length,.25f,3f);request.SpeedUntil=Time.unscaledTime+clip.length+.2f;animator.SetSpeedParameters(request.Speed);}
                player.PlayRandomClip(new BaseSoundPlayer.SoundElement{Volume=sound.Volume,RollOff=sound.RollOff,SoundClips=new[]{clip}});
                Reloads.GetOrCreateValue(player).Until=Time.unscaledTime+clip.length+.2f;request.Until=0;return;
            }
            if(!SplitClips.TryGetValue(clip,out var split))
            {
                // only move the first magazine out sound, everything else keeps the native timing
                var samples=new float[clip.samples*clip.channels];if(!clip.GetData(samples,0)){Plugin.Log.LogWarning("OBM reload: GetData failed for "+clip.name);return;}
                int firstFrames=Math.Min(clip.samples,(int)(clip.frequency*.32f));
                var first=AudioClip.Create(clip.name+"_early",firstFrames,clip.channels,clip.frequency,false);
                var firstData=new float[firstFrames*clip.channels];Array.Copy(samples,firstData,firstData.Length);first.SetData(firstData,0);
                Array.Clear(samples,0,firstData.Length);
                var rest=AudioClip.Create(clip.name+"_continuation",clip.samples,clip.channels,clip.frequency,false);rest.SetData(samples,0);
                split=new[]{new BaseSoundPlayer.SoundElement{Volume=sound.Volume,RollOff=sound.RollOff,SoundClips=new[]{first}},new BaseSoundPlayer.SoundElement{Volume=sound.Volume,RollOff=sound.RollOff,SoundClips=new[]{rest}}};
                SplitClips.Add(clip,split);
            }
            player.PlayRandomClip(split[0]);request.Continuation=split[1];
        }
        internal static readonly ConditionalWeakTable<FirearmsAnimator,ReloadRequest> Requests=new ConditionalWeakTable<FirearmsAnimator,ReloadRequest>();
        internal static bool Handle(BaseSoundPlayer player,string name)
        {
            var weapon=Weapon(player);if(weapon==null)return true;
            var currentAnimator=Bridge(player)?.FirearmsAnimator;
            if(currentAnimator!=null){Players.Remove(currentAnimator);Players.Add(currentAnimator,player);}
            if(name.StartsWith("Snd",StringComparison.Ordinal))name=name.Substring(3);
            var state=Reloads.GetOrCreateValue(player);
            if(name=="MagPullOut"||name=="MagOut")
            {
                if(Time.unscaledTime<state.Until)return false;
                var animator=Bridge(player)?.FirearmsAnimator;
                // mag inspect uses the same sound event, only a real reload starts the Siege reload sequence
                if(animator==null||!Requests.TryGetValue(animator,out var request)||Time.unscaledTime>=request.Until)
                {Plugin.Log.LogInfo($"OBM reload: native {name} kept (animator={(animator!=null)}, request armed={(animator!=null&&Requests.TryGetValue(animator,out var r)&&Time.unscaledTime<r.Until)})");return true;}
                bool loaded=weapon.Chambers.Any(s=>s.ContainedItem is AmmoItemClass ammo&&!ammo.IsUsed);
                var sound=player.AdditionalSounds.FirstOrDefault(s=>s.EventName==(loaded?"R6ReloadLoaded":"R6ReloadEmpty"));
                if(sound==null||sound.SoundClips==null||sound.SoundClips.Length==0)return true;
                if(request.Continuation!=null)sound=request.Continuation;
                request.Until=0;request.Continuation=null;
                state.Until=Time.unscaledTime+sound.SoundClips.Max(c=>c.length)+.2f;
                Plugin.Log.LogInfo($"OBM reload: Siege sound instead of native {name}");
                player.PlayRandomClip(sound);return false;
            }
            // don't play native magazine/bolt sounds over the Siege sequence
            if(Time.unscaledTime<state.Until&&(name.StartsWith("Mag",StringComparison.Ordinal)||name.StartsWith("Bolt",StringComparison.Ordinal)||name.StartsWith("Handle",StringComparison.Ordinal)||name=="OffBoltCatch"||name=="OnBoltCatch"))return false;
            return true;
        }
    }
    [HarmonyPatch]
    internal static class R4CReloadTrigger
    {
        static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(FirearmsAnimator),"Reload",new[]{typeof(bool)});
            yield return AccessTools.Method(typeof(FirearmsAnimator),"ReloadFast",new[]{typeof(bool)});
        }
        static void Postfix(FirearmsAnimator __instance,bool __0){if(__0)R4CAudio.BeginReload(__instance);}
    }
    [HarmonyPatch(typeof(FirearmsAnimator),nameof(FirearmsAnimator.SetSpeedParameters))]
    internal static class JS9ReloadSpeed
    {
        static void Prefix(FirearmsAnimator __instance,ref float __0)
        {if(R4CAudio.Requests.TryGetValue(__instance,out var request)&&Time.unscaledTime<request.SpeedUntil)__0=request.Speed;}
    }
    [HarmonyPatch(typeof(WeaponSoundPlayer),"FireBullet")]
    internal static class R4CSingleFireSound
    {
        static readonly FieldInfo IsFiringField=AccessTools.Field(typeof(WeaponSoundPlayer),"_isFiring");
        internal sealed class State { public EFT.SoundBank Body,Silenced;public bool NonAuto,SharedBodySingle; }
        static void Prefix(WeaponSoundPlayer __instance,bool multiShot,out State __state)
        {
            __state=null;
            var weapon=R4CAudio.Weapon(__instance);
            if(weapon==null||weapon.SelectedFireMode!=Weapon.EFireMode.single||__instance.Doublet==null)return;
            var activeBody=__instance.IsSilenced?__instance.BodySilenced:__instance.Body;
            bool mixedLoop=activeBody!=null&&activeBody.name.StartsWith("obm_siege_mixed_",StringComparison.Ordinal)&&activeBody.name.EndsWith(".auto",StringComparison.Ordinal);
            bool classicLoop=weapon.TemplateId=="d5f8cd3ff7753fb324c2b2b2"&&activeBody!=null&&activeBody.name.StartsWith("r4c_auto",StringComparison.Ordinal);
            if(!multiShot&&!__instance.Non_auto&&(mixedLoop||classicLoop))
            {
                // one trigger pull uses the same recorded attack in both fire modes: keep the auto body and let the
                // native queue switch to its tail on the next beat. Semi only, pump and break action keep full one shots.
                __state=new State{SharedBodySingle=true};
                return;
            }
            if(weapon.TemplateId!="d5f8cd3ff7753fb324c2b2b2"&&!__instance.Doublet.name.StartsWith("obm_",StringComparison.Ordinal))return;
            __state=new State{Body=__instance.Body,Silenced=__instance.BodySilenced,NonAuto=__instance.Non_auto};
            __instance.Body=__instance.Doublet;__instance.BodySilenced=__instance.DoubletSilenced;__instance.Non_auto=true;
        }
        // restore even if native playback throws
        static Exception Finalizer(WeaponSoundPlayer __instance,State __state,Exception __exception)
        {
            if(__state?.SharedBodySingle==true)
            {
                // in single fire the trigger can stay held, stop right after the shot.
                // FireBullet may already have queued the tail, never queue it twice.
                if(__exception==null&&IsFiringField.GetValue(__instance) is bool firing&&firing)
                    __instance.StopFiringLoop();
            }
            else if(__state!=null){__instance.Body=__state.Body;__instance.BodySilenced=__state.Silenced;__instance.Non_auto=__state.NonAuto;}
            return __exception;
        }
    }
    [HarmonyPatch]
    internal static class R4CReloadSound
    {
        static IEnumerable<MethodBase> TargetMethods()
        {
            foreach(var name in new[]{"SoundEventHandler","SoundAtPointEventHandler","EventReceiver"})
                yield return AccessTools.Method(typeof(BaseSoundPlayer),name);
        }
        static bool Prefix(BaseSoundPlayer __instance,string __0)=>R4CAudio.Handle(__instance,__0);
    }
}
