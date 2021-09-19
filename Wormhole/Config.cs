using System.Linq;
using System.Xml.Serialization;
using Torch;
using Torch.Collections;
using Torch.Views;

namespace Wormhole
{
    public class Config : ViewModel
    {
        private bool _saveOnExit = false;
        private bool _saveOnEnter = false;
        private bool _allowInFaction = true;
        private bool _includeConnectedGrids = true;
        private bool _exportProjectorGrids = true;

        private string _jumpDriveSubId = "WormholeDrive, WormholeDrive_Small";
        private string _thisIp = "";

        private double _GateRadius = 180;
        private string _folder = "";
        private int _tick = 240;
        private bool _playerRespawn = true;
        private bool _workWithAllJd = false;
        private bool _autoSend = false;
        private bool _KeepOwnership = false;
        private bool _gridBackup = true;
		private bool _GateVisuals = false;

        [XmlIgnore]
        public MtObservableList<WormholeGate> WormholeGates { get; } = new MtObservableList<WormholeGate>();
        [XmlElement("WormholeGate")]
        public WormholeGate[] WormholeGates_xml
        {
            get => WormholeGates.ToArray();
            set
            {
                WormholeGates.Clear();
                if (value == null) return;

                for (var i = 0; i < value.Length; i++)
                {
                    var Wormhole = value[i];
                    WormholeGates.Add(Wormhole);
                }
            }
        }

        [Display(Name = "Save Server On Grid Exit", Description = "Warning! May Cause Lag")]
        public bool SaveOnExit
        {
            get => _saveOnExit;
            set => SetValue(ref _saveOnExit, value);
        }
        [Display(Name = "Save Server On Grid Enter", Description = "Warning! May Cause Lag")]
        public bool SaveOnEnter
        {
            get => _saveOnEnter;
            set => SetValue(ref _saveOnEnter, value);
        }

        [Display(Name = "Allow Faction Members")]
        public bool AllowInFaction
        {
            get => _allowInFaction;
            set => SetValue(ref _allowInFaction, value);
        }

        [Display(Name = "Keep Connected Grids", Description = "Keep grids linked by connector")]
        public bool IncludeConnectedGrids
        {
            get => _includeConnectedGrids;
            set => SetValue(ref _includeConnectedGrids, value);
        }

        [Display(Name = "Keep projector blueprints")]
        public bool ExportProjectorBlueprints
        {
            get => _exportProjectorGrids;
            set => SetValue(ref _exportProjectorGrids, value);
        }

        [Display(Name = "JumpDrive SubtypeId", Description = "SubtypeId of your jump drive/wormhole stabilizer")]
        public string JumpDriveSubId
        {
            get => _jumpDriveSubId;
            set => SetValue(ref _jumpDriveSubId, value);
        }

        [Display(Name = "Server IP:Port (for reconnecting)")]
        public string ThisIp
        {
            get => _thisIp;
            set => SetValue(ref _thisIp, value);
        }

        public double GateRadius
        {
            get => _GateRadius;
            set => SetValue(ref _GateRadius, value);
        }

        [Display(Name = "Folder", Description = "Must be shared across all torches")]
        public string Folder
        {
            get => _folder;
            set => SetValue(ref _folder, value);
        }

        [Display(Name = "Tick Rate", Description = "Wormhole runs once out of x ticks")]
        public int Tick
        {
            get => _tick;
            set => SetValue(ref _tick, value);
        }

        [Display(Name = "Respawn Players", Description = "Keep players in cryos/beds/cockpits")]
        public bool PlayerRespawn
        {
            get => _playerRespawn;
            set => SetValue(ref _playerRespawn, value);
        }

        [Display(Name = "Work With All Jump Drives")]
        public bool WorkWithAllJd
        {
            get => _workWithAllJd;
            set => SetValue(ref _workWithAllJd, value);
        }

        public bool AutoSend
        {
            get => _autoSend;
            set => SetValue(ref _autoSend, value);
        }

        [Display(Name = "Keep Ownership", Description = "Keep ownership & builtBy on blocks. If false, all blocks will be transferred to player that requested jump")]
        public bool KeepOwnership
        {
            get => _KeepOwnership;
            set => SetValue(ref _KeepOwnership, value);
        }

        public bool GridBackup
        {
            get => _gridBackup;
            set => SetValue(ref _gridBackup, value);
        }
		
		[Display(Name = "Gate Visuals", Description = "Gate visual effects on jump")]
		public bool GateVisuals
        {
            get => _GateVisuals;
            set => SetValue(ref _GateVisuals, value);
        }
    }
}