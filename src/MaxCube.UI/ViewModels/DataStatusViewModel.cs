using System.ComponentModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using MaxCube.UI.Localization;

namespace MaxCube.UI.ViewModels;

/// <summary>
/// Herkunft und Alter der angezeigten Werte. Bewusst UI-nah: die Domäne bleibt frei von
/// Ladezuständen, der Cube bleibt Source of Truth und der SQLite-Cache wird nie als Live
/// ausgegeben (Gremium 0.5.0: UX, Architektur und QA).
/// </summary>
public enum DataReadState
{
    NotRead,
    Loading,
    Live,
    Cached,
    Stale,
    Failed,
}

/// <summary>
/// Ein status text pro Bereich. Symbol UND Text sind immer sichtbar, damit Farbe allein
/// nie die Bedeutung trägt; bei sechs Sprachen ist das die einzige barrierearme Lösung.
/// </summary>
public sealed class DataStatusViewModel : ObservableObject, IDisposable
{
    private readonly Localizer _loc;

    private DataReadState _state = DataReadState.NotRead;
    private DataReadState? _stateBeforeLoading;
    private DateTimeOffset? _lastRead;
    private string _detail = string.Empty;

    public DataStatusViewModel(Localizer localizer)
    {
        _loc = localizer ?? throw new ArgumentNullException(nameof(localizer));
        _loc.PropertyChanged += OnLanguageChanged;
    }

    public DataReadState State => _state;

    public DateTimeOffset? LastRead => _lastRead;

    /// <summary>Nur echte Cube-Lesungen oder ein datierter Cache berechtigen zum Senden.</summary>
    public bool HasRead => _lastRead is not null
        && _state is DataReadState.Live or DataReadState.Cached or DataReadState.Stale;

    public bool IsNotRead => _state == DataReadState.NotRead;

    public string Glyph => _state switch
    {
        DataReadState.NotRead => "○",
        DataReadState.Loading => "◌",
        DataReadState.Live => "●",
        DataReadState.Cached => "◐",
        DataReadState.Stale => "◐",
        DataReadState.Failed => "⚠",
        _ => "○",
    };

    /// <summary>Symbol und lokalisierter Text in einer Zeile – ein Binding je Bereich.</summary>
    public string Text
    {
        get
        {
            var at = _lastRead?.ToLocalTime().ToString("HH:mm", CultureInfo.CurrentCulture) ?? "–";
            var body = _state switch
            {
                DataReadState.NotRead => _loc.Get("DataNotRead"),
                DataReadState.Loading => _loc.Get("DataLoading"),
                DataReadState.Live => _loc.Format("DataLive", at),
                DataReadState.Cached => _loc.Format("DataCached", at),
                DataReadState.Stale => _loc.Format("DataStale", at),
                DataReadState.Failed => string.IsNullOrWhiteSpace(_detail)
                    ? _loc.Get("DataFailed")
                    : _loc.Format("DataFailedDetail", _detail),
                _ => _loc.Get("DataNotRead"),
            };
            return $"{Glyph} {body}";
        }
    }

    public void Update(DataReadState state, DateTimeOffset? lastRead = null, string? detail = null)
    {
        if (state != DataReadState.Loading)
        {
            _stateBeforeLoading = null;
        }

        _state = state;
        _lastRead = lastRead;
        _detail = detail ?? string.Empty;
        OnPropertyChanged(nameof(State));
        OnPropertyChanged(nameof(LastRead));
        OnPropertyChanged(nameof(HasRead));
        OnPropertyChanged(nameof(IsNotRead));
        OnPropertyChanged(nameof(Glyph));
        OnPropertyChanged(nameof(Text));
    }

    public void Reset()
    {
        _stateBeforeLoading = null;
        Update(DataReadState.NotRead);
    }

    public void MarkLoading()
    {
        _stateBeforeLoading = _state;
        Update(DataReadState.Loading, _lastRead, _detail);
    }

    /// <summary>Stellt den Zustand vor einem Abbruch wieder her (kein Fehler).</summary>
    public void Restore()
    {
        if (_stateBeforeLoading is { } previous)
        {
            _stateBeforeLoading = null;
            Update(previous, _lastRead, _detail);
        }
    }

    public void MarkFailed(string? detail) => Update(DataReadState.Failed, _lastRead, detail);

    public void MarkStale(bool stale, DataReadState restoredState)
    {
        if (stale && _state is DataReadState.Live or DataReadState.Cached)
        {
            Update(DataReadState.Stale, _lastRead, _detail);
        }
        else if (!stale && _state == DataReadState.Stale)
        {
            Update(restoredState, _lastRead, _detail);
        }
    }

    private void OnLanguageChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName is "Item[]" or nameof(Localizer.Language) or null)
        {
            OnPropertyChanged(nameof(Text));
        }
    }

    public void Dispose() => _loc.PropertyChanged -= OnLanguageChanged;
}
