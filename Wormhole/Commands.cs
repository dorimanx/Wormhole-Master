﻿using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Text;
using Sandbox.Common.ObjectBuilders;
using Sandbox.Engine.Multiplayer;
using Sandbox.Game;
using Sandbox.Game.Entities;
using Sandbox.Game.GameSystems;
using Sandbox.Game.Multiplayer;
using Sandbox.Game.Screens.Helpers;
using Sandbox.Game.World;
using Sandbox.ModAPI;
using Torch.API.Managers;
using Torch.Commands;
using Torch.Commands.Permissions;
using Torch.Mod;
using Torch.Mod.Messages;
using Torch.Utils;
using VRage;
using VRage.Game.Entity;
using VRage.Game.ModAPI;
using VRage.Network;
using VRage.ObjectBuilders;
using VRageMath;
using Wormhole.ViewModels;
using Wormhole.Managers;

namespace Wormhole
{
    [Category("wormhole")]
    public class Commands : CommandModule
    {
        public Plugin Plugin => (Plugin)Context.Plugin;

        [Command("show", "shows wormholes")]
        [Permission(MyPromoteLevel.None)]
        public void ShowWormholes()
        {
            foreach (var wormholeGate in Plugin.Config.WormholeGates)
            {
                var gps = wormholeGate.ToGps();
                MySession.Static.Gpss.SendAddGpsRequest(Context.Player.IdentityId, ref gps);
            }
            Context.Respond("GPSs added to your list if it didn't already exist");
        }

        [Command("showeveryone", "shows wormholes to everyone (offline players too)")]
        [Permission(MyPromoteLevel.Admin)]
        public void ShowEveryoneWormholes()
        {
            foreach (var wormholeGate in Plugin.Config.WormholeGates)
            {
                var gps = wormholeGate.ToGps();
                foreach (var (_, identityId) in Sync.Players.GetPrivateField<ConcurrentDictionary<MyPlayer.PlayerId, long>>("m_playerIdentityIds"))
                {
                    MySession.Static.Gpss.AddPlayerGps(identityId, ref gps);
                }
            }
            Context.Respond("GPSs added to everyone's list if it didn't already exist");
        }

        [Command("safezone", "adds safe zones")]
        [Permission(MyPromoteLevel.Admin)]
        public void SafeZone(float radius = -1)
        {
            if (radius < 1) radius = (float)Plugin.Config.GateRadius;
            var entities = MyEntities.GetEntities();
            if (entities != null)
            {
                foreach (var entity in entities.Where(static entity => entity is MySafeZone && entity.DisplayName.Contains("[NPC-IGNORE]_[Wormhole-SafeZone]")))
                    entity.Close();
            }

            foreach (var server in Plugin.Config.WormholeGates)
            {
                var ob = new MyObjectBuilder_SafeZone
                {
                    PositionAndOrientation = new MyPositionAndOrientation(new(server.X, server.Y, server.Z), Vector3.Forward, Vector3.Up),
                    PersistentFlags = MyPersistentEntityFlags2.InScene,
                    Shape = MySafeZoneShape.Sphere,
                    Radius = radius,
                    Enabled = true,
                    DisplayName = "[NPC-IGNORE]_[Wormhole-SafeZone]_" + server.Name,
                    AccessTypeGrids = MySafeZoneAccess.Blacklist,
                    AccessTypeFloatingObjects = MySafeZoneAccess.Blacklist,
                    AccessTypeFactions = MySafeZoneAccess.Blacklist,
                    AccessTypePlayers = MySafeZoneAccess.Blacklist
                };
                MyEntities.CreateFromObjectBuilderAndAdd(ob, true);
            }

            Context.Respond("Deleted all entities with '[NPC-IGNORE]_[Wormhole-SafeZone]' in them and readded Safezones to each Wormhole");
        }

