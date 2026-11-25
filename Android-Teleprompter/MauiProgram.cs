using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Android_Teleprompter.Services;

namespace Android_Teleprompter;

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

        // Register services for dependency injection
        builder.Services.AddSingleton<IFileService, FileService>();
        builder.Services.AddSingleton<IScrollingService, ScrollingService>();

		return builder.Build();
	}
}
