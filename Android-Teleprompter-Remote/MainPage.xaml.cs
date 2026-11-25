using AndroidTeleprompterRemote.ViewModels;
#if ANDROID
using AndroidTeleprompterRemote.Services;
#endif

namespace Android_Teleprompter_Remote;

public partial class MainPage : ContentPage
{
	private readonly MainViewModel _viewModel;

	public MainPage(MainViewModel viewModel)
	{
		InitializeComponent();
		_viewModel = viewModel;
		BindingContext = viewModel;
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
			await DisplayAlertAsync(
				"Bluetooth permission required",
				"Allow nearby-device permission so the remote can find and connect to the teleprompter.",
				"OK");
			return;
		}
#endif
		await _viewModel.OnAppearingAsync();
	}

	private async void OnSpeedChanged(object? sender, ValueChangedEventArgs e)
	{
		await _viewModel.SpeedChangedAsync(e.NewValue);
	}
}
