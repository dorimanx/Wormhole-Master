using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows.Controls;
using System.Xml.Serialization;
using NLog;
using ProtoBuf;
using Sandbox.Common.ObjectBuilders;
using Sandbox.Engine.Multiplayer;
using Sandbox.Game;
using Sandbox.Game.Entities;
using Sandbox.Game.Entities.Blocks;
using Sandbox.Game.Entities.Character;
using Sandbox.Game.Multiplayer;
using Sandbox.Game.World;
using Sandbox.ModAPI;
using Torch;
using Torch.API;
using Torch.API.Plugins;
using Torch.Mod;
using Torch.Mod.Messages;
using Torch.Utils;
using VRage;
using VRage.Game;
using VRage.Game.Entity;
using VRage.Game.ModAPI;
using VRage.Network;
using VRageMath;

namespace Wormhole
{
    public class WormholePlugin : TorchPluginBase, IWpfPlugin, ITorchPlugin
    {
        public static readonly Logger Log = LogManager.GetCurrentClassLogger();

        private readonly ConcurrentDictionary<ulong, (Utilities.TransferFileInfo, TransferFile, Vector3D, BoundingSphereD)>
            _queuedSpawns = new ConcurrentDictionary<ulong, (Utilities.TransferFileInfo, TransferFile, Vector3D, BoundingSphereD)>();

        private Persistent<Config> _config;
        private Gui _control;
        public const string AdminGatesConfig = "admingatesconfig";
        public const string AdminGatesFolder = "admingates";
        private Task _saveOnEnterTask;
        private Task _saveOnExitTask;
        private int _tick;
        private string _gridDir;
        private string _gridDirSent;
        private string _gridDirReceived;
        private string _gridDirBackup;

        [ReflectedMethodInfo(typeof(MyVisualScriptLogicProvider), "CloseRespawnScreen")]
        private static readonly MethodInfo _closeRespawnScreenMethod = null;

        private static readonly Lazy<Action> _closeRespawnScreenAction = new Lazy<Action>(() => _closeRespawnScreenMethod.CreateDelegate<Action>());

        public static WormholePlugin Instance { get; private set; }

        public Config Config => _config?.Data;

        public UserControl GetControl() => _control ??= new Gui(this);

        public override void Init(ITorchBase torch)
        {
            base.Init(torch);
            Instance = this;
            SetupConfig();
            Torch.GameStateChanged += (_, state) =>
            {
                if (state == TorchGameState.Loaded)
                    MyMultiplayer.Static.ClientJoined += OnClientConnected;
            };
        }

        internal void OnClientConnected(ulong clientId, string name)
        {
            if (!_queuedSpawns.ContainsKey(clientId))
                return;

            _queuedSpawns.TryRemove(clientId, out var tuple);
            var (transferFileInfo, transferFile, gamePoint, gate) = tuple;
            try
            {
                WormholeTransferInQueue(transferFile, transferFileInfo, gamePoint, gate);
            }
            catch (Exception e)
            {
                Log.Error(e, $"Could not run queued spawn for {name} ({clientId})");
            }
        }

        public override void Update()
        {
            base.Update();
            if (++_tick != Config.Tick) return;
            _tick = 0;
            try
            {
                foreach (var wormhole in Config.WormholeGates)
                {
                    var gatePoint = new Vector3D(wormhole.X, wormhole.Y, wormhole.Z);
                    var gate = new BoundingSphereD(gatePoint, Config.GateRadius);

                    WormholeTransferOut(wormhole.SendTo, gatePoint, gate);
                    WormholeTransferIn(wormhole.Name.Trim(), gatePoint, gate);
                }
            }
            catch (Exception e)
            {
                Log.Error(e, "Could not run Wormhole");
            }

            try
            {
                //check transfer status
                foreach (var file in Directory.EnumerateFiles(_gridDirReceived, "*.sbc"))
                {
                    //if all other files have been correctly removed then remove safety to stop duplication
                    var fileName = Path.GetFileName(file);
                    if (!File.Exists(Path.Combine(_gridDirSent, fileName)) &&
                        !File.Exists(Path.Combine(_gridDir, fileName)))
                        File.Delete(file);
                }
            }
            catch
            {
                //no issue file might in deletion process
            }
        }

