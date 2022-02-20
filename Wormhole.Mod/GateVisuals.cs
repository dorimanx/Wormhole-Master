using System.Collections.Generic;
using Sandbox.ModAPI;
using VRage.Game;
using VRage.Game.Components;
using VRageMath;

namespace Wormhole.Mod
{
    [MySessionComponentDescriptor(MyUpdateOrder.NoUpdate)]
    public class GateVisuals : MySessionComponentBase
    {
        public static GateVisuals Instance;
        private readonly Dictionary<uint, WarpParticle> _allEffects = new Dictionary<uint, WarpParticle>();
        private readonly Dictionary<uint, WarpParticle> _enabledEffects = new Dictionary<uint, WarpParticle>();

        public GateVisuals()
        {
            Instance = this;
        }

        public bool CreateEffectForGate(GateDataMessage gate, bool enable = false)
        {
            var effectName = gate.ParticleId;
            var centerMatrix = MatrixD.CreateWorld(gate.Position, gate.Forward, - Vector3D.Up);

            var warpParticle = new WarpParticle(centerMatrix, gate.ParticleId);

            _allEffects[gate.Id] = warpParticle;

            if (enable)
                _enabledEffects[gate.Id] = warpParticle;

            return true;
        }

        public void EnableEffectForGate(uint gateId)
        {
            WarpParticle effect;
            if (!_allEffects.TryGetValue(gateId, out effect))
                return;

            _enabledEffects[gateId] = effect;
            effect.Play();
        }

        public void DisableEffectForGate(uint gateId)
        {
            WarpParticle effect;
            if (!_enabledEffects.TryGetValue(gateId, out effect))
                return;

            _enabledEffects.Remove(gateId);
            effect.Stop();
        }

        private void RemoveAllEffect()
        {
            foreach (var effect in _allEffects)
            {
                var rotatingParticle = effect.Value;
                rotatingParticle.Stop();
            }

            _allEffects.Clear();
            _enabledEffects.Clear();
        }

        protected override void UnloadData()
        {
            if (MyAPIGateway.Multiplayer.IsServer)
                return;

            RemoveAllEffect();
        }

        public class WarpParticle
        {
            public MyParticleEffect Effect;
            public MatrixD InitialMatrix; //Central position
            private readonly string _effectName;

            public WarpParticle(MatrixD initialMatrix, string effectName)
            {
                InitialMatrix = initialMatrix;
                _effectName = effectName;
            }

            public void Play()
            {
                var pos = InitialMatrix.Translation;
                MyParticlesManager.TryCreateParticleEffect(_effectName, ref InitialMatrix, ref pos, uint.MaxValue, out Effect);

                if (Effect != null)
                    Effect.UserScale = 8;
            }

            public void Stop()
            {
                Effect?.StopEmitting(10);
                Effect = null;
            }
        }
    }
}