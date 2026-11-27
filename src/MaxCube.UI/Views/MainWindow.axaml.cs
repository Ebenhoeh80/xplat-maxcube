using Avalonia.Controls;
using Avalonia.Input;
using MaxCube.UI.ViewModels;

namespace MaxCube.UI.Views;

public partial class MainWindow : Window
{
    private static readonly DataFormat<SwitchPointViewModel> SwitchPointFormat =
        DataFormat.CreateInProcessFormat<SwitchPointViewModel>("MaxCube.WeekSwitchPoint");

    public MainWindow()
    {
        InitializeComponent();

        // Avalonia 12: Drag-Ziel und Handler werden im Code verdrahtet. Nur der kleine
        // Griff startet den Drag; die +/-/Löschen-Buttons bleiben damit klickbar.
        // Der Drop auf der Tagesleiste entspricht einer Zeit im 5-Minuten-Raster.
        DragDrop.SetAllowDrop(WeekTimelineDropTarget, true);
        WeekTimelineDropTarget.AddHandler(DragDrop.DragOverEvent, OnWeekTimelineDragOver);
        WeekTimelineDropTarget.AddHandler(DragDrop.DropEvent, OnWeekTimelineDrop);
    }

    private static SwitchPointViewModel? DraggedPoint(IDataTransfer transfer)
        => transfer.Items
            .Where(item => item.Formats.Contains(SwitchPointFormat))
            .Select(item => item.TryGetRaw(SwitchPointFormat) as SwitchPointViewModel)
            .FirstOrDefault();

    private void OnWeekTimelineDragOver(object? sender, DragEventArgs e)
    {
        e.DragEffects = DraggedPoint(e.DataTransfer) is not null
            ? DragDropEffects.Move
            : DragDropEffects.None;
    }

    private void OnWeekTimelineDrop(object? sender, DragEventArgs e)
    {
        if (DataContext is not MainWindowViewModel viewModel
            || DraggedPoint(e.DataTransfer) is not { } point
            || sender is not Control target
            || target.Bounds.Width <= 0)
        {
            e.DragEffects = DragDropEffects.None;
            return;
        }

        var ratio = Math.Clamp(e.GetPosition(target).X / target.Bounds.Width, 0, 1);
        var minutes = (int)Math.Round(ratio * (24 * 60) / 5.0, MidpointRounding.AwayFromZero) * 5;
        minutes = Math.Clamp(minutes, 5, (23 * 60) + 55);
        var until = new TimeOnly(minutes / 60, minutes % 60);

        e.Handled = viewModel.MoveSwitchPoint(point, until);
        e.DragEffects = e.Handled ? DragDropEffects.Move : DragDropEffects.None;
    }

    private async void OnSwitchPointPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Control control
            || control.DataContext is not SwitchPointViewModel point
            || !e.GetCurrentPoint(control).Properties.IsLeftButtonPressed)
        {
            return;
        }

        var transfer = new DataTransfer();
        transfer.Add(DataTransferItem.Create(SwitchPointFormat, point));
        e.Handled = true;
        await DragDrop.DoDragDropAsync(e, transfer, DragDropEffects.Move);
    }
}
