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
        public string Address { get; }

        public RegisterConfigAttribute(string address)
        {
            Address = address;
        }
    }
}