        public void Save()
        {
            _config.Save();
        }

        private void SetupConfig()
        {
            var configFile = Path.Combine(StoragePath, "Wormhole.cfg");
            try
            {
                _config = Persistent<Config>.Load(configFile);
            }
            catch (Exception e)
            {
                Log.Warn(e);
            }
        }


        public void WormholeTransferOut(string sendTo, Vector3D gatePoint, BoundingSphereD gate)
        {
            foreach (var grid in MyAPIGateway.Entities.GetTopMostEntitiesInSphere(ref gate).OfType<IMyCubeGrid>())
            {
                var gts = MyAPIGateway.TerminalActionsHelper.GetTerminalSystemForGrid(grid);

                if (gts != null)
                {
                    var WormholeDrives = new List<IMyJumpDrive>();
                    gts.GetBlocksOfType(WormholeDrives);

                    foreach (var WormholeDrive in WormholeDrives)
                    {
                        WormholeTransferOutFile(sendTo, grid, WormholeDrive, gatePoint, WormholeDrives);
                    }
                }
            }

        }

        private void WormholeTransferOutFile(string sendTo, IMyCubeGrid grid, IMyJumpDrive wormholeDrive,
            Vector3D gatePoint, IEnumerable<IMyJumpDrive> wormholeDrives)
        {
            if (Config.JumpDriveSubId.Split(',').All(s => s.Trim() != wormholeDrive.BlockDefinition.SubtypeId) && !Config.WorkWithAllJd)
                return;

            Request request = default;
            try
            {
                request = MyAPIGateway.Utilities.SerializeFromXML<Request>(wormholeDrive.CustomData);
            }
            catch { }

            string pickedDestination = default;

            if (request != null)
            {
                if (request.PluginRequest)
                {
                    if (request.Destination != null)
                    {
                        if (sendTo.Split(',').Any(s => s.Trim() == request.Destination.Trim()))
                            pickedDestination = request.Destination.Trim();
                    }
                    Request reply = new Request
                    {
                        PluginRequest = false,
                        Destination = null,
                        Destinations = sendTo.Split(',').Select(s => s.Trim()).ToArray()
                    };
                    wormholeDrive.CustomData = MyAPIGateway.Utilities.SerializeToXML(reply);
                }
            }
            else
            {
                Request reply = new Request
                {
                    PluginRequest = false,
                    Destination = null,
                    Destinations = sendTo.Split(',').Select(s => s.Trim()).ToArray()
                };
                wormholeDrive.CustomData = MyAPIGateway.Utilities.SerializeToXML(reply);
            }

            if (Config.AutoSend && sendTo.Split(',').Length == 1)
                pickedDestination = sendTo.Split(',')[0].Trim();

            if (pickedDestination == null)
                return;

            var playerInCharge = MyAPIGateway.Players.GetPlayerControllingEntity(grid);

            if (playerInCharge?.Identity == null || !wormholeDrive.HasPlayerAccess(playerInCharge.Identity.IdentityId) ||
                    !Utilities.HasRightToMove(playerInCharge, (MyCubeGrid)grid))
                return;

            wormholeDrive.CurrentStoredPower = 0f;
            foreach (var disablingWormholeDrive in wormholeDrives)
            {
                if (Config.JumpDriveSubId.Split(',')
                        .Any(s => s.Trim() == disablingWormholeDrive.BlockDefinition.SubtypeId) ||
                        Config.WorkWithAllJd)
                    disablingWormholeDrive.Enabled = false;
            }
            var grids = Utilities.FindGridList(grid.EntityId.ToString(), (MyCharacter)playerInCharge.Character,
                Config.IncludeConnectedGrids);

            if (grids == null || grids.Count == 0)
                return;

            MyVisualScriptLogicProvider.CreateLightning(gatePoint);

            //NEED TO DROP ENEMY GRIDS
            if (Config.WormholeGates.Any(s => s.Name.Trim() == pickedDestination.Split(':')[0]))
            {
                foreach (var internalWormhole in Config.WormholeGates)
                {
                    if (internalWormhole.Name.Trim() == pickedDestination.Split(':')[0].Trim())
                    {
                        var box = wormholeDrive.GetTopMostParent().PositionComp.WorldAABB;
                        var toGatePoint = new Vector3D(internalWormhole.X, internalWormhole.Y, internalWormhole.Z);

                        Utilities.UpdateGridsPositionAndStopLive(newPosition: Utilities.FindFreePos(new BoundingSphereD(toGatePoint, Config.GateRadius),
                            (float)(Vector3D.Distance(box.Center, box.Max) + 50.0)) ?? Vector3D.Zero, grids: wormholeDrive.GetTopMostParent());

                        MyVisualScriptLogicProvider.CreateLightning(toGatePoint);
                    }
                }
                return;
            }

            var destination = pickedDestination.Split(':');

            if (3 != destination.Length)
                throw new ArgumentException("failed parsing destination '" + destination?.ToString() + "'");

            Utilities.TransferFileInfo transferFileInfo = new Utilities.TransferFileInfo
            {
                DestinationWormhole = destination[0],
                SteamUserId = playerInCharge.SteamUserId,
                PlayerName = playerInCharge.DisplayName,
                GridName = grid.DisplayName,
                Time = DateTime.Now
            };

            Log.Info("creating filetransfer:" + transferFileInfo.CreateLogString());

            var filename = transferFileInfo.CreateFileName();
            var objectBuilders = new List<MyObjectBuilder_CubeGrid>();

            foreach (var mygrid in grids)
            {
                if (mygrid.GetObjectBuilder() is not MyObjectBuilder_CubeGrid objectBuilder)
                    throw new ArgumentException(mygrid?.ToString() + " has a ObjectBuilder thats not for a CubeGrid");

                objectBuilders.Add(objectBuilder);
            }

            var identitiesMap = (from b in objectBuilders.SelectMany((MyObjectBuilder_CubeGrid b) => b.CubeBlocks).SelectMany(GetIds).Distinct()
                                 where !Sync.Players.IdentityIsNpc(b)
                                 select b).ToDictionary((long b) => b, (long b) => Sync.Players.TryGetIdentity(b).GetObjectBuilder());

            var sittingPlayerIdentityIds = new HashSet<long>();
            foreach (var cubeBlock in objectBuilders.SelectMany((MyObjectBuilder_CubeGrid cubeGrid) => cubeGrid.CubeBlocks))
            {
                if (!Config.ExportProjectorBlueprints)
                {
                    if (cubeBlock is MyObjectBuilder_ProjectorBase projector)
                        projector.ProjectedGrids = null;
                }

                if (cubeBlock is MyObjectBuilder_Cockpit cockpit)
                {
                    MyObjectBuilder_Character pilot = cockpit.Pilot;
                    if (pilot != null && pilot.OwningPlayerIdentityId.HasValue)
                    {
                        ulong playerSteamId = Sync.Players.TryGetSteamId(cockpit.Pilot.OwningPlayerIdentityId.Value);
                        sittingPlayerIdentityIds.Add(cockpit.Pilot.OwningPlayerIdentityId.Value);
                        ModCommunication.SendMessageTo(new JoinServerMessage(destination[1] + ":" + destination[2]), playerSteamId);
                    }
                }
            }

            using (var stream = File.Create(Utilities.CreateBlueprintPath(Path.Combine(Config.Folder, "admingates"), filename)))
            {
                Serializer.Serialize(stream, new TransferFile
                {
                    Grids = objectBuilders,
                    IdentitiesMap = identitiesMap,
                    PlayerIdsMap = (from b in identitiesMap.Select(delegate (KeyValuePair<long, MyObjectBuilder_Identity> b)
                        {
                            Sync.Players.TryGetPlayerId(b.Key, out var result);
                            return (b.Key, result.SteamId);
                        })
                                    where b.SteamId != 0
                                    select b).ToDictionary<(long, ulong), long, ulong>(((long Key, ulong SteamId) b) => b.Key, ((long Key, ulong SteamId) b) => b.SteamId)
                });
            }

            foreach (MyIdentity identity in from b in sittingPlayerIdentityIds.Select(Sync.Players.TryGetIdentity)
                                            where b.Character != null
                                            select b)
            {
                Utilities.KillCharacter(identity.Character);
            }

            foreach (var cubeGrid in grids)
            {
                cubeGrid?.Close();
            }

            // Saves the game if enabled in config.
            if (Config.SaveOnExit && (_saveOnExitTask == null || _saveOnExitTask.IsCompleted))
                // (re)Starts the task if it has never been started o´r is done
                _saveOnExitTask = Torch.Save();

            File.Create(Utilities.CreateBlueprintPath(_gridDirSent, filename)).Dispose();
            static IEnumerable<long> GetIds(MyObjectBuilder_CubeBlock block)
            {
                if (block.Owner > 0)
                    yield return block.Owner;
                if (block.BuiltBy > 0)
                    yield return block.BuiltBy;
            }
        }

