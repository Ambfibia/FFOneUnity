using System;
using System.Threading;
using System.Threading.Tasks;

namespace FFOne.Network
{
    public sealed partial class FusionClient
    {
        public Task StartTaskAsync(int taskId, int npcId, int escortId = 0, CancellationToken cancellationToken = default)
        {
            if (taskId <= 0 || npcId < 0 || escortId < 0) throw new ArgumentOutOfRangeException(nameof(taskId));
            var p = new byte[12]; ProtocolGameplay.I(p, 0, taskId); ProtocolGameplay.I(p, 4, npcId); ProtocolGameplay.I(p, 8, escortId);
            return SendGameplayAsync(0x1300000b, p, cancellationToken);
        }
        public Task CompleteTaskAsync(int taskId, int npcId, int firstChoice = 0, int secondChoice = 0, int escortId = 0, CancellationToken cancellationToken = default)
        {
            if (taskId <= 0 || npcId < 0 || escortId < 0 || firstChoice < 0 || firstChoice > 127 || secondChoice < 0 || secondChoice > 127)
                throw new ArgumentOutOfRangeException(nameof(taskId));
            var p = new byte[16]; ProtocolGameplay.I(p, 0, taskId); ProtocolGameplay.I(p, 4, npcId);
            p[8] = (byte)firstChoice; p[9] = (byte)secondChoice; ProtocolGameplay.I(p, 12, escortId);
            return SendGameplayAsync(0x1300000c, p, cancellationToken);
        }
        public Task StopTaskAsync(int taskId, CancellationToken cancellationToken = default)
            => MissionNumber(0x13000012, taskId, cancellationToken);
        public Task TrackMissionAsync(int missionId, CancellationToken cancellationToken = default)
            => MissionNumber(0x13000083, missionId, cancellationToken);
        Task MissionNumber(uint type, int id, CancellationToken cancellationToken)
        {
            if (id < 0) throw new ArgumentOutOfRangeException(nameof(id));
            var p = new byte[4]; ProtocolGameplay.I(p, 0, id); return SendGameplayAsync(type, p, cancellationToken);
        }
    }
}
