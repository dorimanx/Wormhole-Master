﻿using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using NLog;
using Sandbox;
using Sandbox.Common.ObjectBuilders;
using Sandbox.Definitions;
using Sandbox.Game.Entities;
using Sandbox.Game.Entities.Character;
using Sandbox.Game.Multiplayer;
using Sandbox.ModAPI;
using Torch.Mod;
using Torch.Mod.Messages;
using Torch.Utils;
using VRage.Game;
using VRage.Game.Entity;
using VRage.Game.ModAPI;
using VRage.Game.ObjectBuilders.Components;
using VRage.ObjectBuilders;
using VRage.ObjectBuilders.Private;
using VRage.Utils;
using VRageMath;

namespace Wormhole
{
    public static class Utilities
    {
        public static readonly Logger Log = LogManager.GetCurrentClassLogger();
        private static readonly Random RandomPos = new Random();

        public static IEnumerable<ulong> GetAllCharactersClientIds(IEnumerable<MyCubeGrid> grids)
        {
            foreach (var block in grids.SelectMany(b => b.GetFatBlocks<MyCockpit>()).Where(b => b.Pilot is not null))
            {
                if (!block.Pilot.GetPlayerId(out var playerId))
                    continue;

                yield return playerId.SteamId;
            }
        }

        public static bool UpdateGridsPositionAndStop(ICollection<MyObjectBuilder_CubeGrid> grids, Vector3D newPosition)
        {
            var biggestGrid = grids.OrderByDescending(static b => b.CubeBlocks.Count).First();
            var delta = biggestGrid.PositionAndOrientation!.Value.Position;

            // make sure admin didnt failed here.
            if (Plugin.Instance.Config.MinDistance > Plugin.Instance.Config.MaxDistance)
            {
                Plugin.Instance.Config.MinDistance = 1;
                Plugin.Instance.Config.MaxDistance = 5;
            }

            newPosition = RandomPositionFromGatePoint(newPosition, RandomPos.Next(Plugin.Instance.Config.MinDistance, Plugin.Instance.Config.MaxDistance));
            newPosition -= FindGridsBoundingSphere(grids, biggestGrid).Center - biggestGrid.PositionAndOrientation!.Value.Position;

            return grids.All(grid =>
            {
                if (grid.PositionAndOrientation == null)
                {
                    Log.Warn($"Position and Orientation Information missing from Grid {grid.DisplayName} in file.");
                    return false;
                }

                var gridPositionOrientation = grid.PositionAndOrientation.Value;
                if (grid == biggestGrid)
                    gridPositionOrientation.Position = newPosition;
                else
                    gridPositionOrientation.Position = newPosition + gridPositionOrientation.Position - delta;

                grid.PositionAndOrientation = gridPositionOrientation;

                // reset velocity
                grid.AngularVelocity = new();
                grid.LinearVelocity = new();
                return true;
            });
        }

        public static void UpdateGridPositionAndStopLive(MyCubeGrid grid, Vector3D newPosition)
        {
            var matrix = grid.PositionComp.WorldMatrixRef;
            matrix.Translation = newPosition;
            grid.Teleport(matrix);
            grid.Physics.LinearVelocity = Vector3.Zero;
            grid.Physics.AngularVelocity = Vector3.Zero;
        }

        public static string CreateBlueprintPath(string folder, string fileName)
        {
            Directory.CreateDirectory(folder);
            return Path.Combine(folder, fileName + ".sbcB5");
        }

        public static bool HasRightToMove(IMyPlayer player, MyCubeGrid grid)
        {
            var result = player.GetRelationTo(GetOwner(grid)) == MyRelationsBetweenPlayerAndBlock.Owner;
            if (Plugin.Instance.Config.AllowInFaction && !result)
                result = player.GetRelationTo(GetOwner(grid)) == MyRelationsBetweenPlayerAndBlock.FactionShare;
            return result;
        }

        public static long GetOwner(MyCubeGrid grid)
        {
            var gridOwnerList = grid.BigOwners;
            var ownerCnt = gridOwnerList.Count;
            var gridOwner = 0L;

            if (ownerCnt > 0 && gridOwnerList[0] != 0)
                return gridOwnerList[0];

            if (ownerCnt > 1)
                return gridOwnerList[1];

            return gridOwner;
        }

        public static List<MyCubeGrid> FindGridList(MyCubeGrid grid, bool includeConnectedGrids)
        {
            var list = new List<MyCubeGrid>();

            list.AddRange(includeConnectedGrids
                ? MyCubeGridGroups.Static.Physical.GetGroup(grid).Nodes.Select(static b => b.NodeData)
                : MyCubeGridGroups.Static.Mechanical.GetGroup(grid).Nodes.Select(static b => b.NodeData));

            return list;
        }

        public static Vector3D? FindFreePos(BoundingSphereD gate, float sphereradius)
        {
            var rand = new Random();
            MyEntity safezone = null;
            var entities = MyEntities.GetEntitiesInSphere(ref gate);

            foreach (var myentity in entities)
            {
                if (myentity is MySafeZone)
                    safezone = myentity;
            }

            return MyEntities.FindFreePlaceCustom(
                gate.RandomToUniformPointInSphere(rand.NextDouble(), rand.NextDouble(), rand.NextDouble()),
                sphereradius, 20, 5, 1, 0, safezone);
        }