        public void WormholeTransferIn(string wormholeName, Vector3D gatePoint, BoundingSphereD gate)
        {
            EnsureDirectoriesCreated();

            var changes = false;

            foreach (string file in from s in Directory.EnumerateFiles(_gridDir, "*.sbc")
                                    where Path.GetFileNameWithoutExtension(s).Split('_')[0] == wormholeName
                                    select s)
            {
                //if file not null if file exists if file is done being sent and if file hasnt been received before
                var fileName = Path.GetFileName(file);
                if (!File.Exists(file) || !File.Exists(Path.Combine(_gridDirSent, fileName)) ||
                    File.Exists(Path.Combine(_gridDirReceived, fileName)))
                    continue;

                Log.Info("Processing recivied grid: " + fileName);
                var fileTransferInfo = Utilities.TransferFileInfo.ParseFileName(fileName);
                if (fileTransferInfo == null)
                {
                    Log.Error("Error parsing file name");
                    continue;
                }

                TransferFile transferFile;
                try
                {
                    using var stream = File.OpenRead(file);
                    transferFile = Serializer.Deserialize<TransferFile>(stream);
                    if (transferFile.Grids == null || transferFile.IdentitiesMap == null)
                        throw new InvalidOperationException("File is empty or invalid");
                }
                catch (Exception e)
                {
                    Log.Error(e, "Corrupted file at " + fileName);
                    continue;
                }

                // if player online, process now, else queue util player join
                MyCharacter character = Sync.Players.TryGetPlayerIdentity(new MyPlayer.PlayerId(fileTransferInfo.SteamUserId))?.Character;
                if (character != null)
                    Utilities.KillCharacter(character);

                if (MyPlayerCollectionExtensions.TryGetPlayerBySteamId(Sync.Players, fileTransferInfo.SteamUserId, 0) != null)
                    WormholeTransferInQueue(transferFile, fileTransferInfo, gatePoint, gate);
                else
                    _queuedSpawns[fileTransferInfo.SteamUserId] = (fileTransferInfo, transferFile, gatePoint, gate);

                changes = true;
                if (Config.GridBackup)
                {
                    string backupPath = Path.Combine(_gridDirBackup, fileName);
                    if (!File.Exists(backupPath))
                        File.Copy(Path.Combine(_gridDir, fileName), backupPath);
                }

                File.Delete(Path.Combine(_gridDirSent, fileName));
                File.Delete(Path.Combine(_gridDir, fileName));
                File.Create(Path.Combine(_gridDirReceived, fileName)).Dispose();
            }

            // Saves game on enter if enabled in config.
            if (changes && Config.SaveOnEnter && (_saveOnEnterTask == null || _saveOnEnterTask.IsCompleted))
                _saveOnEnterTask = Torch.Save();
        }

