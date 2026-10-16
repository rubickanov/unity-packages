namespace Rubickanov.Characters.Netcode
{
    /// <summary>
    /// A game's glue on its character (camera, input, spawn point…): what it does with its own copy and with every other
    /// copy. Ownership of a character never changes.
    /// </summary>
    public interface INetworkCharacterSide
    {
        /// <summary>This machine simulates the character (a player's own, the host's bots). Before the package listens to it.</summary>
        void OnLocal(NetworkCharacter character);

        /// <summary>Another machine simulates it. After the package made its playback, before its components' Start.</summary>
        void OnReplica(NetworkCharacter character);

        /// <summary>This machine's own copy goes away.</summary>
        void OnLocalDespawn(NetworkCharacter character);

        /// <summary>Another machine's character goes away; must not touch this machine's camera or input.</summary>
        void OnReplicaDespawn(NetworkCharacter character);
    }
}