        public static string LegalCharOnly(string text)
        {
            return string.Join(string.Empty, text.Where(static b => char.IsLetter(b) || char.IsNumber(b)));
        }

        public static float FindGridsRadius(IEnumerable<MyObjectBuilder_CubeGrid> grids)
        {
            Vector3? vector = null;
            var gridradius = 0F;

            foreach (var mygrid in grids)
            {
                var gridSphere = mygrid.CalculateBoundingSphere();
                if (vector == null)
                {
                    vector = gridSphere.Center;
                    gridradius = gridSphere.Radius;
                    continue;
                }

                var distance = Vector3.Distance(vector.Value, gridSphere.Center);
                var newRadius = distance + gridSphere.Radius;
                if (newRadius > gridradius)
                    gridradius = newRadius;
            }
            return (float)new BoundingSphereD(vector.Value, gridradius).Radius;
        }

        public static BoundingSphereD FindGridsBoundingSphere(IEnumerable<MyObjectBuilder_CubeGrid> grids,
            MyObjectBuilder_CubeGrid biggestGrid)
        {
            var boxD = BoundingBoxD.CreateInvalid();
            boxD.Include(biggestGrid.CalculateBoundingBox());
            var matrix = biggestGrid.PositionAndOrientation!.Value.GetMatrix();
            var matrix2 = MatrixD.Invert(matrix);
            var array = new Vector3D[8];

            foreach (var grid in grids)
            {
                if (grid == biggestGrid) continue;

                BoundingBoxD box = grid.CalculateBoundingBox();
                var myOrientedBoundingBoxD =
                    new MyOrientedBoundingBoxD(box, grid.PositionAndOrientation!.Value.GetMatrix());
                myOrientedBoundingBoxD.Transform(matrix2);
                myOrientedBoundingBoxD.GetCorners(array, 0);

                foreach (var point in array)
                {
                    boxD.Include(point);
                }
            }

            var boundingSphereD = BoundingSphereD.CreateFromBoundingBox(boxD);
            return new(new MyOrientedBoundingBoxD(boxD, matrix).Center, boundingSphereD.Radius);
        }

        public static void KillCharacter(MyCharacter character)
        {
            Log.Info("killing character " + character.DisplayName);
            if (character.IsUsing is MyCockpit cockpit)
                cockpit.RemovePilot();

            character.GetIdentity()?.ChangeCharacter(null);
            character.EnableBag(false);
            character.Kill(true, new MyDamageInformation(true, 9999, MyStringHash.GetOrCompute("Deformation"), 0));
            character.Close();
        }

        public static void KillCharacters(ICollection<long> characters)
        {
            foreach (var character in characters)
            {
                if (!MyEntities.TryGetEntityById<MyCharacter>(character, out var entity))
                    continue;
                KillCharacter(entity);
            }
            characters.Clear();
        }

        public static void RemovePilot(MyObjectBuilder_Cockpit cockpit)
        {
            // wasted 15 hours to find this fucking HierarchyComponent trap
            cockpit.Pilot = null;
            var component = cockpit.ComponentContainer?.Components?.FirstOrDefault(static b =>
                b.Component is MyObjectBuilder_HierarchyComponentBase);

            ((MyObjectBuilder_HierarchyComponentBase)component?.Component)?.Children.Clear();
        }

        public static bool TryParseGps(string raw, out string name, out Vector3D position, out Color color)
        {
            name = default;
            position = default;
            color = default;

            var parts = raw.Split(':');
            if (parts.Length != 7)
                return false;

            name = parts[1];
            if (!double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var xCord) ||
                !double.TryParse(parts[3], NumberStyles.Float, CultureInfo.InvariantCulture, out var yCord) ||
                !double.TryParse(parts[4], NumberStyles.Float, CultureInfo.InvariantCulture, out var zCord))
                return false;

            position = new(xCord, yCord, zCord);
            color = ColorUtils.TranslateColor(parts[5]);
            return true;
        }

        public static void SendConnectToServer(string address, ulong clientId)
        {
            ModCommunication.SendMessageTo(
                new JoinServerMessage(ToIpEndpoint(address, MySandboxGame.ConfigDedicated.ServerPort).ToString()),
                clientId);
        }

        public static IPEndPoint ToIpEndpoint(string hostNameOrAddress, int defaultPort)
        {
            var parts = hostNameOrAddress.Split(':');

            if (parts.Length == 2)
                defaultPort = int.Parse(parts[1]);

            var addrs = Dns.GetHostAddresses(parts[0]);
            return new(addrs.FirstOrDefault(addr => addr.AddressFamily == AddressFamily.InterNetwork)
                        ??
                        addrs.First(), defaultPort);
        }

