using System.Threading.Tasks;

namespace Android_Teleprompter.Services
{
    public interface IFileService
    {
        Task<string> ReadTextFileAsync(string filePath);
        Task<string?> PickAndReadFileAsync();
    }
}