        [Command("addgates", "Adds gates to each wormhole (type: 1 = regular, 2 = rotating, 3 = advanced rotating, 4 = regular 53blocks, 5 = rotating 53blocks, 6 = advanced rotating 53blocks) (selfowned: true = owned by you) (ownerid = id of who you want it owned by)")]
        [Permission(MyPromoteLevel.Admin)]
        public void AddGates(int type = 1, bool selfowned = false, long ownerid = 0L, bool setstatic = false)
        {
            try
            {
                var entities = MyEntities.GetEntities();
                if (entities is { })
                {
                    foreach (var grid in entities.OfType<MyCubeGrid>().Where(static b => b.DisplayName.Contains("[NPC-IGNORE]_[Wormhole-Gate]")))
                    {
                        grid.Close();
                    }
                }

                foreach (var gateViewModel in Plugin.Config.WormholeGates)
                {
                    var prefab = type switch
                    {
                        2 => "WORMHOLE_ROTATING",
                        3 => "WORMHOLE_ROTATING_ADVANCED",
                        4 => "WORMHOLE_53_BLOCKS",
                        5 => "WORMHOLE_ROTATING_53_BLOCKS",
                        6 => "WORMHOLE_ROTATING_ADVANCED_53_BLOCKS",
                        _ => "WORMHOLE"
                    };

                    var grids = MyPrefabManager.Static.GetGridPrefab(prefab).ToList();
                    var objectBuilderList = new List<MyObjectBuilder_EntityBase>();

                    foreach (var cubeBlock in grids.SelectMany(static grid => grid.CubeBlocks))
                    {
                        if (selfowned)
                        {
                            cubeBlock.Owner = Context.Player.IdentityId;
                            cubeBlock.BuiltBy = Context.Player.IdentityId;
                        }
                        else
                        {
                            cubeBlock.Owner = ownerid;
                            cubeBlock.BuiltBy = ownerid;
                        }
                    }

                    var firstGrid = true;
                    double deltaX = 0;
                    double deltaY = 0;
                    double deltaZ = 0;

                    foreach (var grid in grids)
                    {
                        var position = grid.PositionAndOrientation;
                        var realPosition = position.Value;
                        var currentPosition = realPosition.Position;

                        if (firstGrid)
                        {
                            deltaX = gateViewModel.X - currentPosition.X;
                            deltaY = gateViewModel.Y - currentPosition.Y;
                            deltaZ = gateViewModel.Z - currentPosition.Z;

                            currentPosition.X = gateViewModel.X;
                            currentPosition.Y = gateViewModel.Y;
                            currentPosition.Z = gateViewModel.Z;

                            firstGrid = false;

                            if (setstatic)
                            {
                                grid.IsStatic = true;
                                grid.IsUnsupportedStation = true;
                            }
                        }
                        else
                        {
                            currentPosition.X += deltaX;
                            currentPosition.Y += deltaY;
                            currentPosition.Z += deltaZ;
                        }

                        realPosition.Position = currentPosition;
                        grid.PositionAndOrientation = realPosition;
                        grid.Immune = true;
                        grid.Editable = false;

                        objectBuilderList.Add(grid);
                    }

                    // spawn not like a clown. or its fail.
                    MyEntities.RemapObjectBuilderCollection(objectBuilderList);
                    MyEntities.Load(objectBuilderList, out _);
                }

                var entitiesRescan = MyEntities.GetEntities();
                if (entitiesRescan is { })
                {
                    foreach (var grid in entitiesRescan.OfType<MyCubeGrid>().Where(static b => b.DisplayName.Contains("[NPC-IGNORE]_[Wormhole-Gate]")))
                    {
                        _ = MyGravityProviderSystem.CalculateNaturalGravityInPoint(grid.PositionComp.GetPosition(), out var ingravitynow);
                        if (ingravitynow > 0)
                        {
                            if (!grid.IsStatic)
                            {
                                grid.Physics?.ClearSpeed();
                                Context.Respond("Converting to station " + grid.DisplayName);
                                grid.OnConvertedToStationRequest();
                                // if was in safezone without permissions to convert, force convert.
                                if (!grid.IsStatic)
                                    MyMultiplayer.RaiseEvent(grid, (MyCubeGrid x) => new Action(x.ConvertToStatic), MyEventContext.Current.Sender);
                            }
                        }
                    }
                }

                Context.Respond("Deleted all entities with '[NPC-IGNORE]_[Wormhole-Gate]' in them and readded Gates to each Wormhole");
            }
            catch
            {
                Context.Respond("Error: make sure you add the mod for the gates you are using");
            }
        }

