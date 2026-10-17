using FFOne.Network;
using UnityEngine;
namespace FFOne.World
{
    public sealed partial class NetworkWorldController
    {
        public BuffState Buffs { get; private set; } = new();
        public bool BuffMovementBlocked => Buffs.Has(10) || Buffs.Has(11) || Buffs.Has(12);
        public void SetBuffState(BuffState value)
        {
            Buffs = value ?? new BuffState();
            if (Player) { var presentation = Player.GetComponent<NativeBuffPresentation>() ?? Player.gameObject.AddComponent<NativeBuffPresentation>(); presentation.SetCondition(Buffs.Condition, View); }
        }
        public void RefreshBuffPresentation()
        { if (Player) Player.GetComponent<NativeBuffPresentation>()?.SetCondition(Buffs.Condition, View); }
        public static float ResolveBuffSpeed(float baseSpeed, int positive, int negative, float vehicleSpeed = 0)
            => Mathf.Max(0, Mathf.Max(vehicleSpeed, baseSpeed + positive / 100f) - negative / 100f);
        public float BuffJumpVelocity => PersonalVehicleMounted ? 5.68f : Mathf.Max(0, 5.68f + Buffs.Value(3) / 100f);
        float BuffGroundSpeed(float baseSpeed) => BuffMovementBlocked ? 0 : ResolveBuffSpeed(baseSpeed, Buffs.Value(1), Buffs.Value(8));
        float BuffVehicleSpeed => BuffMovementBlocked ? 0 : ResolveBuffSpeed(6, Buffs.Value(1), Buffs.Value(8), PersonalVehicleSpeed);
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        // Like vehicle acceptance: bypass window focus, retain the actual speed
        // resolver, CharacterController collision and normal movement notification.
        internal Vector3 AcceptBuffMotorStep(float forward, float delta)
        {
            if (!IsReady || PersonalVehicleMounted || delta <= 0 || delta > .1f) throw new System.InvalidOperationException("Buff motor acceptance is not ready.");
            var horizontal = HorizontalMovement(0, forward, delta); var before = Player.position;
            motor.Move((horizontal + Vector3.down * .1f) * delta);
            Velocity = (Player.position - before) / delta; Stream.Anchor = Player.position;
            Moved?.Invoke(Player.position, yaw); UpdateCamera(); return horizontal;
        }
        internal Vector3 AcceptBuffJumpStep(float delta)
        {
            if (!IsReady || PersonalVehicleMounted || BuffMovementBlocked || !motor.isGrounded || delta <= 0 || delta > .1f)
                throw new System.InvalidOperationException("Buff jump acceptance is not grounded and ready.");
            var before = Player.position; LaunchJump(); motor.Move(Vector3.up * vertical * delta);
            Velocity = (Player.position - before) / delta; Stream.Anchor = Player.position;
            Moved?.Invoke(Player.position, yaw); UpdateCamera(); return Player.position - before;
        }
#endif
    }
}
