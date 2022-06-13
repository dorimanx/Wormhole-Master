using System;
using System.Collections;
using System.Collections.Generic;
using Sandbox.Engine.Multiplayer;
using Sandbox.Game.Entities;
using Sandbox.Game.Multiplayer;
using Torch.Utils;
using VRage.Collections;
using VRage.Network;
using VRage.Replication;
using VRageMath;
using System.Linq;

namespace Wormhole.Managers
{
    public static class EntityRefresh
    {
        [ReflectedGetter(Name = "m_clientStates")]
        public static Func<MyReplicationServer, IDictionary> _clientStates;

        [ReflectedGetter(TypeName = "VRage.Network.MyClient, VRage", Name = "Replicables")]
        public static Func<object, MyConcurrentDictionary<IMyReplicable, MyReplicableClientData>> _replicables;

        [ReflectedMethod(Name = "RemoveForClient", OverrideTypeNames = new string[] { null, "VRage.Network.MyClient, VRage", null })]
        public static Action<MyReplicationServer, IMyReplicable, object, bool> _removeForClient;

        [ReflectedMethod(Name = "ForceReplicable")]
        public static Action<MyReplicationServer, IMyReplicable, Endpoint> _forceReplicable;

        public static void RefreshAroundGrid(MyCubeGrid grid)
        {
            Vector3D center = grid.PositionComp.GetPosition();
            var players = Sync.Players.GetOnlinePlayers();

            if (players != null && players.Count > 0)
            {
                foreach (var player in players)
                {
                    if (player != null && center != null)
                    {
                        if (Vector3D.Distance(player.GetPosition(), center) < 4000)
                            Refresh(player.Id.SteamId);
                    }
                }
            }
        }

        public static void Refresh(ulong steamid)
        {
            if (steamid < 1)
                return;

            try
            {
                var playerEndpoint = new Endpoint(steamid, 0);
                var replicationServer = (MyReplicationServer)MyMultiplayer.ReplicationLayer;
                var clientDataDict = _clientStates.Invoke(replicationServer);
                object clientData = clientDataDict[playerEndpoint];

                var clientReplicables = _replicables.Invoke(clientData);
                var replicableList = new List<IMyReplicable>(clientReplicables.Count);

                replicableList.AddRange(from pair in clientReplicables
                                        select pair.Key);

                foreach (var replicable in replicableList)
                {
                    _removeForClient.Invoke(replicationServer, replicable, clientData, true);
                    _forceReplicable.Invoke(replicationServer, replicable, playerEndpoint);
                }
            }
            catch
            {
                // Avoid Client Crash if some entitie is null.
            }
        }
    }
}