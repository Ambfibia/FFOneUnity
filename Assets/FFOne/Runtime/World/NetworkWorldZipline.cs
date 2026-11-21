using System;
using FFOne.UI;
using UnityEngine;

namespace FFOne.World
{
    internal readonly struct ZiplineMovement
    {
        internal readonly Vector3 Start, Position, Direction;
        internal readonly float Moved, Length, Speed, Heading;
        internal readonly bool Started, Detached;

        internal ZiplineMovement(Vector3 start, Vector3 position, Vector3 direction, float moved,
            float length, float speed, float heading, bool started, bool detached)
        {
            Start = start; Position = position; Direction = direction;
            Moved = moved; Length = length; Speed = speed; Heading = heading;
            Started = started; Detached = detached;
        }
    }

    public sealed partial class NetworkWorldController
    {
        const float ZiplineSendInterval = .1f;
        const float DefaultZiplineHang = 2.2f;
        WorldZipline zipline;
        float ziplineDistance, ziplineLength, ziplinePacketTime;
        Vector3 ziplineDirection;
        int ziplineAttachFrame;
        internal bool Ziplining => zipline != null;
        internal bool HasZiplineTarget => Player && IsReady && MovementEnabled && !InputBlocked &&
            !playerDead && !BuffMovementBlocked && !Riding && !Ziplining && !PersonalVehicleMounted &&
            WorldZipline.Find(View, Player.position) != null;
        internal event Action<ZiplineMovement> ZiplineMoved;

        internal bool TryStartZipline()
        {
            if (!Player || !IsReady || !MovementEnabled || InputBlocked || playerDead || BuffMovementBlocked ||
                Riding || PersonalVehicleMounted || Ziplining) return false;
            var route = WorldZipline.Find(View, Player.position);
            if (route == null) return false;
            LeaveSlider();
            autoRun = false; freeCamera = false;
            zipline = route; ziplineAttachFrame = Time.frameCount;
            ziplineDistance = ziplinePacketTime = 0;
            ziplineLength = Vector3.Distance(route.Start, route.End);
            ziplineDirection = (route.End - route.Start) / ziplineLength;
            var forward = new Vector3(ziplineDirection.x, 0, ziplineDirection.z);
            if (forward.sqrMagnitude > .0001f) yaw = Mathf.Atan2(forward.x, forward.z) * Mathf.Rad2Deg;
            Player.rotation = Quaternion.Euler(0, yaw, 0);
            NativePlayerMotor.Teleport(motor, route.Start - Vector3.up * DefaultZiplineHang);
            vertical = 0; Jumping = false; MoveDirection = 0;
            Velocity = ziplineDirection * route.Speed;
            avatar?.SetZiplinePresentation(true);
            LocalNano?.SetNano(0);
            PublishZipline(started: true);
            return true;
        }

        bool TickZipline()
        {
            if (!Ziplining) return false;
            if (!Player || !Stream || !MovementEnabled || zipline.Owner == null)
            {
                StopZipline();
                return false;
            }
            float delta = Mathf.Min(Time.deltaTime, .1f);
            if (IsReady && Time.frameCount != ziplineAttachFrame)
            {
                ziplineDistance = Mathf.Min(ziplineLength, ziplineDistance + zipline.Speed * delta);
                float hang = avatar ? avatar.ZiplineHangHeight(DefaultZiplineHang) : DefaultZiplineHang;
                Vector3 target = zipline.Start + ziplineDirection * ziplineDistance - Vector3.up * hang;
                motor.Move(target - Player.position);
                ziplinePacketTime += delta;
                bool detached = !InputBlocked && Application.isFocused && GameplayInput.Pressed(GameplayAction.Jump);
                if (detached || ziplineDistance >= ziplineLength)
                {
                    PublishZipline(detached: detached);
                    StopZipline();
                    Jumping = true;
                    jumpAge = 0; vertical = JumpLaunchVelocity = 0;
                    JumpKey = JumpKey == uint.MaxValue ? 1073741823 : JumpKey + 1;
                }
                else if (ziplinePacketTime >= ZiplineSendInterval) PublishZipline();
            }
            Stream.Anchor = Player.position;
            UpdateCamera();
            return true;
        }

        void PublishZipline(bool started = false, bool detached = false)
        {
            ziplinePacketTime = 0;
            ZiplineMoved?.Invoke(new ZiplineMovement(zipline.Start, Player.position, ziplineDirection, ziplineDistance,
                ziplineLength, zipline.Speed, yaw, started, detached));
        }

        void StopZipline(bool detached = false)
        {
            if (!Ziplining) return;
            if (detached && Player) PublishZipline(detached: true);
            zipline = null;
            Velocity = Vector3.zero;
            if (avatar) avatar.SetZiplinePresentation(false);
        }
    }
}
