using AndroidTeleprompterRemote.Models;
using AndroidTeleprompterRemote.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AndroidTeleprompterRemote.ViewModels;

public partial class MainViewModel : ObservableObject
{
	private readonly BluetoothLeClientService _bluetoothService;
	private CancellationTokenSource? _speedDebounce;
	private bool _isPlaying;
	private bool _isScanning;

	[ObservableProperty]
	private bool _isConnected;

	[ObservableProperty]
	private double _scrollSpeed = 60;

	public MainViewModel(BluetoothLeClientService bluetoothService)
	{
		_bluetoothService = bluetoothService;
		_bluetoothService.Connected += () =>
			MainThread.BeginInvokeOnMainThread(() => IsConnected = true);
		_bluetoothService.Disconnected += () =>
			MainThread.BeginInvokeOnMainThread(() => IsConnected = false);
	}

	public async Task OnAppearingAsync()
	{
		await ScanForConnectionAsync();
	}

	[RelayCommand]
	private async Task ConnectAsync()
	{
		await ScanForConnectionAsync();
	}

	private async Task ScanForConnectionAsync()
	{
		if (IsConnected || _isScanning)
		{
			return;
		}

		_isScanning = true;
		try
		{
			await _bluetoothService.StartScanAsync();
		}
		catch (Exception exception)
		{
			IsConnected = false;
			System.Diagnostics.Debug.WriteLine($"Unable to start BLE scan: {exception.Message}");
		}
		finally
		{
			_isScanning = false;
		}
	}

	[RelayCommand]
	private async Task ScrollUpAsync() =>
		await SendAsync(new RemoteMessage { Command = RemoteCommand.ScrollUp });

	[RelayCommand]
	private async Task ScrollDownAsync() =>
		await SendAsync(new RemoteMessage { Command = RemoteCommand.ScrollDown });

	[RelayCommand]
	private async Task PlayPauseAsync()
	{
		_isPlaying = !_isPlaying;
		await SendAsync(new RemoteMessage
		{
			Command = _isPlaying ? RemoteCommand.Play : RemoteCommand.Pause
		});
	}

	[RelayCommand]
	private async Task RestartAsync() =>
		await SendAsync(new RemoteMessage { Command = RemoteCommand.Restart });

	public async Task SpeedChangedAsync(double value)
	{
		ScrollSpeed = Math.Round(value / 5.0) * 5.0;

		_speedDebounce?.Cancel();
		_speedDebounce?.Dispose();
		_speedDebounce = new CancellationTokenSource();
		var token = _speedDebounce.Token;

		try
		{
			await Task.Delay(300, token);
			await SendAsync(new RemoteMessage
			{
				Command = RemoteCommand.Play,
				SpeedDps = ScrollSpeed
			});
		}
		catch (OperationCanceledException)
		{
		}
	}

	private async Task SendAsync(RemoteMessage message)
	{
		try
		{
			await _bluetoothService.SendAsync(message);
		}
		catch (Exception exception)
		{
			System.Diagnostics.Debug.WriteLine($"Unable to send remote command: {exception.Message}");
		}
	}
}
