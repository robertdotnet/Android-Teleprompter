using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace Android_Teleprompter.Services
{
    public class ScrollingService : IScrollingService
    {
        public async Task AutoScrollAsync(CancellationToken cancellationToken, double speed, double startY, double maxY, Microsoft.Maui.Controls.ScrollView scrollView)
        {
            var stopwatch = Stopwatch.StartNew();

            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var y = Math.Min(startY + stopwatch.Elapsed.TotalSeconds * speed, maxY);

                await scrollView.ScrollToAsync(0, y, animated: false);

                if (y >= maxY)
                {
                    break;
                }

                await Task.Delay(16, cancellationToken);
            }
        }

        public async Task ScrollUpAsync(Microsoft.Maui.Controls.ScrollView scrollView, double dpAmount)
        {
            if (scrollView == null) return;
            
            var newY = Math.Max(0, scrollView.ScrollY - dpAmount);
            await scrollView.ScrollToAsync(0, newY, animated: false);
        }

        public async Task ScrollDownAsync(Microsoft.Maui.Controls.ScrollView scrollView, double dpAmount)
        {
            if (scrollView == null) return;

            var maxY = Math.Max(0, scrollView.ContentSize.Height - scrollView.Height);
            var newY = Math.Min(maxY, scrollView.ScrollY + dpAmount);
            await scrollView.ScrollToAsync(0, newY, animated: false);
        }

        public async Task ResetScrollAsync(Microsoft.Maui.Controls.ScrollView scrollView)
        {
            if (scrollView == null) return;

            await scrollView.ScrollToAsync(0, 0, animated: false);
        }

    }
}