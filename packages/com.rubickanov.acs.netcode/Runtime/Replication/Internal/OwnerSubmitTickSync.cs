namespace Rubickanov.ACS.Runtime.Netcode
{
    /// <summary>
    /// Tracks the offset between the owner's estimated ServerTime and the server's
    /// authoritative tick, derived from the senderTick stamped on each owner-auth
    /// submission. First sample seeds exactly; later samples feed an EMA
    /// (alpha = 0.1) so mid-session NGO clock re-syncs converge over a few seconds
    /// without single-frame jitter visibly moving receivedTime.
    ///
    /// Extracted from EntityReplicator so the math can be unit-tested in isolation
    /// of NetworkManager + FastBufferReader.
    /// </summary>
    internal struct OwnerSubmitTickSync
    {
        private double _offset;
        private bool _hasOffset;

        /// <summary>Current EMA-blended offset in ticks. Zero before the first sample.</summary>
        public double Offset => _offset;

        /// <summary>True once the first sample has been applied. Tests and reset paths read this.</summary>
        public bool HasOffset => _hasOffset;

        /// <summary>
        /// Feed a new sample. First call seeds exactly; subsequent calls blend with alpha = 0.1.
        /// </summary>
        public void Update(int serverTick, int senderTick)
        {
            double rawOffset = serverTick - senderTick;
            if (!_hasOffset)
            {
                _offset = rawOffset;
                _hasOffset = true;
                return;
            }
            _offset = 0.9 * _offset + 0.1 * rawOffset;
        }

        /// <summary>
        /// Clear both the offset and the seeded flag. Called on OnNetworkDespawn and
        /// OnGainedOwnership — a new owner has a different clock drift and must re-seed
        /// from scratch rather than blending with the stale EMA.
        /// </summary>
        public void Reset()
        {
            _offset = 0;
            _hasOffset = false;
        }
    }
}
