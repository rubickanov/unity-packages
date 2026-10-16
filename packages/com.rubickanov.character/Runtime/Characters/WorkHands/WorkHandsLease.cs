using System;

namespace Rubickanov.Characters
{
    /// <summary>Claims idle work hands. A station or another higher-priority presenter may replace the claim.</summary>
    public sealed class WorkHandsLease : IDisposable
    {
        private CharacterRig _rig;
        private readonly IWorkHands _source;
        public bool IsActive => _rig != null && ReferenceEquals(_rig.WorkHands, _source);
        private WorkHandsLease(CharacterRig rig, IWorkHands source) { _rig = rig; _source = source; rig.WorkHands = source; }
        public static WorkHandsLease TryAcquire(CharacterRig rig, IWorkHands source) =>
            rig != null && source != null && !(source is UnityEngine.Object obj && obj == null)
            && rig.WorkHands == null ? new WorkHandsLease(rig, source) : null;
        public void Dispose()
        {
            if (IsActive) _rig.WorkHands = null;
            _rig = null;
        }
    }
}
