using System;

namespace Rubickanov.DevConsole
{
    /// <summary>Assigns an autocomplete provider to a specific argument of a console command.</summary>
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
    public class AutoCompleteAttribute : Attribute
    {
        public int ArgumentIndex { get; }
        public Type ProviderType { get; }
        public string[] ProviderArgs { get; }

        /// <param name="argumentIndex">Zero-based index of the parameter to provide suggestions for.</param>
        /// <param name="providerType">Type implementing <see cref="IAutoCompleteProvider"/>.</param>
        /// <param name="providerArgs">Constructor arguments passed to the provider.</param>
        public AutoCompleteAttribute(int argumentIndex, Type providerType, params string[] providerArgs)
        {
            ArgumentIndex = argumentIndex;
            ProviderType = providerType;
            ProviderArgs = providerArgs;
        }
    }
}