        [Command("addonegate", "Adds one gate to gate position by gate name as set in config, (type: 1 = regular, 2 = rotating, 3 = advanced rotating, 4 = regular 53blocks, 5 = rotating 53blocks, 6 = advanced rotating 53blocks) (selfowned: true = owned by you) (ownerid = id of who you want it owned by)")]
        [Permission(MyPromoteLevel.Admin)]
        public void AddOneGate(int type = 1, string Name = "error", bool selfowned = false, long ownerid = 0L, bool setstatic = false)
        {
            try
            {
                if (Name == "error")
                {
                    Context.Respond("Please provide Gate Name as set in configuration. !addonegate PrefabNum GateName");
                    return;
                }

                var entities = MyEntities.GetEntities();
                var GateFound = false;

                foreach (var server in Plugin.Config.WormholeGates)
                {
                    if (server.Name != Name)
                        continue;

                    GateFound = true;

                    var prefab = type switch
                    {
                        2 => "WORMHOLE_ROTATING",
                        3 => "WORMHOLE_ROTATING_ADVANCED",
                        4 => "WORMHOLE_53_BLOCKS",
                        5 => "WORMHOLE_ROTATING_53_BLOCKS",
                        6 => "WORMHOLE_ROTATING_ADVANCED_53_BLOCKS",
                        _ => "WORMHOLE"
                    };

                    var grids = MyPrefabManager.Static.GetGridPrefab(prefab);
                    var objectBuilderList = new List<MyObjectBuilder_EntityBase>();

                    foreach (var grid in grids)
                    {
                        foreach (var cubeBlock in grid.CubeBlocks)
                        {
                            if (selfowned)
                            {
                                cubeBlock.Owner = Context.Player.IdentityId;
                                cubeBlock.BuiltBy = Context.Player.IdentityId;
                            }
                            else
                            {
                                cubeBlock.Owner = ownerid;
                                cubeBlock.BuiltBy = ownerid;
                            }
                        }
                        objectBuilderList.Add(grid);
                    }

                    var firstGrid = true;
                    double deltaX = 0;
                    double deltaY = 0;
                    double deltaZ = 0;

                    foreach (var grid in grids)
                    {
                        var position = grid.PositionAndOrientation;
                        var realPosition = position.Value;
                        var currentPosition = realPosition.Position;

                        if (firstGrid)
                        {
                            deltaX = server.X - currentPosition.X;
                            deltaY = server.Y - currentPosition.Y;
                            deltaZ = server.Z - currentPosition.Z;

                            currentPosition.X = server.X;
                            currentPosition.Y = server.Y;
                            currentPosition.Z = server.Z;

                            firstGrid = false;

                            if (setstatic)
                            {
                                grid.IsStatic = true;
                                grid.IsUnsupportedStation = true;
                            }
                        }
                        else
                        {
                            currentPosition.X += deltaX;
                            currentPosition.Y += deltaY;
                            currentPosition.Z += deltaZ;
                        }

                        realPosition.Position = currentPosition;
                        grid.PositionAndOrientation = realPosition;
                    }

                    if (entities is { })
                    {
                        foreach (var grid in entities.OfType<MyCubeGrid>().Where(static b => b.DisplayName.Contains("[NPC-IGNORE]_[Wormhole-Gate]")))
                        {
                            var Gridposition = grid.PositionComp.GetPosition();
                            var PositionX = Gridposition.X;
                            var PositionY = Gridposition.Y;
                            var PositionZ = Gridposition.Z;

                            if (PositionX == server.X && PositionY == server.Y && PositionZ == server.Z)
                            {
                                grid.Close();
                                Context.Respond("Deleted old gate grid at gate location");
                            }
                        }
                    }

                    MyEntities.RemapObjectBuilderCollection(objectBuilderList);
                    MyEntities.Load(objectBuilderList, out _);
                }

                var entitiesRescan = MyEntities.GetEntities();
                if (entitiesRescan is { } && GateFound)
                {
                    foreach (var grid in entitiesRescan.OfType<MyCubeGrid>().Where(static b => b.DisplayName.Contains("[NPC-IGNORE]_[Wormhole-Gate]")))
                    {
                        _ = MyGravityProviderSystem.CalculateNaturalGravityInPoint(grid.PositionComp.GetPosition(), out var ingravitynow);
                        if (ingravitynow > 0)
                        {
                            if (!grid.IsStatic)
                            {
                                grid.Physics?.ClearSpeed();
                                Context.Respond("Converting to station " + grid.DisplayName);
                                grid.OnConvertedToStationRequest();
                                // if was in safezone without permissions to convert, force convert.
                                if (!grid.IsStatic)
                                    MyMultiplayer.RaiseEvent(grid, (MyCubeGrid x) => new Action(x.ConvertToStatic), MyEventContext.Current.Sender);
                            }
                        }
                    }
                }

                if (GateFound)
                    Context.Respond("Added Gate to Wormhole GPS Position");
                else
                    Context.Respond("Didnt found gate configuration with that name! Abort");
            }
            catch
            {
                Context.Respond("Error: make sure you add the mod for the gates you are using");
            }
        }

