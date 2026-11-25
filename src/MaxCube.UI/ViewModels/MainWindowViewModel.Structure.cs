using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MaxCube.Application.Abstractions;
using MaxCube.Application.Services;
using MaxCube.Domain.ValueObjects;

namespace MaxCube.UI.ViewModels;

/// <summary>
/// Gremium Runde 5: Strukturpflege (Raum/Gerät umbenennen, löschen, verschieben, sortieren)
/// und Anlernen neuer Geräte. Jede verändernde Aktion verlangt die ausdrückliche Bestätigung
/// („Ich habe verstanden“), weil sie Funktelegramme und Metadaten des Cubes ändert.
/// </summary>
public partial class MainWindowViewModel
{
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RenameRoomCommand))]
    private string renameRoomText = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RenameDeviceCommand))]
    private string renameDeviceText = string.Empty;

    [ObservableProperty]
    private DeviceViewModel? selectedDevice;

    /// <summary>Bestätigung für Löschen, Verschieben und Zuordnen.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(DeleteRoomCommand))]
    [NotifyCanExecuteChangedFor(nameof(DeleteDeviceCommand))]
    [NotifyCanExecuteChangedFor(nameof(MoveDeviceCommand))]
    [NotifyCanExecuteChangedFor(nameof(AssignFoundDeviceCommand))]
    private bool confirmChanges;

    /// <summary>Beim Löschen zusätzlich auf Werkseinstellungen zurücksetzen (t: mit Force-Flag).</summary>
    [ObservableProperty]
    private bool factoryReset;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(MoveDeviceCommand))]
    private RoomOption? selectedMoveTarget;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AssignFoundDeviceCommand))]
    private RoomOption? selectedAssignTarget;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AssignFoundDeviceCommand))]
    private string newRoomName = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AssignFoundDeviceCommand))]
    private string assignDeviceName = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AssignFoundDeviceCommand))]
    private FoundDevice? foundDevice;

    [ObservableProperty]
    private string foundDeviceText = string.Empty;

    [ObservableProperty]
    private string structureHint = string.Empty;

    public bool HasStructureHint => StructureHint.Length > 0;

    partial void OnStructureHintChanged(string value) => OnPropertyChanged(nameof(HasStructureHint));

    private bool CanRenameRoom => CanAdminRoom && RenameRoomText.Trim().Length > 0;

    private bool CanRenameDevice => CanAdmin && SelectedDevice is not null && RenameDeviceText.Trim().Length > 0;

    private bool CanDeleteRoom => CanAdminRoom && ConfirmChanges;

    private bool CanDeleteDevice => CanAdmin && SelectedDevice is not null && ConfirmChanges;

    private bool CanMoveDevice
        => CanAdmin && SelectedDevice is not null && SelectedMoveTarget is not null && ConfirmChanges;

    private bool CanAssign
        => CanAdmin && ConfirmChanges && FoundDevice is not null && AssignDeviceName.Trim().Length > 0
            && (NewRoomName.Trim().Length > 0 || SelectedAssignTarget is not null);

    private bool CanMoveRoom => CanAdminRoom && Rooms.Count > 1;

    [RelayCommand(CanExecute = nameof(CanRenameRoom))]
    private async Task RenameRoomAsync()
    {
        var room = SelectedRoom;
        if (room is null)
        {
            return;
        }

        await RunAsync(async token =>
        {
            await _service.RenameRoomAsync(room.Id, RenameRoomText, token);
            await RefreshAuditAsync();
        }, L("ErrStructure"), L("OkRenameRoom"));
    }

    [RelayCommand(CanExecute = nameof(CanDeleteRoom))]
    private async Task DeleteRoomAsync()
    {
        var room = SelectedRoom;
        if (room is null)
        {
            return;
        }

        await RunAsync(async token =>
        {
            await _service.DeleteRoomAsync(room.Id, FactoryReset, ConfirmChanges, token);
            ConfirmChanges = false;
            await RefreshAuditAsync();
        }, L("ErrStructure"), L("OkDeleteRoom"));
    }

    [RelayCommand(CanExecute = nameof(CanMoveRoom))]
    private Task MoveRoomUpAsync() => MoveRoomAsync(-1);

    [RelayCommand(CanExecute = nameof(CanMoveRoom))]
    private Task MoveRoomDownAsync() => MoveRoomAsync(+1);

    private async Task MoveRoomAsync(int direction)
    {
        var room = SelectedRoom;
        if (room is null)
        {
            return;
        }

        var order = Rooms.Select(entry => entry.Id).ToList();
        var index = order.IndexOf(room.Id);
        var target = index + direction;
        if (index < 0 || target < 0 || target >= order.Count)
        {
            return;
        }

        (order[index], order[target]) = (order[target], order[index]);
        await RunAsync(async token =>
        {
            await _service.ReorderRoomsAsync(order, token);
            await RefreshAuditAsync();
        }, L("ErrStructure"), L("OkReorder"));
    }

    [RelayCommand(CanExecute = nameof(CanRenameDevice))]
    private async Task RenameDeviceAsync()
    {
        var device = SelectedDevice;
        if (device is null)
        {
            return;
        }

        await RunAsync(async token =>
        {
            await _service.RenameDeviceAsync(RadioAddress.Parse(device.Address), RenameDeviceText, token);
            await RefreshAuditAsync();
        }, L("ErrStructure"), L("OkRenameDevice"));
    }

    [RelayCommand(CanExecute = nameof(CanDeleteDevice))]
    private async Task DeleteDeviceAsync()
    {
        var device = SelectedDevice;
        if (device is null)
        {
            return;
        }

        await RunAsync(async token =>
        {
            await _service.DeleteDeviceAsync(RadioAddress.Parse(device.Address), FactoryReset, ConfirmChanges, token);
            ConfirmChanges = false;
            await RefreshAuditAsync();
        }, L("ErrStructure"), L("OkDeleteDevice"));
    }

    [RelayCommand(CanExecute = nameof(CanMoveDevice))]
    private async Task MoveDeviceAsync()
    {
        var device = SelectedDevice;
        var target = SelectedMoveTarget;
        if (device is null || target is null)
        {
            return;
        }

        await RunAsync(async token =>
        {
            StructureHint = await _service.MoveDeviceAsync(
                RadioAddress.Parse(device.Address), target.Id, ConfirmChanges, token) ?? string.Empty;
            ConfirmChanges = false;
            await RefreshAuditAsync();
        }, L("ErrStructure"), L("OkMoveDevice"));
    }

    // --- Anlernen ---------------------------------------------------------------------

    [RelayCommand(CanExecute = nameof(CanAdmin))]
    private async Task SearchNewDeviceAsync()
    {
        FoundDevice = null;
        FoundDeviceText = L("SearchRunning", (int)CubeApplicationService.InclusionTotal.TotalSeconds);
        await RunAsync(async token =>
        {
            var found = await _service.SearchNewDeviceAsync(CubeApplicationService.InclusionTotal, token);
            FoundDevice = found;
            FoundDeviceText = found is null
                ? L("SearchNone")
                : L("SearchFound", found.Type, found.Address, found.SerialNumber);
            if (found is not null && AssignDeviceName.Trim().Length == 0)
            {
                AssignDeviceName = $"{found.Type} {found.SerialNumber}";
            }

            await RefreshAuditAsync();
        }, L("ErrSearch"), L("OkSearch"));
    }

    [RelayCommand(CanExecute = nameof(CanAssign))]
    private async Task AssignFoundDeviceAsync()
    {
        var found = FoundDevice;
        if (found is null)
        {
            return;
        }

        var creating = NewRoomName.Trim().Length > 0;
        var roomId = creating ? 0 : SelectedAssignTarget?.Id ?? 0;
        await RunAsync(async token =>
        {
            await _service.AssignNewDeviceAsync(
                found, AssignDeviceName, roomId, creating ? NewRoomName : null, ConfirmChanges, token);
            FoundDevice = null;
            FoundDeviceText = string.Empty;
            AssignDeviceName = string.Empty;
            NewRoomName = string.Empty;
            ConfirmChanges = false;
            await RefreshAuditAsync();
        }, L("ErrStructure"), L("OkAssign"));
    }

    /// <summary>Liest den Zustand nach einer Strukturänderung erneut vom Cube (Source of Truth).</summary>
    [RelayCommand(CanExecute = nameof(CanAdmin))]
    private async Task ReloadStateAsync()
    {
        await RunAsync(async token =>
        {
            await _service.ReloadAsync(token);
            IsConnected = true;
            await RefreshAuditAsync();
        }, L("ErrReload"), L("OkReload"));
    }
}
