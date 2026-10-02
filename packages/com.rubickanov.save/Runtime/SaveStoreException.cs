using System;

namespace Rubickanov.Save
{
    /// <summary>The platform could not read, write, list or delete the player's data: a full disk, a lost permission.</summary>
    public sealed class SaveStoreException : Exception
    {
        public SaveStoreException(string message, Exception inner)
            : base(message, inner)
        {
        }
    }
}
