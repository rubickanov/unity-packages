namespace Rubickanov.Config
{
    /// <summary>
    /// Interface for data items that have a unique identifier.
    /// Used by ConfigDatabase for lookups by ID.
    /// </summary>
    public interface IIdentifiable
    {
        string Id { get; }
    }
}
