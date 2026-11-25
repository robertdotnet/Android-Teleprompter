#if ANDROID
using System.Text;
using Android.Bluetooth;
using Android.Bluetooth.LE;
using Android.Content;
using Android.OS;
using Android.Util;
using AndroidTeleprompterRemote.Models;

namespace AndroidTeleprompterRemote.Services;

public sealed class BluetoothLeClientService : IDisposable
{
    public static readonly Java.Util.UUID ServiceUuid =
        Java.Util.UUID.FromString("0000FFF0-0000-1000-8000-00805F9B34FB")!;
    public static readonly Java.Util.UUID CommandCharacteristicUuid =
        Java.Util.UUID.FromString("0000FFF1-0000-1000-8000-00805F9B34FB")!;
    public static readonly Java.Util.UUID StatusCharacteristicUuid =
        Java.Util.UUID.FromString("0000FFF2-0000-1000-8000-00805F9B34FB")!;
    private static readonly Java.Util.UUID ClientConfigurationUuid =
        Java.Util.UUID.FromString("00002902-0000-1000-8000-00805F9B34FB")!;

    private const string LogTag = "TeleprompterBLE";
    private static readonly TimeSpan ScanTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan ConnectionTimeout = TimeSpan.FromSeconds(15);
    private readonly object _sync = new();
    private BluetoothLeScanner? _scanner;
    private ClientScanCallback? _scanCallback;
    private BluetoothGatt? _gatt;
    private BluetoothGattCharacteristic? _commandCharacteristic;
    private TaskCompletionSource<BluetoothDevice>? _scanCompletion;
    private TaskCompletionSource? _connectionCompletion;
    private bool _isConnected;
    private bool _disposed;

    public bool IsConnected
    {
        get
        {
            lock (_sync)
            {
                return _isConnected;
            }
        }
    }

    public event Action? Connected;
    public event Action? Disconnected;

    public async Task StartScanAsync()
    {
        BluetoothAdapter? adapter;
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_isConnected || _scanCompletion is not null || _connectionCompletion is not null)
            {
                throw new InvalidOperationException("A BLE scan or connection is already in progress.");
            }

            var manager = (BluetoothManager?)Android.App.Application.Context
                .GetSystemService(Context.BluetoothService);
            adapter = manager?.Adapter;
            if (adapter is null || !adapter.IsEnabled)
            {
                throw new InvalidOperationException("Bluetooth is unavailable or turned off.");
            }

            _scanner = adapter.BluetoothLeScanner;
            if (_scanner is null)
            {
                throw new InvalidOperationException("BLE scanning is not supported on this device.");
            }

