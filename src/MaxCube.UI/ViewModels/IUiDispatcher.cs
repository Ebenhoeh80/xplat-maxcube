namespace MaxCube.UI.ViewModels;

/// <summary>
/// Vermittelt zwischen Hintergrund-Threads (Gateway/Application-Events) und dem UI-Thread.
/// Die ViewModels bleiben dadurch frei von Avalonia-Controls und in Tests ohne UI lauffähig.
/// </summary>
public interface IUiDispatcher
{
    bool CheckAccess();

    void Post(Action action);
}

/// <summary>Direkte Ausführung für Design-Time und Headless-Tests.</summary>
public sealed class ImmediateUiDispatcher : IUiDispatcher
{
    public bool CheckAccess() => true;

    public void Post(Action action) => action();
}
