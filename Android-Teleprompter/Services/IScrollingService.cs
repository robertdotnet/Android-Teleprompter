using System.Threading;
using System.Threading.Tasks;

namespace Android_Teleprompter.Services
{
    public interface IScrollingService
    {
        Task AutoScrollAsync(CancellationToken cancellationToken, double speed, double startY, double maxY, Microsoft.Maui.Controls.ScrollView scrollView);
        Task ScrollUpAsync(Microsoft.Maui.Controls.ScrollView scrollView, double dpAmount);
        Task ScrollDownAsync(Microsoft.Maui.Controls.ScrollView scrollView, double dpAmount);
        Task ResetScrollAsync(Microsoft.Maui.Controls.ScrollView scrollView);
    }
}