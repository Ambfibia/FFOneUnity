using System;
using FFOne.Content;
using FFOne.UI;
using UnityEngine;
using Random = UnityEngine.Random;

namespace FFOne.World
{
    internal static class NanoClipNames
    {
        // The accepted GLBs retain these two case-sensitive source names.
        internal static string Resolve(string resource, string motion) => (resource, motion) switch
        {
            ("characters/nanos/nano_cheese/nano_cheese", "skill1") => "Skill1",
            ("characters/nanos/nano_ghostfreak/nano_ghostfreak", "stand1") => "Stand1",
            _ => motion
        };
    }

    // The owner drives identity and lifetime. The model is a sibling so its follow
    // interpolation is in world space and the avatar's render/bone queries exclude it.
    [DisallowMultipleComponent]
    internal sealed partial class WorldNanoCompanion : MonoBehaviour
    {
        internal int NanoId { get; private set; }
        internal GameObject Model { get; private set; }
        NativeModelAssets.Request request;
        NetworkWorldEntities replicas;
        Camera view;
        Animation animationPlayer;
        Vector3 position;
        Quaternion rotation;
        float side, patternAt, orbitSpeed, turnSpeed, pulseHeight, pulseHz, offsetYaw;
        int orbit, turn, pulse;
        bool compact, calling, withdrawing;
        string idle, modelResource;
        int hand = -1;

        internal void Initialize(NetworkWorldEntities entities, Camera worldCamera, bool leftSide)
        {
            replicas = entities;
            world = entities.GetComponentInParent<NetworkWorldController>();
            view = worldCamera;
            side = leftSide ? -1 : 1;
        }

        internal bool RewardPreview { get; set; }
        internal bool SkillPresentationFinished => Model && !calling && pendingSkill == null &&
            activeClip is not ("skill1" or "skill2" or "skill3");

        internal void SetWeapon(int id)
        {
            if (hand == id) return;
            hand = id;
            compact = id > 0 && WorldWeaponCatalog.Load().Find(id)?.Projectile == true;
        }

        internal void SetNano(int id)
        {
            if (id < 0)
            {
                // The remote -1 sentinel requests the authored withdraw animation.
                if (withdrawing) return;
                if (Model && Play("withdraw"))
                {
                    NanoId = 0;
                    withdrawing = true;
                    return;
                }
                Clear();
                return;
            }
            if (id == NanoId && !withdrawing) return;
            bool hide = id == 0 && (Model || request != null);
            Vector3 previous = position;
            var outgoing = presentation;
            var outgoingRotation = rotation;
            RetireModel();
            if (hide)
            {
                SpawnDetachedEffect(10, previous, outgoingRotation);
                if (outgoing != null) QueueVoice(outgoing.voiceOwner, outgoing.dismiss, true);
            }
            if (id == 0) return;

            NanoId = id;
            var content = GameplayUiCatalog.Load().Nano(id);
            if (string.IsNullOrEmpty(content?.NanoModelResource))
            {
                replicas.ContentFailure($"Nano {id}: accepted model route is missing.");
                return;
            }
            presentation = NanoPresentationCatalog.Find(content.NanoModelResource);
            modelResource = content.NanoModelResource;
            offsetYaw = transform.eulerAngles.y;
            rotation = Quaternion.Euler(0, offsetYaw, 0);
            position = transform.position + rotation * new Vector3(.7f * side, 1.12f, 0);
            NewPattern();
            request = NativeModelAssets.LoadAsync(content.NanoModelResource);
            if (content.NanoStyle.HasValue) SpawnDetachedEffect(527 + content.NanoStyle.Value, position, rotation);
        }

