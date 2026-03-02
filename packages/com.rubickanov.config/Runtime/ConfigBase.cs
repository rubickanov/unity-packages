using UnityEngine;

namespace Rubickanov.Config
{
    /// <summary>
    /// Base class for all configuration ScriptableObjects.
    /// </summary>
    public abstract class ConfigBase : ScriptableObject
    {
        /// <summary>
        /// Called after loading to validate config data.
        /// Override to add custom validation logic.
        /// </summary>
        public virtual bool Validate() => true;
    }
}