        private void WormholeTransferInQueue(TransferFile file, Utilities.TransferFileInfo fileTransferInfo,
            Vector3D gatePosition, BoundingSphereD gate)
        {
            Log.Info("processing filetransfer:" + fileTransferInfo.CreateLogString());

            var playerId = Sync.Players.TryGetPlayerIdentity(new MyPlayer.PlayerId(fileTransferInfo.SteamUserId));

            // to prevent from hungry trash collector
            if (playerId != null)
                playerId.LastLogoutTime = DateTime.Now;

            var gridBlueprints = file.Grids;
            if (gridBlueprints == null || gridBlueprints.Count < 1)
            {
                Log.Error("can't find any blueprints in: " + fileTransferInfo.CreateLogString());
                return;
            }

            var identitiesToChange = new Dictionary<long, long>();
            long k;
            ulong v;
            foreach (var item in file.PlayerIdsMap.Where((KeyValuePair<long, ulong> b) => Sync.Players.TryGetPlayerIdentity(new MyPlayer.PlayerId(b.Value)) == null))
            {
                item.Deconstruct(out k, out v);
                long identityId = k;
                ulong clientId2 = v;
                var ob = file.IdentitiesMap[identityId];
                ob.IdentityId = MyEntityIdentifier.AllocateId(MyEntityIdentifier.ID_OBJECT_TYPE.IDENTITY);
                Sync.Players.CreateNewIdentity(ob).PerformFirstSpawn();
                Sync.Players.InitNewPlayer(new MyPlayer.PlayerId(clientId2), new MyObjectBuilder_Player
                {
                    IdentityId = ob.IdentityId
                });
            }
            foreach (var item2 in file.PlayerIdsMap)
            {
                item2.Deconstruct(out k, out v);
                long oldIdentityId = k;
                ulong clientId = v;
                MyIdentity identity = Sync.Players.TryGetPlayerIdentity(new MyPlayer.PlayerId(clientId));
                identitiesToChange[oldIdentityId] = identity.IdentityId;
                if (identity.Character != null)
                    Utilities.KillCharacter(identity.Character);
            }
            MyIdentity requesterIdentity = null;
            if (!Config.KeepOwnership)
                requesterIdentity = Sync.Players.TryGetPlayerIdentity(new MyPlayer.PlayerId(fileTransferInfo.SteamUserId));

            foreach (var cubeBlock in gridBlueprints.SelectMany((MyObjectBuilder_CubeGrid b) => b.CubeBlocks))
            {
                if (!Config.KeepOwnership && requesterIdentity != null)
                {
                    cubeBlock.Owner = requesterIdentity.IdentityId;
                    cubeBlock.BuiltBy = requesterIdentity.IdentityId;
                    continue;
                }
                if (identitiesToChange.TryGetValue(cubeBlock.BuiltBy, out var builtBy))
                    cubeBlock.BuiltBy = builtBy;

                if (identitiesToChange.TryGetValue(cubeBlock.Owner, out var owner))
                    cubeBlock.Owner = owner;
            }

            var pos = Utilities.FindFreePos(gate, Utilities.FindGridsRadius(gridBlueprints));
            if (!pos.HasValue || !Utilities.UpdateGridsPositionAndStop(gridBlueprints, pos.Value))
            {
                Log.Warn("no free space available for grid '" + fileTransferInfo.GridName + "' at wormhole '" +
                         fileTransferInfo.DestinationWormhole + "'");
                return;
            }

            var savedCharacters = new Dictionary<long, MyObjectBuilder_Character>();
            foreach (var cockpit in gridBlueprints.SelectMany((MyObjectBuilder_CubeGrid grid) => grid.CubeBlocks.OfType<MyObjectBuilder_Cockpit>()))
            {
                if (cockpit.Pilot != null)
                {
                    var pilot = cockpit.Pilot;
                    Utilities.RemovePilot(cockpit);
                    if (Config.PlayerRespawn && identitiesToChange.TryGetValue(pilot.OwningPlayerIdentityId.GetValueOrDefault(), out var newIdentityId))
                        savedCharacters[newIdentityId] = pilot;
                }
            }

            MyEntities.RemapObjectBuilderCollection(gridBlueprints);
            foreach (MyObjectBuilder_CubeGrid gridBlueprint in gridBlueprints)
            {
                MyEntity entity = MyEntities.CreateFromObjectBuilderNoinit(gridBlueprint);
                MyEntities.InitEntity(gridBlueprint, ref entity);
                MyEntities.Add(entity);
                OnGridSpawned(entity, savedCharacters);
            }
            MyVisualScriptLogicProvider.CreateLightning(gatePosition);
        }

