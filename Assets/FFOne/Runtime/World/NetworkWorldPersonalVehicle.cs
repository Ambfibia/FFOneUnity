using FFOne.UI;
using FFOne.Content;
using UnityEngine;

namespace FFOne.World
{
    public sealed partial class NetworkWorldController
    {
        public bool PersonalVehicleMounted => avatar && avatar.PersonalVehicleId > 0;
        public int PersonalVehicleId => avatar ? avatar.PersonalVehicleId : 0;
        public float PersonalVehicleSpeed { get; private set; }
        readonly PersonalVehicleMotion vehicleMotion = new();
        AudioSource vehicleEngine;
        AudioSource vehicleSounds;
        int engineVehicleId;
        public void SetPersonalVehicle(int id)
        {
            if (!avatar) return;
            bool changed = id != PersonalVehicleId;
            PersonalVehicleSpeed = id > 0 ? (GameplayUiCatalog.Load().Item(10, id)?.VehicleSpeed ?? 0) / 100f : 0;
            if (!avatar.SetPersonalVehicle(id)) { error = avatar.LastError; MovementEnabled = false; }
            if (changed)
            {
                vehicleMotion.Reset();
                avatar.PlayerAnimation?.ResetLocomotion();
                ConfigureVehicleEngine(id);
            }
        }
        float GroundSpeed(float forward) => BuffGroundSpeed(forward < 0 ? 3 : 6);
        Vector3 HorizontalMovement(float side, float forward, float delta)
        {
            if (!PersonalVehicleMounted) return Quaternion.Euler(0, MovementYaw, 0) * new Vector3(side, 0, forward).normalized * GroundSpeed(forward);
            var movement = vehicleMotion.Step(side, forward, MovementYaw, BuffVehicleSpeed, motor.isGrounded, delta);
            if ((side != 0 || forward != 0) && movement.sqrMagnitude > .001f)
                foreach (var hit in Physics.RaycastAll(Player.position + Vector3.up, movement.normalized, .5f, ~(1 << 31), QueryTriggerInteraction.Ignore))
                {
                    if (hit.collider.transform.IsChildOf(Player)) continue;
                    movement = vehicleMotion.Bounce(); PlayVehicleSound("vehicle_jumplnd"); break;
                }
            if (vehicleEngine)
            {
                vehicleEngine.volume = VehicleEngineGain(engineVehicleId);
                string cue = GameplayUiCatalog.Load().Item(10, engineVehicleId)?.VehicleEngineSound ?? "";
                vehicleEngine.pitch = cue.StartsWith("Vehicle_TimeSquad", System.StringComparison.OrdinalIgnoreCase) ? 1 : Mathf.Max(1, movement.magnitude / (PersonalVehicleSpeed * 1.5f) * 2);
            }
            if (vehicleSounds) vehicleSounds.volume = FrontendController.AudioGain(Preferences, "effects");
            return movement;
        }
        void ConfigureVehicleEngine(int id)
        {
            if (avatar?.PlayerAnimation != null)
            { avatar.PlayerAnimation.SourceSound -= PlayVehicleAnimationSound; if (id > 0) avatar.PlayerAnimation.SourceSound += PlayVehicleAnimationSound; }
            if (vehicleEngine) { vehicleEngine.Stop(); Destroy(vehicleEngine); vehicleEngine = null; }
            if (vehicleSounds) { vehicleSounds.Stop(); Destroy(vehicleSounds); vehicleSounds = null; }
            engineVehicleId = id; if (id <= 0 || !Player) return;
            // Jump/collision cues use the ordinary SFX gain independently of the
            // accepted board engine gain and the engine's speed-dependent pitch.
            vehicleSounds = Player.gameObject.AddComponent<AudioSource>(); vehicleSounds.playOnAwake = false;
            vehicleSounds.spatialBlend = 0; vehicleSounds.volume = FrontendController.AudioGain(Preferences, "effects");
            string cue = GameplayUiCatalog.Load().Item(10, id)?.VehicleEngineSound;
            if (string.IsNullOrWhiteSpace(cue)) return;
            var clip = ContentAssets.Load<AudioClip>("audio/sfx/vehicles/" + System.IO.Path.GetFileNameWithoutExtension(cue).ToLowerInvariant());
            if (!clip) { Debug.LogWarning("Missing accepted personal vehicle engine audio: " + cue); return; }
            vehicleEngine = Player.gameObject.AddComponent<AudioSource>(); vehicleEngine.playOnAwake = false;
            vehicleEngine.clip = clip; vehicleEngine.loop = true; vehicleEngine.spatialBlend = 0;
            vehicleEngine.volume = VehicleEngineGain(id); vehicleEngine.Play();
        }
        float VehicleEngineGain(int id) => FrontendController.AudioGain(Preferences, "effects") * (GameplayUiCatalog.Load().Item(10, id)?.VehicleEquipType == 1 ? .5f : 1);
        void PlayVehicleSound(string cue)
        {
            var clip = ContentAssets.Load<AudioClip>("audio/sfx/vehicles/" + cue);
            if (vehicleSounds && clip) vehicleSounds.PlayOneShot(clip, .15f);
        }
        void PlayVehicleAnimationSound(string cue)
        {
            var clip = ContentAssets.Load<AudioClip>("audio/sfx/vehicles/" + System.IO.Path.GetFileNameWithoutExtension(cue).ToLowerInvariant());
            if (vehicleSounds && clip) vehicleSounds.PlayOneShot(clip);
        }
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        // Live acceptance drives the production momentum resolver and CharacterController. It
        // bypasses OS focus only; collision, authored scene geometry and network movement remain active.
        internal Vector3 AcceptVehicleMotorStep(float side, float forward, float delta)
        {
            if (!PersonalVehicleMounted || !IsReady || delta <= 0 || delta > .1f) throw new System.InvalidOperationException("Vehicle motor acceptance is not ready.");
            var horizontal = HorizontalMovement(side, forward, delta); var before = Player.position;
            motor.Move((horizontal + Vector3.down * .1f) * delta);
            Velocity = (Player.position - before) / delta; MoveDirection = WorldCoordinates.Direction(side, forward);
            avatar.PlayLocomotion(horizontal.sqrMagnitude < .01f ? "stand1" : forward < 0 ? "runback" : "run");
            Stream.Anchor = Player.position; Moved?.Invoke(Player.position, yaw); UpdateCamera();
            return horizontal;
        }
#endif
    }
}
