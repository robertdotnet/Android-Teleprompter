using System.Threading.Tasks;
using Android_Teleprompter.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Maui.Controls;

namespace Android_Teleprompter.ViewModels
{
    public partial class MainViewModel : ObservableObject
    {
        [ObservableProperty]
        public partial string LongText { get; set; } = "Lorem ipsum dolor sit amet, consectetur adipiscing elit. Sed do eiusmod tempor incididunt ut labore et dolore magna aliqua. Ut enim ad minim veniam, quis nostrud exercitation ullamco laboris nisi ut aliquip ex ea commodo consequat. Duis aute irure dolor in reprehenderit in voluptate velit esse cillum dolore eu fugiat nulla pariatur. Excepteur sint occaecat cupidatat non proident, sunt in culpa qui officia deserunt mollit anim id est laborum.";

        [ObservableProperty]
        public partial string AutoScrollButtonText { get; set; } = "Start Scrolling";

        [ObservableProperty]
        public partial bool IsAutoScrolling { get; set; }

        [ObservableProperty]
        public partial bool IsRemoteConnected { get; set; }

        [ObservableProperty]
        public partial double ScrollSpeed { get; set; } = 50;

        private CancellationTokenSource? _autoScrollCancellation;
        private readonly IFileService _fileService;
        private readonly IScrollingService _scrollingService;

        public MainViewModel(IFileService fileService, IScrollingService scrollingService)
        {
            _fileService = fileService;
            _scrollingService = scrollingService;
        }

        [RelayCommand]
        private async Task OpenFileAsync()
        {
            var content = await _fileService.PickAndReadFileAsync();
            if (content != null)
            {
                LongText = content;
            }
        }

        public async Task PlayAsync(ScrollView scrollView)
        {
            if (scrollView == null) return;

            var previousCancellation = _autoScrollCancellation;
            _autoScrollCancellation = null;
            previousCancellation?.Cancel();
            var cancellation = new CancellationTokenSource();
            _autoScrollCancellation = cancellation;
            IsAutoScrolling = true;
            AutoScrollButtonText = "Stop Scrolling";

            await RunAutoScrollAsync(cancellation, scrollView);
        }

        public async Task PauseAsync()
        {
            var cancellation = _autoScrollCancellation;
            _autoScrollCancellation = null;
            cancellation?.Cancel();
            IsAutoScrolling = false;
            AutoScrollButtonText = "Start Scrolling";
            await Task.CompletedTask;
        }

        public async Task RestartAsync(ScrollView scrollView)
        {
            if (scrollView == null) return;

            var cancellation = _autoScrollCancellation;
            _autoScrollCancellation = null;
            cancellation?.Cancel();
            await _scrollingService.ResetScrollAsync(scrollView);
            await PlayAsync(scrollView);
        }

        public async Task ScrollUpAsync(ScrollView scrollView)
        {
            if (scrollView == null) return;
            
            await _scrollingService.ScrollUpAsync(scrollView, 100);
        }

        public async Task ScrollDownAsync(ScrollView scrollView)
        {
            if (scrollView == null) return;
            
            await _scrollingService.ScrollDownAsync(scrollView, 100);
        }

        public void SetSpeed(double dpPerSecond)
        {
            ScrollSpeed = dpPerSecond;
        }

        [RelayCommand(AllowConcurrentExecutions = true)]
        private async Task ToggleAutoScrollAsync(ScrollView scrollView)
        {
            ArgumentNullException.ThrowIfNull(scrollView);

            if (_autoScrollCancellation is not null)
            {
                var cancellation = _autoScrollCancellation;
                _autoScrollCancellation = null;
                cancellation.Cancel();
                return;
            }

            await PlayAsync(scrollView);
        }

        private async Task RunAutoScrollAsync(CancellationTokenSource cancellation, ScrollView scrollView)
        {
            try
            {
                var maxY = Math.Max(0, scrollView.ContentSize.Height - scrollView.Height);
                await _scrollingService.AutoScrollAsync(
                    cancellation.Token,
                    speed: ScrollSpeed,
                    startY: scrollView.ScrollY,
                    maxY,
                    scrollView);
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
            {
            }
            finally
            {
                if (ReferenceEquals(_autoScrollCancellation, cancellation))
                {
                    _autoScrollCancellation = null;
                    AutoScrollButtonText = "Start Scrolling";
                    IsAutoScrolling = false;
                }

                cancellation.Dispose();
            }
        }
    }
}