        [Command("showeffect", "Show gate Effect on gate by gate name, need to have grid with jumpdrive near gate in 500m")]
        [Permission(MyPromoteLevel.Admin)]
        public void ShowEffectonGate(string Name = "error")
        {
            try
            {
                if (Name == "error")
                {
                    Context.Respond("Please provide Gate Name as set in configuration.");
                    return;
                }

                GateViewModel GateModel = new GateViewModel();

                foreach (var server in Plugin.Config.WormholeGates)
                {
                    if (server.Name != Name)
                        continue;

                    GateModel = server;
                    break;
                }

                IMyPlayer player = Context.Player;
                if (player == null || GateModel == null || GateModel.Position == null)
                {
                    Context.Respond("Didnt found gate configuration with that name! Abort");
                    return;
                }

                var gate = new BoundingSphereD(GateModel.Position, 500);
                List<MyEntity> _tmpEntities = new();
                MyCubeGrid FoundGrid = null;

                MyGamePruningStructure.GetAllTopMostEntitiesInSphere(ref gate, _tmpEntities, MyEntityQueryType.Dynamic);

                foreach (var grid in _tmpEntities.OfType<MyCubeGrid>())
                {
                    if (grid != null)
                    {
                        var JumpDriveCheck = grid.GridSystems?.TerminalSystem?.Blocks.OfType<MyJumpDrive>().ToList().Count();

                        if (JumpDriveCheck != null && JumpDriveCheck > 0)
                        {
                            FoundGrid = grid;
                            break;
                        }
                    }
                }

                if (FoundGrid != null)
                {
                    Plugin.TestEffectStart(GateModel, (MyPlayer)player, FoundGrid);
                    Context.Respond("Showing Effect now");
                    return;
                }

                Context.Respond("Didnt found grid nearby with jumpdrive on it! Abort");
            }
            catch
            {
                Context.Respond("Error: make sure you add the mod for the gates you are using");
            }
        }

        [Command("stopeffect", "Stop gate Effect on gate by gate name, need to have grid with jumpdrive near gate in 500m")]
        [Permission(MyPromoteLevel.Admin)]
        public void StopEffectonGate(string Name = "error")
        {
            try
            {
                if (Name == "error")
                {
                    Context.Respond("Please provide Gate Name as set in configuration.");
                    return;
                }

                GateViewModel GateModel = new GateViewModel();

                foreach (var server in Plugin.Config.WormholeGates)
                {
                    if (server.Name != Name)
                        continue;

                    GateModel = server;
                    break;
                }

                if (GateModel == null || GateModel.Position == null)
                {
                    Context.Respond("Didnt found gate configuration with that name! Abort");
                    return;
                }

                var gate = new BoundingSphereD(GateModel.Position, 500);
                List<MyEntity> _tmpEntities = new();
                MyCubeGrid FoundGrid = null;

                MyGamePruningStructure.GetAllTopMostEntitiesInSphere(ref gate, _tmpEntities, MyEntityQueryType.Dynamic);

                foreach (var grid in _tmpEntities.OfType<MyCubeGrid>())
                {
                    if (grid != null)
                    {
                        var JumpDriveCheck = grid.GridSystems?.TerminalSystem?.Blocks.OfType<MyJumpDrive>().ToList().Count();

                        if (JumpDriveCheck != null && JumpDriveCheck > 0)
                        {
                            FoundGrid = grid;
                            break;
                        }
                    }
                }

                if (FoundGrid != null)
                {
                    Plugin.TestEffectStop(GateModel, FoundGrid);
                    Context.Respond("Stopping Effect now");
                    return;
                }

                Context.Respond("Didnt found grid nearby with jumpdrive on it! Abort");
            }
            catch
            {
                Context.Respond("Error: make sure you add the mod for the gates you are using");
            }
        }

        [Command("clearsync", "try to remove all files in sync folder")]
        [Permission(MyPromoteLevel.Admin)]
        public void ClearSync()
        {
            var gridDir = new DirectoryInfo(Plugin.Config.Folder + "/" + Plugin.AdminGatesFolder);

            if (!gridDir.Exists) return;

            foreach (var file in gridDir.GetFiles())
            {
                if (file == null) continue;

                try
                {
                    file.Delete();
                }
                catch
                {
                }
            }
        }

        [Command("clear characters", "try to remove all your character (if it duplicated or bugged)")]
        [Permission(MyPromoteLevel.None)]
        public void RemoveCharacters()
        {
            if (Context.Player == null)
                return;

            var identity = (MyIdentity)Context.Player.Identity;
            Utilities.KillCharacters(identity.SavedCharacters);

            Sync.Players.KillPlayer((MyPlayer)Context.Player);
        }

        [Command("show discovery", "show the current discovered gates")]
        public void ShowDiscovery()
        {
            var sb = new StringBuilder();

            if (Context.Player is null)
                sb.AppendLine("Discovered Gates:");

            foreach (var (ip, discovery) in Context.Torch.Managers.GetManager<WormholeDiscoveryManager>().Discoveries)
            {
                sb.AppendLine($"= {ip} - {discovery.Gates.Count} gates");
                foreach (var gate in discovery.Gates)
                {
                    sb.AppendLine($"{" ",-3}+ {gate.Name}:");
                    foreach (var destination in gate.Destinations)
                    {
                        sb.AppendLine($"{" ",-6}-> {destination.DisplayName}");
                    }
                }
            }

            if (Context.Player is null)
                Context.Respond(sb.ToString());
            else
                ModCommunication.SendMessageTo(new DialogMessage("Wormhole", "Discovered Gates", sb.ToString()), Context.Player.SteamUserId);
        }
    }
}