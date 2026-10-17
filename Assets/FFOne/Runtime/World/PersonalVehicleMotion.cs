using UnityEngine;

namespace FFOne.World
{
    /// <summary>Primary cnAvatarThirdPersonMove.UpdateVehicleAcceleration/Coasting horizontal state.</summary>
    public sealed class PersonalVehicleMotion
    {
        public Vector3 Momentum { get; private set; }
        float directionalSpeedCap;
        public void Reset() { Momentum = Vector3.zero; directionalSpeedCap = 0; }
        public Vector3 Step(float side, float forward, float yaw, float speed, bool grounded, float delta)
        {
            if (speed <= 0) { Reset(); return Momentum; }
            if (Mathf.Abs(side) < .0001f && Mathf.Abs(forward) < .0001f)
            {
                // The original decay is per Update, including its separate airborne coefficient.
                Momentum = Momentum.magnitude > .1f ? Momentum * (grounded ? .98f : .997f) : Vector3.zero;
                return Momentum;
            }
            float acceleration, multiplier;
            if (forward < 0) { acceleration = grounded ? .75f : .375f; multiplier = .54f; }
            else if (Mathf.Abs(side) > .9f && forward > .9f) { acceleration = grounded ? .8f : .45f; multiplier = 1.215f; }
            else if (Mathf.Abs(side) > .9f && forward < .1f)
            {
                acceleration = Momentum.magnitude > 10 ? grounded ? 2.5f : 1.25f : grounded ? .9f : .45f;
                multiplier = .9f;
            }
            else { acceleration = grounded ? .85f : .5f; multiplier = 1.35f; }
            directionalSpeedCap = speed * multiplier;
            Vector3 direction = Quaternion.Euler(0, yaw, 0) * new Vector3(side, 0, forward).normalized;
            var target = direction * directionalSpeedCap;
            Momentum = Vector3.Lerp(Momentum, target, acceleration * delta);
            Momentum = Vector3.Lerp(Momentum, target, Vector3.Dot(direction, Momentum.normalized) >= .8f ? 3 * delta : .0001f);
            Momentum = Vector3.ClampMagnitude(Momentum, directionalSpeedCap);
            return Momentum;
        }
        public Vector3 Bounce()
        {
            // UpdateVehicleAcceleration applies its directional cap after the obstacle
            // response too (IL_02ed–0384), so reversing or strafing cannot bounce at 7.5.
            Momentum = -Momentum.normalized * Mathf.Min(7.5f, directionalSpeedCap);
            return Momentum;
        }
    }
}