            _scanCompletion = new TaskCompletionSource<BluetoothDevice>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            _scanCallback = new ClientScanCallback(this);
        }

        var scanner = _scanner ?? throw new InvalidOperationException("The BLE scanner is unavailable.");
        var scanCallback = _scanCallback ?? throw new InvalidOperationException("The BLE scan callback is unavailable.");
        var filterBuilder = new ScanFilter.Builder();
        filterBuilder.SetServiceUuid(new ParcelUuid(ServiceUuid));
        var filters = new List<ScanFilter> { filterBuilder.Build()! };
        var settingsBuilder = new ScanSettings.Builder();
        settingsBuilder.SetScanMode(Android.Bluetooth.LE.ScanMode.LowLatency);
        var settings = settingsBuilder.Build()!;

        try
        {
            scanner.StartScan(filters, settings, scanCallback);
            var scanTask = _scanCompletion!.Task;
            var completedTask = await Task.WhenAny(scanTask, Task.Delay(ScanTimeout)).ConfigureAwait(false);
            if (completedTask != scanTask)
            {
                throw new TimeoutException("No compatible teleprompter was found during the BLE scan.");
            }

            var device = await scanTask.ConfigureAwait(false);
            StopScan();
            await ConnectAsync(device).ConfigureAwait(false);
        }
        catch
        {
            StopScan();
            ResetConnectionAttempt();
            throw;
        }
    }

    public Task SendAsync(RemoteMessage msg)
    {
        ArgumentNullException.ThrowIfNull(msg);
        BluetoothGatt gatt;
        BluetoothGattCharacteristic characteristic;
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (!_isConnected || _gatt is null || _commandCharacteristic is null)
            {
                throw new InvalidOperationException("The BLE peripheral is not connected.");
            }

            gatt = _gatt;
            characteristic = _commandCharacteristic;
        }

        var payload = RemoteMessageCodec.Encode(msg);
        if (OperatingSystem.IsAndroidVersionAtLeast(33))
        {
            var result = gatt.WriteCharacteristic(characteristic, payload, (int)GattWriteType.NoResponse);
            if (result != (int)CurrentBluetoothStatusCodes.Success)
            {
                throw new InvalidOperationException($"Unable to write the remote command: {result}.");
            }
        }
        else
        {
            characteristic.WriteType = GattWriteType.NoResponse;
            characteristic.SetValue(payload);
            if (!gatt.WriteCharacteristic(characteristic))
            {
                throw new InvalidOperationException("Unable to write the remote command to the BLE peripheral.");
            }
        }

        return Task.CompletedTask;
    }

    public Task DisconnectAsync()
    {
        BluetoothGatt? gatt;
        lock (_sync)
        {
            gatt = _gatt;
        }

        if (gatt is null)
        {
            StopScan();
            return Task.CompletedTask;
        }

        gatt.Disconnect();
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        BluetoothGatt? gatt;
        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            gatt = _gatt;
        }

        StopScan();
        gatt?.Close();
        ResetConnectionAttempt();
    }

    private void OnScanResult(ScanResult? result)
    {
        var device = result?.Device;
        if (device is null)
        {
            return;
        }

        lock (_sync)
        {
            _scanCompletion?.TrySetResult(device);
        }
    }

    private async Task ConnectAsync(BluetoothDevice device)
    {
        TaskCompletionSource completion;
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _connectionCompletion = completion;
        }

        var callback = new ClientGattCallback(this);
        var context = Android.App.Application.Context;
        var gatt = OperatingSystem.IsAndroidVersionAtLeast(23)
            ? device.ConnectGatt(context, false, callback, BluetoothTransports.Le)
            : device.ConnectGatt(context, false, callback);
        if (gatt is null)
        {
            ResetConnectionAttempt();
            throw new InvalidOperationException("Unable to start a GATT connection to the BLE peripheral.");
        }

        lock (_sync)
        {
            _gatt = gatt;
        }

        var completedTask = await Task.WhenAny(completion.Task, Task.Delay(ConnectionTimeout)).ConfigureAwait(false);
        if (completedTask != completion.Task)
        {
            gatt.Disconnect();
            gatt.Close();
            ResetConnectionAttempt();
            throw new TimeoutException("The BLE peripheral did not complete its GATT connection in time.");
        }

        await completion.Task.ConfigureAwait(false);
    }

    private void StopScan()
    {
        BluetoothLeScanner? scanner;
        ClientScanCallback? callback;
        lock (_sync)
        {
            scanner = _scanner;
            callback = _scanCallback;
            _scanner = null;
            _scanCallback = null;
            _scanCompletion = null;
        }

        if (scanner is not null && callback is not null)
        {
            scanner.StopScan(callback);
        }
    }

    private void ResetConnectionAttempt()
    {
        BluetoothGatt? gatt;
        lock (_sync)
        {
            gatt = _gatt;
            _gatt = null;
            _commandCharacteristic = null;
            _connectionCompletion = null;
            _isConnected = false;
        }

        gatt?.Close();
    }

    private void OnConnectionStateChanged(BluetoothGatt? gatt, GattStatus status, ProfileState newState)
    {
        if (gatt is null)
        {
            return;
        }

        if (newState == ProfileState.Connected && status == GattStatus.Success)
        {
            if (!gatt.DiscoverServices())
            {
                FailConnection(new InvalidOperationException("Unable to discover BLE peripheral services."));
            }

            return;
        }

        if (newState == ProfileState.Disconnected)
        {
            bool wasConnected;
            TaskCompletionSource? completion;
            lock (_sync)
            {
                wasConnected = _isConnected;
                _isConnected = false;
                _commandCharacteristic = null;
                completion = _connectionCompletion;
                _connectionCompletion = null;
                if (ReferenceEquals(_gatt, gatt))
                {
                    _gatt = null;
                }
            }

            if (!wasConnected)
            {
                completion?.TrySetException(new InvalidOperationException(
                    $"The BLE connection failed: {status}."));
            }
            else if (wasConnected)
            {
                Disconnected?.Invoke();
            }

            gatt.Close();
            return;
        }

        if (status != GattStatus.Success)
        {
            Log.Warn(LogTag, $"GATT connection state changed with status {status}.");
            FailConnection(new InvalidOperationException($"The BLE connection failed: {status}."));
        }
    }

    private void OnServicesDiscovered(BluetoothGatt? gatt, GattStatus status)
    {
        if (gatt is null || status != GattStatus.Success)
        {
            FailConnection(new InvalidOperationException($"Unable to discover BLE services: {status}."));
            return;
        }

        var service = gatt.GetService(ServiceUuid);
        var command = service?.GetCharacteristic(CommandCharacteristicUuid);
        var statusCharacteristic = service?.GetCharacteristic(StatusCharacteristicUuid);
        if (command is null || statusCharacteristic is null)
        {
            FailConnection(new InvalidOperationException("The BLE peripheral is missing a required characteristic."));
            return;
        }

        if (!gatt.SetCharacteristicNotification(statusCharacteristic, true))
        {
            FailConnection(new InvalidOperationException("Unable to subscribe to BLE connection status."));
            return;
        }

        var descriptor = statusCharacteristic.GetDescriptor(ClientConfigurationUuid);
        if (descriptor is null)
        {
            FailConnection(new InvalidOperationException("The BLE status characteristic has no notification descriptor."));
            return;
        }

        var enableNotifications = BluetoothGattDescriptor.EnableNotificationValue!.ToArray();
        if (OperatingSystem.IsAndroidVersionAtLeast(33))
        {
            var result = gatt.WriteDescriptor(descriptor, enableNotifications);
            if (result != (int)CurrentBluetoothStatusCodes.Success)
            {
                FailConnection(new InvalidOperationException($"Unable to enable BLE status notifications: {result}."));
                return;
            }
        }
        else
        {
            descriptor.SetValue(enableNotifications);
            if (!gatt.WriteDescriptor(descriptor))
            {
                FailConnection(new InvalidOperationException("Unable to enable BLE status notifications."));
                return;
            }
        }

        lock (_sync)
        {
            _commandCharacteristic = command;
        }
    }

    private void OnDescriptorWrite(BluetoothGatt? gatt, BluetoothGattDescriptor? descriptor, GattStatus status)
    {
        if (descriptor?.Uuid?.Equals(ClientConfigurationUuid) != true)
        {
            return;
        }

        if (status != GattStatus.Success)
        {
            FailConnection(new InvalidOperationException($"Unable to subscribe to BLE status notifications: {status}."));
            return;
        }

        TaskCompletionSource? completion;
        lock (_sync)
        {
            if (!ReferenceEquals(_gatt, gatt))
            {
                return;
            }

            _isConnected = true;
            completion = _connectionCompletion;
            _connectionCompletion = null;
        }

        completion?.TrySetResult();
        Connected?.Invoke();
    }

    private void OnStatusChanged(byte[]? value)
    {
        var status = value is { Length: > 0 } ? Encoding.UTF8.GetString(value) : string.Empty;
        if (string.Equals(status, "disconnected", StringComparison.OrdinalIgnoreCase))
        {
            bool wasConnected;
            lock (_sync)
            {
                wasConnected = _isConnected;
                _isConnected = false;
            }

            if (wasConnected)
            {
                Disconnected?.Invoke();
            }
        }
    }

    private void FailConnection(Exception exception)
    {
        TaskCompletionSource? completion;
        BluetoothGatt? gatt;
        lock (_sync)
        {
            completion = _connectionCompletion;
            _connectionCompletion = null;
            _isConnected = false;
            _commandCharacteristic = null;
            gatt = _gatt;
            _gatt = null;
        }

        completion?.TrySetException(exception);
        gatt?.Disconnect();
        gatt?.Close();
    }

    private sealed class ClientScanCallback(BluetoothLeClientService owner) : ScanCallback
    {
        public override void OnScanResult(ScanCallbackType callbackType, ScanResult? result) =>
            owner.OnScanResult(result);

        public override void OnScanFailed(ScanFailure errorCode)
        {
            owner._scanCompletion?.TrySetException(
                new InvalidOperationException($"BLE scan failed: {errorCode}."));
        }
    }

    private sealed class ClientGattCallback(BluetoothLeClientService owner) : BluetoothGattCallback
    {
        public override void OnConnectionStateChange(
            BluetoothGatt? gatt,
            GattStatus status,
            ProfileState newState) =>
            owner.OnConnectionStateChanged(gatt, status, newState);

        public override void OnServicesDiscovered(BluetoothGatt? gatt, GattStatus status) =>
            owner.OnServicesDiscovered(gatt, status);

        public override void OnDescriptorWrite(
            BluetoothGatt? gatt,
            BluetoothGattDescriptor? descriptor,
            GattStatus status) =>
            owner.OnDescriptorWrite(gatt, descriptor, status);

        public override void OnCharacteristicChanged(
            BluetoothGatt? gatt,
            BluetoothGattCharacteristic? characteristic)
        {
            if (characteristic?.Uuid?.Equals(StatusCharacteristicUuid) == true)
            {
                if (OperatingSystem.IsAndroidVersionAtLeast(33))
                {
                    return;
                }

                owner.OnStatusChanged(characteristic.GetValue()?.ToArray());
            }
        }

        public override void OnCharacteristicChanged(
            BluetoothGatt? gatt,
            BluetoothGattCharacteristic? characteristic,
            byte[]? value)
        {
            if (characteristic?.Uuid?.Equals(StatusCharacteristicUuid) == true)
            {
                owner.OnStatusChanged(value?.ToArray());
            }
        }
    }
}

public sealed class BluetoothLePermissions : Permissions.BasePlatformPermission
{
    public override (string androidPermission, bool isRuntime)[] RequiredPermissions
    {
        get
        {
            if (OperatingSystem.IsAndroidVersionAtLeast(31))
            {
                return
                [
                    ("android.permission.BLUETOOTH_SCAN", true),
                    ("android.permission.BLUETOOTH_CONNECT", true)
                ];
            }

            return [("android.permission.ACCESS_FINE_LOCATION", true)];
        }
    }
}
#else
namespace AndroidTeleprompterRemote.Services;

public sealed class BluetoothLeClientService : IDisposable
{
    public bool IsConnected => false;

    public event Action? Connected;
    public event Action? Disconnected;

    public Task StartScanAsync() =>
        Task.FromException(new PlatformNotSupportedException(
            "Bluetooth Low Energy scanning is only supported on Android."));

    public Task SendAsync(AndroidTeleprompterRemote.Models.RemoteMessage msg) =>
        Task.FromException(new PlatformNotSupportedException(
            "Bluetooth Low Energy commands are only supported on Android."));

    public Task DisconnectAsync() => Task.CompletedTask;

    public void Dispose()
    {
    }
}
#endif
