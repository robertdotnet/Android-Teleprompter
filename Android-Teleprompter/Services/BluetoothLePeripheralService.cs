#if ANDROID
using System.Collections.Concurrent;
using System.Text;
using Android.Bluetooth;
using Android.Bluetooth.LE;
using Android.Content;
using Android.OS;
using Android.Util;
using Microsoft.Maui.ApplicationModel;

namespace AndroidTeleprompter.Services;

public sealed class BluetoothLePeripheralService : IDisposable
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
    private readonly object _sync = new();
    private readonly ConcurrentDictionary<string, BluetoothDevice> _connectedClients = new();
    private readonly ConcurrentDictionary<string, BluetoothDevice> _subscribedClients = new();
    private BluetoothGattServer? _gattServer;
    private BluetoothGattCharacteristic? _statusCharacteristic;
    private BluetoothAdapter? _adapter;
    private BluetoothLeAdvertiser? _advertiser;
    private AdvertiseCallback? _advertiseCallback;
    private bool _isAdvertising;
    private bool _disposed;

    public bool IsAdvertising
    {
        get
        {
            lock (_sync)
            {
                return _isAdvertising;
            }
        }
    }

    public event Action? ClientConnected;
    public event Action? ClientDisconnected;
    public event Action<byte[]>? CommandReceived;

    public void StartAdvertising()
    {
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_gattServer is not null)
            {
                return;
            }

            var manager = (BluetoothManager?)Android.App.Application.Context
                .GetSystemService(Context.BluetoothService);
            _adapter = manager?.Adapter;
            if (_adapter is null || !_adapter.IsEnabled)
            {
                throw new InvalidOperationException("Bluetooth is unavailable or turned off.");
            }

            var gattServerCallback = new PeripheralGattServerCallback(this);
            _gattServer = manager!.OpenGattServer(Android.App.Application.Context, gattServerCallback);
            if (_gattServer is null)
            {
                throw new InvalidOperationException("Unable to open the Bluetooth GATT server.");
            }

            var service = new BluetoothGattService(ServiceUuid, GattServiceType.Primary);
            var commandCharacteristic = new BluetoothGattCharacteristic(
                CommandCharacteristicUuid,
                GattProperty.WriteNoResponse,
                GattPermission.Write);
            _statusCharacteristic = new BluetoothGattCharacteristic(
                StatusCharacteristicUuid,
                GattProperty.Notify,
                GattPermission.Read);
            _statusCharacteristic.AddDescriptor(new BluetoothGattDescriptor(
                ClientConfigurationUuid,
                GattDescriptorPermission.Read | GattDescriptorPermission.Write));
            service.AddCharacteristic(commandCharacteristic);
            service.AddCharacteristic(_statusCharacteristic);

            if (!_gattServer.AddService(service))
            {
                _gattServer.Close();
                _gattServer = null;
                _statusCharacteristic = null;
                throw new InvalidOperationException("Unable to add the teleprompter GATT service.");
            }
        }
    }

    public void StopAdvertising()
    {
        BluetoothGattServer? server;
        BluetoothLeAdvertiser? advertiser;
        AdvertiseCallback? callback;
        lock (_sync)
        {
            server = _gattServer;
            advertiser = _advertiser;
            callback = _advertiseCallback;
            _gattServer = null;
            _statusCharacteristic = null;
            _advertiser = null;
            _advertiseCallback = null;
            _isAdvertising = false;
        }

        if (advertiser is not null && callback is not null)
        {
            advertiser.StopAdvertising(callback);
        }

        server?.Close();
        _connectedClients.Clear();
        _subscribedClients.Clear();
    }

    public void NotifyStatus(string status)
    {
        ArgumentNullException.ThrowIfNull(status);

        BluetoothGattServer? server;
        BluetoothGattCharacteristic? characteristic;
        lock (_sync)
        {
            server = _gattServer;
            characteristic = _statusCharacteristic;
        }

        if (server is null || characteristic is null)
        {
            return;
        }

        var value = Encoding.UTF8.GetBytes(status);
        foreach (var client in _subscribedClients.Values)
        {
            if (OperatingSystem.IsAndroidVersionAtLeast(33))
            {
                var result = server.NotifyCharacteristicChanged(client, characteristic, false, value);
                if (result != (int)CurrentBluetoothStatusCodes.Success)
                {
                    Log.Warn(LogTag, $"Unable to notify {client.Address}: {result}");
                }
            }
            else
            {
                characteristic.SetValue(value);
                if (!server.NotifyCharacteristicChanged(client, characteristic, false))
                {
                    Log.Warn(LogTag, $"Unable to notify {client.Address}.");
                }
            }
        }
    }

    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
        }

        StopAdvertising();
    }

    private void OnServiceAdded(GattStatus status)
    {
        if (status != GattStatus.Success)
        {
            Log.Error(LogTag, $"Unable to add GATT service: {status}");
            StopAdvertising();
            return;
        }

        lock (_sync)
        {
            if (_gattServer is null || _adapter is null)
            {
                return;
            }

            var advertiser = _adapter.BluetoothLeAdvertiser;
            if (advertiser is null)
            {
                Log.Error(LogTag, "BLE advertising is not supported on this device.");
                StopAdvertising();
                return;
            }
            _advertiser = advertiser;

            var callback = new PeripheralAdvertiseCallback(this);
            _advertiseCallback = callback;
            var settingsBuilder = new AdvertiseSettings.Builder();
            settingsBuilder.SetAdvertiseMode(AdvertiseMode.LowLatency);
            settingsBuilder.SetConnectable(true);
            settingsBuilder.SetTimeout(0);
            settingsBuilder.SetTxPowerLevel(AdvertiseTx.PowerMedium);
            var settings = settingsBuilder.Build();

            var dataBuilder = new AdvertiseData.Builder();
            dataBuilder.AddServiceUuid(new ParcelUuid(ServiceUuid));
            var data = dataBuilder.Build();
            advertiser.StartAdvertising(settings, data, callback);
        }
    }

    private void OnAdvertisingStarted()
    {
        lock (_sync)
        {
            _isAdvertising = _gattServer is not null && _advertiseCallback is not null;
        }
    }

    private void OnAdvertisingFailed(AdvertiseFailure errorCode)
    {
        lock (_sync)
        {
            _isAdvertising = false;
        }

        Log.Error(LogTag, $"BLE advertising failed: {errorCode}");
        StopAdvertising();
    }

    private void OnConnectionStateChanged(BluetoothDevice device, ProfileState status, ProfileState newState)
    {
        if ((int)status != (int)GattStatus.Success)
        {
            Log.Warn(LogTag, $"GATT connection state changed with status {status}.");
        }

        if (newState == ProfileState.Connected)
        {
            _connectedClients[device.Address ?? string.Empty] = device;
            ClientConnected?.Invoke();
        }
        else if (newState == ProfileState.Disconnected)
        {
            var key = device.Address ?? string.Empty;
            _connectedClients.TryRemove(key, out _);
            _subscribedClients.TryRemove(key, out _);
            ClientDisconnected?.Invoke();
        }
    }

    private void OnCommandWritten(byte[]? value)
    {
        if (value is { Length: > 0 })
        {
            CommandReceived?.Invoke((byte[])value.Clone());
        }
    }

    private void OnStatusSubscriptionChanged(
        BluetoothDevice device,
        BluetoothGattDescriptor descriptor,
        byte[]? value)
    {
        if (descriptor.Uuid?.Equals(ClientConfigurationUuid) != true)
        {
            return;
        }

        if (value is not null && !OperatingSystem.IsAndroidVersionAtLeast(33))
        {
            descriptor.SetValue(value);
        }

        var key = device.Address ?? string.Empty;
        if (value is not null && value.SequenceEqual(BluetoothGattDescriptor.EnableNotificationValue!))
        {
            _subscribedClients[key] = device;
            if (_connectedClients.ContainsKey(key))
            {
                NotifyStatus("connected");
            }
        }
        else
        {
            _subscribedClients.TryRemove(key, out _);
        }
    }

    private sealed class PeripheralGattServerCallback(BluetoothLePeripheralService owner) : BluetoothGattServerCallback
    {
        public override void OnServiceAdded(GattStatus status, BluetoothGattService? service) =>
            owner.OnServiceAdded(status);

        public override void OnConnectionStateChange(BluetoothDevice? device, ProfileState status, ProfileState newState)
        {
            if (device is not null)
            {
                owner.OnConnectionStateChanged(device, status, newState);
            }
        }

        public override void OnCharacteristicWriteRequest(
            BluetoothDevice? device,
            int requestId,
            BluetoothGattCharacteristic? characteristic,
            bool preparedWrite,
            bool responseNeeded,
            int offset,
            byte[]? value)
        {
            if (device is null || characteristic?.Uuid != CommandCharacteristicUuid)
            {
                return;
            }

            owner.OnCommandWritten(value);
            if (responseNeeded)
            {
                lock (owner._sync)
                {
                    owner._gattServer?.SendResponse(device, requestId, GattStatus.Success, offset, value ?? []);
                }
            }
        }

        public override void OnDescriptorWriteRequest(
            BluetoothDevice? device,
            int requestId,
            BluetoothGattDescriptor? descriptor,
            bool preparedWrite,
            bool responseNeeded,
            int offset,
            byte[]? value)
        {
            if (device is null || descriptor is null)
            {
                return;
            }

            owner.OnStatusSubscriptionChanged(device, descriptor, value);
            if (responseNeeded)
            {
                lock (owner._sync)
                {
                    owner._gattServer?.SendResponse(device, requestId, GattStatus.Success, offset, value ?? []);
                }
            }
        }
    }

    private sealed class PeripheralAdvertiseCallback(BluetoothLePeripheralService owner) : AdvertiseCallback
    {
        public override void OnStartSuccess(AdvertiseSettings? settings) => owner.OnAdvertisingStarted();

        public override void OnStartFailure(AdvertiseFailure errorCode) => owner.OnAdvertisingFailed(errorCode);
    }
}

public sealed class BluetoothLePermissions : Permissions.BasePlatformPermission
{
    public override (string androidPermission, bool isRuntime)[] RequiredPermissions
    {
        get
        {
            if (!OperatingSystem.IsAndroidVersionAtLeast(31))
            {
                return [];
            }

            return
            [
                ("android.permission.BLUETOOTH_SCAN", true),
                ("android.permission.BLUETOOTH_ADVERTISE", true),
                ("android.permission.BLUETOOTH_CONNECT", true)
            ];
        }
    }
}
#endif
