using Unity.Netcode;

namespace Rubickanov.ACS.Runtime.Netcode
{
    internal interface IEventBroadcaster
    {
        void SendEvent(ulong networkObjectId, byte eventIndex, FastBufferWriter writer,
            AuthorityMode authority, Reliability reliability, bool isOwnerSubmit);
    }
}
