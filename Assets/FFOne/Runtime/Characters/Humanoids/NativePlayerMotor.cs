using UnityEngine;

namespace FFOne
{
    public static class NativePlayerMotor
    {
        public const float Gravity = 10;
        public const float JumpMultiplier = 1.45f;

        public static void Teleport(CharacterController motor, Vector3 position)
        {
            bool enabled = motor.enabled;
            motor.enabled = false; motor.transform.position = position; motor.enabled = enabled;
            // Disabling/re-enabling the capsule retains isGrounded from the old
            // location in current PhysX. Clear that contact before probing anew.
            if (enabled) motor.Move(Vector3.zero);
        }

        // cnAvatarThirdPersonMove.ExecuteJump / UpdateAirControl: holding Jump
        // keeps normal gravity for the first half-second; releasing cuts ascent.
        public static float AdvanceJump(float velocity, bool held, float age, float delta, bool variable = true)
        {
            float gravity = Gravity;
            if (variable && (!held || age >= .5f))
            {
                gravity = 20;
                if (!held && velocity > 3) velocity *= .25f;
            }
            return Mathf.Max(-10, velocity - gravity * delta);
        }

        public static bool IsGrounded(CharacterController motor, float vertical)
            // PhysX can support the capsule on a narrow rock edge which a
            // smaller downward probe misses. Preserve its actual Below contact.
            => motor && motor.enabled && vertical <= 0 &&
                (motor.isGrounded || ProbeGround(motor, motor.skinWidth + .08f, out _));

        public static CollisionFlags Step(CharacterController motor, Vector3 horizontal, ref float vertical,
            ref float jumpAge, float delta, bool jumpHeld, bool launched, bool variable = true)
        {
            var flags = CollisionFlags.None;
            int count = Mathf.Max(1, Mathf.CeilToInt(delta / (1f / 60)));
            float dt = delta / count;
            for (int i = 0; i < count; i++)
            {
                bool grounded = IsGrounded(motor, vertical);
                if (!(launched && i == 0))
                {
                    if (grounded) vertical = 0;
                    else { jumpAge += dt; vertical = AdvanceJump(vertical, jumpHeld, jumpAge, dt, variable); }
                }
                flags |= Move(motor, horizontal, ref vertical, dt, grounded);
            }
            return flags;
        }

        static bool ProbeGround(CharacterController motor, float distance, out RaycastHit support)
        {
            support = default;
            if (!motor || !motor.enabled) return false;
            float radius = Mathf.Max(.01f, motor.radius - motor.skinWidth);
            Vector3 foot = motor.transform.TransformPoint(motor.center) - Vector3.up * (motor.height * .5f);
            const float lift = .1f;
            float nearest = float.PositiveInfinity;
            foreach (var hit in Physics.SphereCastAll(foot + Vector3.up * (radius + lift), radius,
                Vector3.down, distance + lift, ~(1 << 31), QueryTriggerInteraction.Ignore))
            {
                // Sphere casts starting inside a wall report distance zero and
                // a synthetic up normal. That overlap is not floor support.
                if (hit.distance <= .0001f || hit.collider.transform.IsChildOf(motor.transform) ||
                    hit.normal.y < Mathf.Cos(motor.slopeLimit * Mathf.Deg2Rad)) continue;
                if (hit.distance >= nearest) continue;
                support = hit; nearest = hit.distance;
            }
            return float.IsFinite(nearest);
        }

        public static CollisionFlags Move(CharacterController motor, Vector3 horizontal, ref float vertical, float delta, bool wasGrounded)
        {
            // Keep contact on descending slopes and split hitch-sized movement
            // into capsule sweeps. A micron bias loses Below on modern PhysX.
            CollisionFlags flags = CollisionFlags.None;
            int steps = Mathf.Max(1, Mathf.CeilToInt(delta / (1f / 60)));
            float step = delta / steps;
            for (int i = 0; i < steps; i++)
            {
                var collision = motor.Move((horizontal + Vector3.up * (wasGrounded && vertical <= 0 ? -2 : vertical)) * step);
                flags |= collision;
                if ((collision & CollisionFlags.Above) != 0 && vertical > 0) vertical = 0;
                if ((collision & CollisionFlags.Below) != 0 && vertical < 0) vertical = 0;
            }
            if (wasGrounded && vertical <= 0 && ProbeGround(motor, motor.stepOffset + motor.skinWidth, out var support))
                flags |= motor.Move(Vector3.down * Mathf.Max(0, support.distance - .1f));
            return flags;
        }

        // Retrobution main.unity3d / sharedassets0.assets: OwnUser #1330,
        // CharacterController #1443. Keep the gameplay capsule independent of
        // visual avatar size so the original narrow routes remain passable.
        public static void Configure(CharacterController motor)
        {
            motor.height = 1.6f;
            motor.radius = .3f;
            motor.center = Vector3.up * .8f;
            motor.skinWidth = .03f;
            motor.slopeLimit = 50;
            motor.stepOffset = .3f;
            motor.minMoveDistance = 0;
        }
    }
}
