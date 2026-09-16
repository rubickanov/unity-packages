using System;

namespace Rubickanov.UI
{
    public interface ISpinnerHost
    {
        IDisposable Show(string? label = null);
    }
}
