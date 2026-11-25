using System.IO;
using System.Threading.Tasks;
using Microsoft.Maui.Devices;
using Microsoft.Maui.Storage;

namespace Android_Teleprompter.Services
{
    public class FileService : IFileService
    {
        public Task<string> ReadTextFileAsync(string filePath)
        {
            return File.ReadAllTextAsync(filePath);
        }

        public async Task<string?> PickAndReadFileAsync()
        {
            var file = await FilePicker.Default.PickAsync(new PickOptions
            {
                PickerTitle = "Select a text file",
                FileTypes = new FilePickerFileType(new Dictionary<DevicePlatform, IEnumerable<string>>
                {
                    [DevicePlatform.Android] = new[] { "text/plain" },
                    [DevicePlatform.iOS] = new[] { "public.plain-text" },
                    [DevicePlatform.MacCatalyst] = new[] { "public.plain-text" },
                    [DevicePlatform.WinUI] = new[] { ".txt" }
                })
            });

            if (file is null)
            {
                return null;
            }

            await using var stream = await file.OpenReadAsync();
            using var reader = new StreamReader(stream);
            return await reader.ReadToEndAsync();
        }
    }
}