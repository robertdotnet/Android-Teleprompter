using AndroidTeleprompterRemote.ViewModels;

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
		await _viewModel.OnAppearingAsync();
	}

	private async void OnSpeedChanged(object? sender, ValueChangedEventArgs e)
	{
		await _viewModel.SpeedChangedAsync(e.NewValue);
	}
}
