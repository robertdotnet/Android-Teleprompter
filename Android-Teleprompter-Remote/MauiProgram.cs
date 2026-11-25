using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using AndroidTeleprompterRemote.Services;
using AndroidTeleprompterRemote.ViewModels;
using Microsoft.Maui.Hosting;

namespace Android_Teleprompter_Remote;

public static class MauiProgram
{
	public static MauiApp CreateMauiApp()
	{
		var builder = MauiApp.CreateBuilder();
		builder
			.UseMauiApp<App>()
			.ConfigureFonts(fonts =>
			{
				fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
				fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
			});

#if DEBUG
		builder.Logging.AddDebug();
#endif

		builder.Services.AddSingleton<BluetoothLeClientService>();
		builder.Services.AddSingleton<MainViewModel>();
		builder.Services.AddTransient<MainPage>();

		return builder.Build();
	}
}
