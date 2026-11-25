namespace Android_Teleprompter;

using System.Text.Json;
using AndroidTeleprompter.Models;
#if ANDROID
using AndroidTeleprompter.Services;
#endif
using Android_Teleprompter.ViewModels;

public partial class MainPage : ContentPage
{
#if ANDROID
    private readonly BluetoothLePeripheralService _bluetoothService = new();
#endif
    private readonly MainViewModel _viewModel;

    public MainPage()
    {
        InitializeComponent();
        
        _viewModel = new MainViewModel(new Services.FileService(), new Services.ScrollingService());
        BindingContext = _viewModel;
#if ANDROID
        _bluetoothService.CommandReceived += HandleRemoteCommand;
        _bluetoothService.ClientConnected += () => _bluetoothService.NotifyStatus("connected");
        _bluetoothService.ClientDisconnected += () => _bluetoothService.NotifyStatus("disconnected");
        _bluetoothService.ClientConnected += () =>
            MainThread.BeginInvokeOnMainThread(() => _viewModel.IsRemoteConnected = true);
        _bluetoothService.ClientDisconnected += () =>
            MainThread.BeginInvokeOnMainThread(() => _viewModel.IsRemoteConnected = false);
#endif
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
#if ANDROID
        var permission = await Permissions.CheckStatusAsync<BluetoothLePermissions>();
        if (permission != PermissionStatus.Granted)
        {
            permission = await Permissions.RequestAsync<BluetoothLePermissions>();
        }

        if (permission != PermissionStatus.Granted)
        {
            await DisplayAlertAsync("Bluetooth permission required", "Allow nearby-device permissions to connect the remote.", "OK");
            return;
        }

        try
        {
            _bluetoothService.StartAdvertising();
        }
        catch (Exception exception) when (exception is InvalidOperationException or Java.Lang.SecurityException)
        {
            await DisplayAlertAsync("Bluetooth unavailable", exception.Message, "OK");
        }
#endif
    }

    protected override void OnDisappearing()
    {
#if ANDROID
        _bluetoothService.StopAdvertising();
        _viewModel.IsRemoteConnected = false;
#endif
        base.OnDisappearing();
    }

#if ANDROID
    private void HandleRemoteCommand(byte[] data)
    {
        RemoteMessage? message;
        try
        {
            message = RemoteMessageCodec.Decode(data);
        }
        catch (JsonException exception)
        {
            System.Diagnostics.Debug.WriteLine($"Invalid remote command: {exception.Message}");
            return;
        }

        if (message is null)
        {
            System.Diagnostics.Debug.WriteLine("Remote command payload was empty or invalid.");
            return;
        }

        MainThread.BeginInvokeOnMainThread(async () =>
        {
            if (message.SpeedDps is double speed)
            {
                _viewModel.SetSpeed(speed);
            }

            switch (message.Command)
            {
                case RemoteCommand.Play:
                    await _viewModel.PlayAsync(TeleprompterScrollView);
                    break;
                case RemoteCommand.Pause:
                    await _viewModel.PauseAsync();
                    break;
                case RemoteCommand.Restart:
                    await _viewModel.RestartAsync(TeleprompterScrollView);
                    break;
                case RemoteCommand.ScrollUp:
                    await _viewModel.ScrollUpAsync(TeleprompterScrollView);
                    break;
                case RemoteCommand.ScrollDown:
                    await _viewModel.ScrollDownAsync(TeleprompterScrollView);
                    break;
                default:
                    System.Diagnostics.Debug.WriteLine($"Unknown remote command: {message.Command}");
                    break;
            }
        });
    }
#endif
}