        void LateUpdate()
        {
            UpdateSounds();
            if (!Model)
            {
                if (request == null || !request.isDone) return;
                if (!(request.asset is GameObject prefab))
                {
                    replicas.ContentFailure($"Nano {NanoId}: accepted model failed to load.");
                    request.Dispose(); request = null;
                    return;
                }
                Model = Instantiate(prefab, position, rotation, transform.parent);
                Model.name = $"World Nano {NanoId} ({gameObject.name})";
                foreach (var collider in Model.GetComponentsInChildren<Collider>(true)) collider.enabled = false;
                animationPlayer = Model.GetComponentInChildren<Animation>(true);
                NativeNanoFaceTrack.Attach(Model, presentation);
                calling = Play("call");
                if (!calling && !PlayPendingSkill()) PlayIdle();
            }
            AdvanceEvents();
            if (withdrawing && (clipEnded || !animationPlayer || !animationPlayer.IsPlaying("withdraw")))
            {
                RetireModel();
                return;
            }
            if (calling && PresentationEnded)
            {
                calling = false;
                if (!PlayPendingSkill()) PlayIdle();
            }
            else if (!calling && !withdrawing && activeClip != null && PresentationEnded)
                PlayIdle();

            if (RewardPreview)
            {
                position = transform.position;
                rotation = transform.rotation;
            }
            else Follow(Time.deltaTime);
            Model.transform.SetPositionAndRotation(position, rotation);
        }

        bool Play(string clip)
        {
            string assetClip = NanoClipNames.Resolve(modelResource, clip);
            if (!animationPlayer || !animationPlayer[assetClip]) return false;
            animationPlayer.cullingType = AnimationCullingType.AlwaysAnimate;
            animationPlayer[assetClip].wrapMode = WrapMode.Once;
            animationPlayer[assetClip].time = 0;
            animationPlayer.CrossFade(assetClip, .1f);
            StartEvents(clip, assetClip);
            return true;
        }

        void PlayIdle()
        {
            idle = "stand" + Random.Range(1, 4);
            if (Play(idle)) return;
            idle = Play("stand1") ? "stand1" : null;
        }

        void NewPattern()
        {
            patternAt = Time.time;
            orbit = Direction(Random.Range(0, 10));
            pulse = compact ? 2 : Random.Range(0, 3);
            turn = -Direction(Random.Range(0, 10));
            orbitSpeed = Random.Range(1f, 2f) * Mathf.Rad2Deg;
            turnSpeed = Random.Range(.5f, 2f) * Mathf.Rad2Deg;
            pulseHeight = Random.Range(.1f, .3f);
            pulseHz = Random.Range(.5f, 1.5f);
        }

        static int Direction(int choice) => choice == 0 ? 1 : choice == 1 ? -1 : 0;

        void Follow(float delta)
        {
            bool skill = activeClip is "skill1" or "skill2" or "skill3";
            if (skill) rotation = transform.rotation;
            if (!calling && !skill && Time.time - patternAt > 10)
            {
                if (Random.Range(0, 3) == 0) NewPattern();
                else patternAt = Time.time;
            }
            float phase = pulseHz * patternAt * Mathf.PI;
            float height = 1.12f + (pulse == 0 ? Mathf.Sin(phase) : pulse == 1 ? Mathf.Cos(phase) : 0) * pulseHeight;
            if (compact)
            {
                offsetYaw = Mathf.LerpAngle(offsetYaw, transform.eulerAngles.y, delta * 5);
                var target = transform.position + Quaternion.Euler(0, offsetYaw, 0) * new Vector3(side * .45f, height, -.45f);
                var horizontal = target - position; horizontal.y = 0;
                float rate = Mathf.Lerp(3, 18, 1 - horizontal.magnitude / 3);
                position += horizontal * Mathf.Clamp01(delta * rate);
                position.y = Mathf.Lerp(position.y, target.y, delta * 6);
                if (!calling && !skill) rotation = Quaternion.Slerp(rotation, transform.rotation, delta * 4);
                return;
            }
            if (!calling && !skill)
            {
                offsetYaw += orbit * orbitSpeed * delta;
                rotation *= Quaternion.Euler(0, turn * turnSpeed * delta, 0);
            }
            var destination = transform.position + Quaternion.Euler(0, offsetYaw, 0) * new Vector3(.7f * side, height, 0);
            position = Vector3.LerpUnclamped(position, destination, delta * 4.25f);
        }

        void RetireModel()
        {
            CancelPendingModelSounds();
            presentation = null; modelResource = activeClip = activeAssetClip = pendingSkill = null; events = null;
            if (Model) { Model.SetActive(false); Destroy(Model); }
            Model = null; animationPlayer = null;
            request?.Dispose(); request = null;
            NanoId = 0; calling = withdrawing = false; idle = null;
        }

        internal void Clear()
        {
            RetireModel();
            ClearPresentation();
        }

        void OnDisable() => Clear();
    }
}