        private static void OnGridSpawned(MyEntity entity, IDictionary<long, MyObjectBuilder_Character> savedCharacters)
        {
            MyCubeGrid grid = (MyCubeGrid)entity;
            foreach (MyCockpit cockpit in from b in grid.GetFatBlocks().OfType<MyCockpit>()
                                          orderby (!(b is MyCryoChamber)) ? 0 : 1
                                          select b)
            {
                if (savedCharacters.Count < 1)
                    break;

                if (cockpit.Pilot != null)
                    continue;

                var (identity, ob) = savedCharacters.First();
                savedCharacters.Remove(identity);
                ob.OwningPlayerIdentityId = identity;
                ob.EntityId = 0L;
                MatrixD matrix = cockpit.WorldMatrix;
                matrix.Translation -= Vector3.Up - Vector3.Forward;
                ob.PositionAndOrientation = new MyPositionAndOrientation(matrix);
                MyEntity characterEntity = MyEntities.CreateFromObjectBuilderNoinit(ob);
                MyCharacter character = (MyCharacter)characterEntity;
                MyEntities.InitEntity(ob, ref characterEntity);
                MyEntities.Add(character);
                MyIdentity myIdentity = Sync.Players.TryGetIdentity(identity);
                Utilities.KillCharacters(myIdentity.SavedCharacters);
                myIdentity.ChangeCharacter(character);
                cockpit.AttachPilot(character, storeOriginalPilotWorld: false, calledFromInit: false, merged: true);
                if (Sync.Players.TryGetPlayerId(identity, out var playerId) && Sync.Players.TryGetPlayerById(playerId, out var player))
                {
                    character.SetPlayer(player);
                    Sync.Players.SetControlledEntity(Sync.Players.TryGetSteamId(identity), cockpit);
                    Sync.Players.RevivePlayer(player);
                    MySession.SendVicinityInformation(cockpit.CubeGrid.EntityId, new EndpointId(playerId.SteamId));
                    MyMultiplayer.RaiseStaticEvent((IMyEventOwner _) => _closeRespawnScreenAction.Value, new EndpointId(player.Id.SteamId));
                    Wormhole.RefreshUtil.EntityRefresh.RefreshAroundGrid(cockpit.CubeGrid);
                }
            }
        }

        private void EnsureDirectoriesCreated()
        {
            if (_gridDir == null)
                _gridDir = Path.Combine(Config.Folder, "admingates");
            if (_gridDirSent == null)
                _gridDirSent = Path.Combine(Config.Folder, "admingatesconfirmsent");
            if (_gridDirReceived == null)
                _gridDirReceived = Path.Combine(Config.Folder, "admingatesconfirmreceived");
            if (_gridDirBackup == null)
                _gridDirBackup = Path.Combine(Config.Folder, "grids_backup");
            if (!Directory.Exists(_gridDir))
                Directory.CreateDirectory(_gridDir);
            if (!Directory.Exists(_gridDirSent))
                Directory.CreateDirectory(_gridDirSent);
            if (!Directory.Exists(_gridDirReceived))
                Directory.CreateDirectory(_gridDirReceived);
            if (!Directory.Exists(_gridDirBackup))
                Directory.CreateDirectory(_gridDirBackup);
        }
    }
}
