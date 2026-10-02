using System;

namespace Rubickanov.Config
{
    /// <summary>
    /// Registers the Addressable address for a config type.
    /// </summary>
    /// <example>
    /// [RegisterConfig("Configs/GameSettings")]
    /// public class GameSettings : ConfigBase { }
    /// </example>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
    public class RegisterConfigAttribute : Attribute
    {
        /// <summary>
        /// Addressable address used by <see cref="IAssetLoader"/> to load the config asset.
        /// </summary>
        public string Address { get; }

        public RegisterConfigAttribute(string address)
        {
            Address = address;
        }
    }
}
