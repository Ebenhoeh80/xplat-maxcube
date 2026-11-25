using Avalonia.Threading;
using MaxCube.UI.ViewModels;

namespace MaxCube.UI.Desktop;

/// <summary>Bindet die ViewModel-Events an Avalonias UI-Thread.</summary>
public sealed class AvaloniaUiDispatcher : IUiDispatcher
{
    public bool CheckAccess() => Dispatcher.UIThread.CheckAccess();

    public void Post(Action action) => Dispatcher.UIThread.Post(action);
}
