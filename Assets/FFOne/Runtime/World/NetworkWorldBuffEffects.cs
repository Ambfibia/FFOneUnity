using FFOne.Network;
using UnityEngine;
namespace FFOne.World
{
    public sealed partial class NetworkWorldController
    {
        internal System.Func<bool, System.Threading.Tasks.Task> ReportPoisonContact;
        System.Threading.Tasks.Task pendingPoison;
        bool? sentPoison;
        bool pendingPoisonValue;
        float nextPoisonSample;
        Vector2Int attributeTile = new(-1, -1);
        byte[] terrainAttributes;
        readonly RaycastHit[] waterContacts = new RaycastHit[24];

        void UpdatePoisonContact()
        {
            if (!Player || !Stream || !IsReady || ReportPoisonContact == null) return;
            if (pendingPoison != null)
            {
                if (!pendingPoison.IsCompleted) return;
                if (pendingPoison.IsCompletedSuccessfully) sentPoison = pendingPoisonValue;
                else if (pendingPoison.IsFaulted) Debug.LogWarning(pendingPoison.Exception.GetBaseException().Message);
                pendingPoison = null;
            }
            if (Time.unscaledTime < nextPoisonSample) return;
            nextPoisonSample = Time.unscaledTime + .1f;
            bool poisoned = !PersonalVehicleMounted && !Riding && PoisonAt(Player.position);
            if (sentPoison == poisoned) return;
            pendingPoisonValue = poisoned;
            pendingPoison = ReportPoisonContact(poisoned);
            nextPoisonSample = Time.unscaledTime + 1;
        }

        bool PoisonAt(Vector3 point)
        {
            var tile = new Vector2Int(Mathf.FloorToInt(point.x / 512), Mathf.FloorToInt(point.z / 512));
            if (attributeTile != tile)
            {
                attributeTile = tile;
                terrainAttributes = null;
                if (tile.x >= 0 && tile.x < 16 && tile.y >= 0 && tile.y < 16)
                {
                    var data = FFOne.Content.ContentAssets.Load<TextAsset>(
                        $"NativeBinary/map/tiles/map_{tile.x:00}_{tile.y:00}/terrain/gameplay/attributes.bin");
                    if (data) terrainAttributes = data.bytes;
                }
            }
            if (terrainAttributes == null || terrainAttributes.Length != 129 * 129) return false;
            int x = Mathf.FloorToInt((point.x - tile.x * 512) / 4);
            int z = Mathf.FloorToInt((point.z - tile.y * 512) / 4);
            byte flags = terrainAttributes[z * 129 + x];
            if (flags >= 64 || (flags & 8) == 0) return false;
            int count = Physics.RaycastNonAlloc(point + Vector3.up * 2.5f, Vector3.down, waterContacts, 3,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
            {
                var hit = waterContacts[i];
                var c = hit.collider;
                if (!Stream.Owns(c)) continue;
                if (c is MeshCollider ground && ground.sharedMesh && (ground.sharedMesh.name == "Native collision WorldSurface" || ground.sharedMesh.name == "WorldSurface") &&
                    Mathf.Abs(hit.point.y - point.y) < .5f) return true;
                if (c.name.StartsWith("ffWater ", System.StringComparison.Ordinal) ||
                    c.name.StartsWith("ffPoison ", System.StringComparison.Ordinal)) return true;
            }
            return false;
        }

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
        internal float BuffGroundSpeed(float baseSpeed) => CurrentBuffSpeed(baseSpeed);
        float BuffVehicleSpeed => CurrentBuffSpeed(6, PersonalVehicleSpeed);
        float CurrentBuffSpeed(float baseSpeed, float vehicleSpeed = 0)
        {
            if (BuffMovementBlocked) return 0;
            int slow = Buffs.Value(8);
            float speed = ResolveBuffSpeed(baseSpeed, Buffs.Value(1), slow, vehicleSpeed);
            return slow == 0 && Buffs.Has(8) ? speed * .5f : speed;
        }
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
            var before = Player.position; LaunchJump();
            avatar.PlayLocomotion("jumpstart", true);
            motor.Move(Vector3.up * vertical * delta);
            Velocity = (Player.position - before) / delta; Stream.Anchor = Player.position;
            Moved?.Invoke(Player.position, yaw); UpdateCamera(); return Player.position - before;
        }
#endif
    }
}