        public static Vector3D RandomPositionFromGatePoint(Vector3D GatePoint, double distance)
        {
            Random NewRandom = new Random();
            var Zrand = (NewRandom.NextDouble() * 2) - 1;
            var PI = NewRandom.NextDouble() * 2 * Math.PI;
            var ZrandSqrt = Math.Sqrt(1 - (Zrand * Zrand));
            var direction = new Vector3D(ZrandSqrt * Math.Cos(PI), ZrandSqrt * Math.Sin(PI), Zrand);

            direction.Normalize();
            GatePoint += direction * -2;
            return GatePoint + (direction * distance);
        }

        private static readonly List<IMyPlayer> _playerCache = new List<IMyPlayer>();

        public static IMyPlayer GetPlayerBySteamId(ulong steamId)
        {
            _playerCache.Clear();
            MyAPIGateway.Players.GetPlayers(_playerCache);
            return _playerCache.FirstOrDefault(p => p.SteamUserId == steamId);
        }

        public static void SaveShipsToFile(ulong steamId, MyObjectBuilder_CubeGrid[] grids_objectbuilders)
        {
            string FirstShipName = grids_objectbuilders.First().DisplayName;
            string filenameexported = GetPlayerBySteamId(steamId).DisplayName.ToString() + "_" + DateTime.Now.ToShortDateString() + "_" + DateTime.Now.Millisecond + "_" + FirstShipName;
            var a1 = Path.GetInvalidPathChars();
            var b1 = Path.GetInvalidFileNameChars();

            var arraychar = a1.Concat(b1);
            foreach (char c in arraychar)
            {
                filenameexported = filenameexported.Replace(c.ToString(), ".");
            }

            SaveToFile(steamId, grids_objectbuilders, filenameexported);
        }

        private static string[] GetNecessaryDLCs(MyObjectBuilder_CubeGrid[] cubeGrids)
        {
            if (cubeGrids.IsNullOrEmpty())
                return null;

            HashSet<string> hashSet = new HashSet<string>();
            for (int i = 0; i < cubeGrids.Length; i++)
            {
                foreach (MyObjectBuilder_CubeBlock builder in cubeGrids[i].CubeBlocks)
                {
                    MyCubeBlockDefinition cubeBlockDefinition = MyDefinitionManager.Static.GetCubeBlockDefinition(builder);
                    if (cubeBlockDefinition != null && cubeBlockDefinition.DLCs != null && cubeBlockDefinition.DLCs.Length != 0)
                    {
                        for (int j = 0; j < cubeBlockDefinition.DLCs.Length; j++)
                        {
                            hashSet.Add(cubeBlockDefinition.DLCs[j]);
                        }
                    }
                }
            }
            return hashSet.ToArray();
        }

        private static bool SaveToFile(ulong steamid, MyObjectBuilder_CubeGrid[] gridstotpob, string filenameexported)
        {
            string FirstShipName = gridstotpob.First().DisplayName;
            MyObjectBuilder_ShipBlueprintDefinition myObjectBuilder_ShipBlueprintDefinition = MyObjectBuilderSerializerKeen.CreateNewObject<MyObjectBuilder_ShipBlueprintDefinition>();
            myObjectBuilder_ShipBlueprintDefinition.Id = new MyDefinitionId(new MyObjectBuilderType(typeof(MyObjectBuilder_ShipBlueprintDefinition)), MyUtils.StripInvalidChars(filenameexported));
            myObjectBuilder_ShipBlueprintDefinition.DLCs = GetNecessaryDLCs(myObjectBuilder_ShipBlueprintDefinition.CubeGrids);
            myObjectBuilder_ShipBlueprintDefinition.CubeGrids = gridstotpob;
            myObjectBuilder_ShipBlueprintDefinition.RespawnShip = false;
            myObjectBuilder_ShipBlueprintDefinition.DisplayName = FirstShipName;
            myObjectBuilder_ShipBlueprintDefinition.OwnerSteamId = Sync.MyId;

            //myObjectBuilder_ShipBlueprintDefinition.CubeGrids[0].DisplayName = blueprintDisplayName;
            MyObjectBuilder_Definitions myObjectBuilder_Definitions = MyObjectBuilderSerializerKeen.CreateNewObject<MyObjectBuilder_Definitions>();
            myObjectBuilder_Definitions.ShipBlueprints = new MyObjectBuilder_ShipBlueprintDefinition[1];
            myObjectBuilder_Definitions.ShipBlueprints[0] = myObjectBuilder_ShipBlueprintDefinition;

            string mypathdir = Plugin.Instance.storagepath + "\\OutgoingWormHoleShipsBackup\\" + steamid;
            if (!Directory.Exists(mypathdir))
                Directory.CreateDirectory(mypathdir);

            string finalpath = Path.Combine(mypathdir, filenameexported + ".sbc");
            var flag = MyObjectBuilderSerializerKeen.SerializeXML(finalpath, false, myObjectBuilder_Definitions, null);
            if (flag)
                MyObjectBuilderSerializerKeen.SerializePB(finalpath + MyObjectBuilderSerializerKeen.ProtobufferExtension, true, myObjectBuilder_Definitions);

            return flag;
        }
    }
}

namespace System.Runtime.CompilerServices
{
    internal class IsExternalInit { }
}