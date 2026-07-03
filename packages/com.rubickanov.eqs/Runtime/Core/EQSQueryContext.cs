using UnityEngine;

namespace Rubickanov.EQS
{
    /// <summary>
    /// Context for an EQS query execution. Provides the querier's spatial info
    /// and an optional reference position for target-relative generators.
    /// </summary>
    public readonly struct EQSQueryContext
    {
        public readonly Vector3 QuerierPosition;

        /// <summary>
        /// The querier's forward direction. Expected to be a unit vector — tests such as
        /// <see cref="DotProductTest"/> assume it is normalized. Passing <c>transform.forward</c>
        /// satisfies this.
        /// </summary>
        public readonly Vector3 QuerierForward;

        public readonly GameObject? QuerierObject;
        public readonly Vector3? ReferencePosition;
        public readonly object? UserData;

        public EQSQueryContext(
            Vector3 position,
            Vector3 forward,
            GameObject? querierObject = null,
            Vector3? referencePosition = null,
            object? userData = null)
        {
            QuerierPosition = position;
            QuerierForward = forward;
            QuerierObject = querierObject;
            ReferencePosition = referencePosition;
            UserData = userData;
        }
    }
}
