using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using InTheHand.Net;
using InTheHand.Net.Bluetooth;
using InTheHand.Net.Sockets;
using Newtonsoft.Json;

namespace TeBot
{
    public static class HexUtils
    {
        /// <summary>
        /// Converts a hex string (e.g. "AABBCC") to a byte array.
        /// </summary>
        public static byte[] HexStringToBytes(string hex)
        {
            if (string.IsNullOrWhiteSpace(hex))
                return Array.Empty<byte>();
            int len = hex.Length / 2;
            byte[] packet = new byte[len];
            for (int i = 0; i < len; i++)
                packet[i] = Convert.ToByte(hex.Substring(i * 2, 2), 16);
            return packet;
        }

        /// <summary>
        /// Converts a byte array to a hex string (e.g. {0xAA,0xBB} => "AABB").
        /// </summary>
        public static string BytesToHexString(byte[] data)
        {
            if (data == null || data.Length == 0) return string.Empty;
            return BitConverter.ToString(data).Replace("-", "");
        }
    }
    public class BluetoothManager
    {
        private BluetoothClient _bluetoothClient;
        private BluetoothDeviceInfo _connectedDevice;
        private Stream _bluetoothStream;
        private bool _isConnected;
        private System.Timers.Timer _flushTimer;

        // Queue for communication
        private readonly ConcurrentQueue<byte[]> _sendQueue = new ConcurrentQueue<byte[]>();
        private readonly ConcurrentQueue<byte[]> _receiveQueue = new ConcurrentQueue<byte[]>();

        // Async task management
        private CancellationTokenSource _globalCancellation;
        // Tasks for TeBotRobot communication
        private Task _tebotPollingTask;   // Sends poll commands to TeBotRobot
        private Task _tebotReceiverTask;  // Reads responses from TeBotRobot

        // Synchronization for queues
        private readonly SemaphoreSlim _sendSemaphore = new SemaphoreSlim(0);
        private readonly SemaphoreSlim _receiveSemaphore = new SemaphoreSlim(0);

        // State tracking - for status reporting
        private bool _isPolling = false;
        private bool _isReceiving = false;

        // TeBotRobot polling constants
        private const int TEBOT_POLL_INTERVAL_MS = 100; // Poll every 100ms
        private const byte TEBOT_POLL_COMMAND = 0x0A;   // Poll command byte        
        // Command constants for robot control
        private const byte CMD_MOVE_FORWARD = 0x01;
        private const byte CMD_MOVE_BACKWARD = 0x02;
        private const byte CMD_TURN_LEFT = 0x03;
        private const byte CMD_TURN_RIGHT = 0x04;
        private const byte CMD_STOP = 0x05;
        private const byte CMD_SET_SPEED = 0x06;
        private const byte CMD_SET_LED = 0x07;
        private const byte CMD_PLAY_TONE = 0x08;
        private const byte CMD_SET_NEOMATRIX = 0x09;

        // NEW: Track Scratch connection state to prevent sending when disconnected
        private bool _isScratchConnected = false;

        // CRITICAL: Always preserve most recent robot data stream for safety
        private byte[] _latestRobotData = null;
        private DateTime _lastRobotDataTime = DateTime.MinValue;
        private readonly object _robotDataLock = new object();

        // NEW: JSON-RPC status cache (for fast status replies)
        private string _latestStatusJson = null;
        private readonly object _statusJsonLock = new object();

        // Constants optimized for 115200 baud HC-05
        private const int DATA_PACKET_SIZE = 16;
        private const int CONNECTION_TIMEOUT_MS = 10000;
        private const int STREAM_TIMEOUT_MS = 5000;

        // Bluetooth adapter management
        private BluetoothRadio[] _availableRadios;
        private BluetoothRadio _selectedRadio;

        public event Action<string> StatusChanged;
        public event Action<BluetoothDeviceInfo[]> DevicesDiscovered;
        public event Action<byte[]> DataReceived;
        // Removed unused event: QueueStatus
        public event Action<BluetoothRadio[]> BluetoothAdaptersDiscovered;
        // NEW: Event to push JSON-RPC status to Scratch on every sensor update
        public event Action<string> StatusJsonPushed;

        public bool IsConnected => _isConnected;
        public string ConnectedDeviceName => _connectedDevice?.DeviceName ?? "None";
        public int QueuedDataCount => _sendQueue.Count;
        public bool IsPolling => _isPolling;
        public bool IsReceiving => _isReceiving;

        public BluetoothRadio[] AvailableBluetoothAdapters => _availableRadios ?? new BluetoothRadio[0];
        public BluetoothRadio SelectedBluetoothAdapter => _selectedRadio;
        public string SelectedAdapterInfo => _selectedRadio != null ?
            $"{_selectedRadio.Name} ({_selectedRadio.LocalAddress})" : "None";

        public BluetoothManager()
        {
            // Initialize and detect Bluetooth adapters first
            DetectBluetoothAdapters();

            _bluetoothClient = new BluetoothClient();

            // Setup periodic flush timer for even lower latency
            _flushTimer = new System.Timers.Timer(25); // Flush every 25ms
            _flushTimer.Elapsed += FlushTimer_Elapsed;
        }

        private async void FlushTimer_Elapsed(object sender, System.Timers.ElapsedEventArgs e)
        {
            try
            {
                if (_isConnected && _bluetoothStream != null && _bluetoothStream.CanWrite)
                {
                    await _bluetoothStream.FlushAsync();
                }
            }
            catch
            {
                // Ignore flush errors
            }
        }

        public async Task<BluetoothDeviceInfo[]> ScanForDevicesAsync()
        {
            try
            {
                // Ensure we have a selected adapter
                if (_selectedRadio == null)
                {
                    DetectBluetoothAdapters();
                    PreferExternalDongle();
                }

                var adapterInfo = _selectedRadio != null ?
                    $" using {_selectedRadio.Name}" : " using default adapter";

                StatusChanged?.Invoke($"Scanning for Bluetooth devices{adapterInfo}...");

                var allDevices = new List<BluetoothDeviceInfo>();

                var scanTask = Task.Run(() =>
                {
                    try
                    {
                        // First, get already paired devices
                        var pairedDevices = _bluetoothClient.DiscoverDevices(10, true, false, false);
                        StatusChanged?.Invoke($"Found {pairedDevices.Length} already paired devices");
                        allDevices.AddRange(pairedDevices);

                        // Then scan for discoverable devices (not necessarily paired)
                        var discoverableDevices = _bluetoothClient.DiscoverDevices(15, false, true, false);
                        StatusChanged?.Invoke($"Found {discoverableDevices.Length} discoverable devices");

                        // Add discoverable devices that aren't already in our paired list
                        foreach (var device in discoverableDevices)
                        {
                            if (!allDevices.Any(d => d.DeviceAddress.Equals(device.DeviceAddress)))
                            {
                                allDevices.Add(device);
                            }
                        }

                        return allDevices.ToArray();
                    }
                    catch (Exception ex)
                    {
                        StatusChanged?.Invoke($"Error during device discovery: {ex.Message}");
                        return new BluetoothDeviceInfo[0];
                    }
                });

                var devices = await scanTask;

                StatusChanged?.Invoke($"Found {devices.Length} total Bluetooth devices{adapterInfo} ({allDevices.Count(d => d.Authenticated)} paired, {allDevices.Count(d => !d.Authenticated)} unpaired)");
                DevicesDiscovered?.Invoke(devices);
                return devices;
            }
            catch (Exception ex)
            {
                StatusChanged?.Invoke($"Error scanning for devices: {ex.Message}");
                return new BluetoothDeviceInfo[0];
            }
        }

        public async Task<bool> ConnectToDeviceAsync(BluetoothDeviceInfo device)
        {
            int maxRetries = 3;
            for (int attempt = 1; attempt <= maxRetries; attempt++)
            {
                try
                {
                    if (_isConnected)
                        await DisconnectAsync();

                    // Make sure we have a clean client before connecting
                    _bluetoothClient?.Dispose();
                    _bluetoothClient = new BluetoothClient();

                    StatusChanged?.Invoke($"Connecting to {device.DeviceName} (attempt {attempt})...");

                    var serviceUuids = new[]
                    {
                        BluetoothService.SerialPort,
                        new Guid("0000ffe0-0000-1000-8000-00805f9b34fb"),
                        new Guid("6e400001-b5a3-f393-e0a9-e50e24dcca9e")
                    };

                    Exception lastException = null;

                    foreach (var serviceUuid in serviceUuids)
                    {
                        try
                        {
                            var endpoint = new BluetoothEndPoint(device.DeviceAddress, serviceUuid);
                            var connectTask = Task.Run(() => _bluetoothClient.Connect(endpoint));
                            var timeoutTask = Task.Delay(CONNECTION_TIMEOUT_MS);
                            var completedTask = await Task.WhenAny(connectTask, timeoutTask);

                            if (completedTask == timeoutTask)
                                throw new TimeoutException($"Connection timeout after {CONNECTION_TIMEOUT_MS / 1000} seconds");

                            if (connectTask.IsFaulted)
                                throw connectTask.Exception?.GetBaseException() ?? new Exception("Connection failed");

                            _bluetoothStream = _bluetoothClient.GetStream();

                            if (_bluetoothStream.CanTimeout)
                            {
                                _bluetoothStream.WriteTimeout = STREAM_TIMEOUT_MS;
                                _bluetoothStream.ReadTimeout = STREAM_TIMEOUT_MS;
                            }

                            _connectedDevice = device;
                            _isConnected = true;

                            StatusChanged?.Invoke($"Connected to {device.DeviceName} (optimized for 115200 baud)");
                            Debug.WriteLine($"Successfully connected using service UUID: {serviceUuid}");

                            StartOptimizedAsyncHandlers();
                            return true;
                        }
                        catch (Exception ex)
                        {
                            lastException = ex;
                            Debug.WriteLine($"Failed to connect using service UUID {serviceUuid}: {ex.Message}");
                            continue;
                        }
                    }

                    StatusChanged?.Invoke($"Failed to connect to {device.DeviceName}: {lastException?.Message}");
                }
                catch (Exception ex)
                {
                    StatusChanged?.Invoke($"Error connecting to device: {ex.Message}");
                }

                // Wait a bit before retrying
                await Task.Delay(1000);
            }

            StatusChanged?.Invoke($"All connection attempts failed for {device.DeviceName}.");
            return false;
        }

        /// <summary>
        /// Start optimized async handlers - separate threads for send/receive
        /// </summary>
        private void StartOptimizedAsyncHandlers()
        {
            // Cancel any existing operations
            StopOptimizedAsyncHandlers();

            _globalCancellation = new CancellationTokenSource();
            var token = _globalCancellation.Token;

            StatusChanged?.Invoke("🚀 Starting TeBotRobot communication threads...");

            // CRITICAL DEBUG: Verify stream state before starting handlers
            var streamInfo = _bluetoothStream != null ?
                $"Stream exists: CanRead={_bluetoothStream.CanRead}, CanWrite={_bluetoothStream.CanWrite}" :
                "Stream is NULL!";
            StatusChanged?.Invoke($"🔍 STREAM STATE: {streamInfo}");

            var clientInfo = _bluetoothClient != null ?
                $"Client exists: Connected={_bluetoothClient.Connected}" :
                "Client is NULL!";
            StatusChanged?.Invoke($"🔍 CLIENT STATE: {clientInfo}");

            // HANDLER 1: TeBotRobot poller (periodically sends poll commands)
            // This handler ONLY SENDS poll commands to TeBotRobot every 100ms
            _tebotPollingTask = Task.Run(async () => await TeBotPollingHandler(token), token);
            StatusChanged?.Invoke("📊 TEBOT POLLER TASK CREATED (sends poll requests)");

            // HANDLER 2: TeBotRobot receiver (continuously reads from Bluetooth)
            // This handler ONLY READS data from TeBotRobot and converts it to JSON-RPC
            _tebotReceiverTask = Task.Run(async () => await TeBotReceiverHandler(token), token);
            StatusChanged?.Invoke("📡 TEBOT RECEIVER TASK CREATED (reads responses)");

            StatusChanged?.Invoke("✅ TeBotRobot communication active (2 threads)");
        }

        /// <summary>
        /// Stop TeBotRobot communication handlers
        /// </summary>
        private async void StopOptimizedAsyncHandlers()
        {
            try
            {
                StatusChanged?.Invoke("🛑 Stopping TeBotRobot communication handlers...");

                _globalCancellation?.Cancel();

                var tasks = new[] { _tebotPollingTask, _tebotReceiverTask }
                    .Where(t => t != null).ToArray();

                if (tasks.Length > 0)
                {
                    try
                    {
                        await Task.WhenAny(Task.WhenAll(tasks), Task.Delay(2000));
                    }
                    catch { /* Ignore exceptions from cancelled tasks */ }
                }

                _globalCancellation?.Dispose();
                _globalCancellation = null;

                _isPolling = false;
                _isReceiving = false;

                StatusChanged?.Invoke("✅ TeBotRobot communication handlers stopped");
            }
            catch (Exception ex)
            {
                StatusChanged?.Invoke($"❌ Error stopping communication handlers: {ex.Message}");
            }
        }

        // Handler methods removed - only TeBotPollingHandler remains

        /// <summary>
        /// Build and cache the latest status JSON for JSON-RPC replies
        /// Follows the TeBot protocol table for sensor data formatting
        /// </summary>
        private void UpdateStatusJson()
        {
            lock (_statusJsonLock)
            {
                var data = GetLatestSensorData();
                var age = GetSensorDataAgeMs();

                // Create a dictionary for the parsed sensor values
                var sensorValues = new Dictionary<string, object>();

                if (data != null && data.Length >= 16)
                {
                    // First validate that it's a proper packet with 0x00 as the first byte
                    // This ensures we only process valid packets in UpdateStatusJson
                    bool isValidPacket = data.Length == 16 && data[0] == 0x00;
                    sensorValues["isValid"] = isValidPacket;

                    // Parse the 16-byte structure from TeBotRobot according to protocol
                    // Byte 0: Status code
                    sensorValues["statusCode"] = data[0];

                    // Byte 1: Direction
                    byte directionByte = data[1];
                    string direction;
                    switch (directionByte)
                    {
                        case 0: direction = "stop"; break;
                        case 1: direction = "forward"; break;
                        case 2: direction = "backward"; break;
                        case 3: direction = "left"; break;
                        case 4: direction = "right"; break;
                        default: direction = "unknown"; break;
                    }
                    sensorValues["direction"] = direction;

                    // Byte 2: Speed (0-100)
                    sensorValues["speed"] = data[2];

                    // Byte 3: Battery level (0-100)
                    sensorValues["batteryLevel"] = data[3];

                    // Bytes 4-5: Ultrasonic distance in cm (16-bit)
                    // Low byte first, high byte second (little endian)
                    int ultrasonicDistance = data[4] | (data[5] << 8);
                    sensorValues["ultrasonicDistance"] = ultrasonicDistance;

                    // Bytes 6-7: IR sensor value (16-bit)
                    int irValue = data[6] | (data[7] << 8);
                    sensorValues["irValue"] = irValue;

                    // Bytes 8-9: Light sensor value (16-bit)
                    int lightValue = data[8] | (data[9] << 8);
                    sensorValues["lightValue"] = lightValue;

                    // Byte 10: Button states (bits)
                    bool buttonA = (data[10] & 0x01) > 0;
                    bool buttonB = (data[10] & 0x02) > 0;
                    sensorValues["buttonA"] = buttonA;
                    sensorValues["buttonB"] = buttonB;

                    // Byte 11: LED state
                    sensorValues["ledOn"] = data[11] > 0;

                    // Byte 12: Movement status
                    sensorValues["isMoving"] = data[12] > 0;

                    // Bytes 13-14: Command counter (16-bit)
                    int commandCount = data[13] | (data[14] << 8);
                    sensorValues["commandCount"] = commandCount;

                    // Byte 15: Checksum (XOR of all preceding bytes)
                    byte calculatedChecksum = 0;
                    for (int i = 0; i < 15; i++)
                    {
                        calculatedChecksum ^= data[i];
                    }
                    sensorValues["checksumValid"] = (calculatedChecksum == data[15]);
                }

                // Create JSON-RPC 2.0 response object according to protocol
                var statusObj = new Dictionary<string, object>
                {
                    ["jsonrpc"] = "2.0",
                    ["result"] = new Dictionary<string, object>
                    {
                        // Include raw hex data for debugging/transparency
                        ["raw"] = data != null ? HexUtils.BytesToHexString(data) : null,
                        // Include parsed sensor values in their proper types
                        ["sensors"] = sensorValues,
                        // Include data age for freshness indication
                        ["ageMs"] = age
                    }
                    // id is omitted here; will be added in GetStatusJson if needed
                };
                _latestStatusJson = JsonConvert.SerializeObject(statusObj);
            }
        }

        /// <summary>
        /// Get the latest status JSON for JSON-RPC replies (optionally with id)
        /// </summary>
        public string GetStatusJson(string id = null)
        {
            lock (_statusJsonLock)
            {
                if (_latestStatusJson == null)
                {
                    UpdateStatusJson();
                }
                if (id == null)
                {
                    return _latestStatusJson;
                }
                // Insert the id into the cached JSON (using Newtonsoft.Json)
                try
                {
                    var baseObj = JsonConvert.DeserializeObject<System.Collections.Generic.Dictionary<string, object>>(_latestStatusJson);
                    baseObj["id"] = id;
                    return JsonConvert.SerializeObject(baseObj);
                }
                catch
                {
                    // Fallback: just return the cached JSON
                    return _latestStatusJson;
                }
            }
        }

        // Sender handler removed - TeBotPollingHandler now handles all communication

        /// <summary>
        /// Send data immediately - optimized bridge from Scratch to robot
        /// Accepts either a byte array or a hex string (for minimal data traffic)
        /// POLICY: Only send commands when Scratch is connected
        /// </summary>
        public Task<bool> SendDataImmediately(string hex)
        {
            if (string.IsNullOrWhiteSpace(hex))
                return Task.FromResult(false);

            // Convert hex string to byte array
            byte[] data = null;
            try
            {
                data = HexUtils.HexStringToBytes(hex);
            }
            catch (Exception ex)
            {
                StatusChanged?.Invoke($"❌ Invalid hex string: {ex.Message}");
                return Task.FromResult(false);
            }

            // Call the byte array version
            return SendDataImmediately(data);
        }

        /// <summary>
        /// Overload: Send data immediately using a byte array (for compatibility)
        /// </summary>
        public Task<bool> SendDataImmediately(byte[] data)
        {
            if (data == null || data.Length == 0)
            {
                return Task.FromResult(false);
            }

            if (!_isConnected)
            {
                StatusChanged?.Invoke("❌ Not connected to any Bluetooth device");
                return Task.FromResult(false);
            }

            // Only send commands if Scratch is connected
            if (!_isScratchConnected)
            {
                StatusChanged?.Invoke("❌ Scratch is disconnected, command ignored for safety");
                return Task.FromResult(false);
            }

            try
            {
                // Convert to hex string for logging
                var hex = HexUtils.BytesToHexString(data);
                
                // Send the command packet to the robot using the thread-safe method
                SendCommandPacketSafe(data);
                
                StatusChanged?.Invoke($"✅ Command sent: {data.Length} bytes → {hex}");
                return Task.FromResult(true);
            }
            catch (Exception e)
            {
                StatusChanged?.Invoke($"❌ Error sending command: {e.Message}");
                return Task.FromResult(false);
            }
        }
        
        /// <summary>
        /// Sends a command packet to the robot in a way that won't interfere with polling
        /// This method is thread-safe and ensures commands and polls don't overlap
        /// </summary>
        private void SendCommandPacketSafe(byte[] commandPacket)
        {
            if (commandPacket == null || commandPacket.Length == 0)
                return;
                
            // Ensure we have an 8-byte packet (required by the protocol)
            byte[] paddedPacket = new byte[8];
            Array.Copy(commandPacket, paddedPacket, Math.Min(commandPacket.Length, 8));
            
            // Use a lock to ensure commands don't interfere with polling
            lock (_robotDataLock)
            {
                if (_bluetoothStream != null && _bluetoothStream.CanWrite)
                {
                    try
                    {
                        // Send the command
                        _bluetoothStream.Write(paddedPacket, 0, paddedPacket.Length);
                        _bluetoothStream.Flush();
                        
                        // Release the receive semaphore to allow the receiver to process any response
                        // This is crucial as the robot may send back a status update after a command
                        _receiveSemaphore.Release();
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[COMMAND] Error sending command: {ex.Message}");
                        throw; // Re-throw to notify caller
                    }
                }
                else
                {
                    throw new InvalidOperationException("Bluetooth stream not available for writing");
                }
            }
        }

        /// <summary>
        /// Clear queued commands (but preserve sensor data)
        /// </summary>
        public void ClearQueue()
        {
            // Clear send queue only (commands TO robot)
            int clearedSendCommands = 0;
            while (_sendQueue.TryDequeue(out _))
            {
                clearedSendCommands++;
            }

            // Do NOT clear receive queue - preserve most recent robot data
            StatusChanged?.Invoke($"🧹 Cleared {clearedSendCommands} pending commands (most recent robot data preserved)");
        }

        /// <summary>
        /// Get the most recent robot data stream received from robot
        /// CRITICAL: This ensures Scratch always has access to latest robot data stream
        /// </summary>
        public byte[] GetLatestSensorData()
        {
            lock (_robotDataLock)
            {
                if (_latestRobotData != null)
                {
                    var copy = new byte[_latestRobotData.Length];
                    Array.Copy(_latestRobotData, copy, _latestRobotData.Length);
                    return copy;
                }
                return null;
            }
        }

        /// <summary>
        /// Get age of most recent robot data in milliseconds
        /// </summary>
        public double GetSensorDataAgeMs()
        {
            lock (_robotDataLock)
            {
                if (_lastRobotDataTime == DateTime.MinValue)
                    return double.MaxValue;
                return (DateTime.Now - _lastRobotDataTime).TotalMilliseconds;
            }
        }

        /// <summary>
        /// Notify BluetoothManager that Scratch has connected
        /// CRITICAL: Immediately send most recent robot data to ensure safety
        /// </summary>
        public void OnScratchConnected()
        {
            _isScratchConnected = true;
            StatusChanged?.Invoke("🌟 Scratch connected - robot command sending enabled");

            // CRITICAL SAFETY: Immediately send most recent robot data to Scratch
            byte[] latestData = GetLatestSensorData();
            if (latestData != null)
            {
                var ageMs = GetSensorDataAgeMs();
                var hex = BitConverter.ToString(latestData).Replace("-", " ");
                StatusChanged?.Invoke($"🎯 SENDING MOST RECENT ROBOT DATA on connect: {latestData.Length} bytes, age: {ageMs:F0}ms → {hex}");

                // IMPORTANT: Don't send binary data to Scratch, use JSON-RPC format
                // Instead, update and push JSON status
                UpdateStatusJson();
                StatusJsonPushed?.Invoke(GetStatusJson() + "\n");

                StatusChanged?.Invoke("✅ Most recent robot data delivered to Scratch for safety");
            }
            else
            {
                StatusChanged?.Invoke("ℹ️ No robot data available yet");
            }
        }

        /// <summary>
        /// Notify BluetoothManager that Scratch has disconnected and clear pending commands
        /// POLICY: Block commands TO robot, but continue preserving sensor data FROM robot
        /// </summary>
        public void OnScratchDisconnected()
        {
            _isScratchConnected = false;

            // Clear only the SEND queue (commands TO robot) - preserve sensor data
            int clearedCount = 0;
            while (_sendQueue.TryDequeue(out _))
            {
                clearedCount++;
            }

            StatusChanged?.Invoke($"❌ Scratch disconnected - robot command sending disabled");
            if (clearedCount > 0)
            {
                StatusChanged?.Invoke($"🧹 Cleared {clearedCount} pending commands (sensor data preserved)");
            }

            // Continue preserving most recent robot data for when Scratch reconnects
            StatusChanged?.Invoke("📡 Continuing to preserve most recent robot data");
        }

        public async Task DisconnectAsync()
        {
            try
            {
                StatusChanged?.Invoke("Disconnecting...");

                // Set disconnected state immediately to stop all loops and operations
                _isConnected = false;

                // Stop OPTIMIZED async handlers
                StopOptimizedAsyncHandlers();

                // Close and dispose stream with timeout
                if (_bluetoothStream != null)
                {
                    try
                    {
                        var closeStreamTask = Task.Run(() =>
                        {
                            _bluetoothStream.Close();
                            _bluetoothStream.Dispose();
                        });

                        var timeoutTask = Task.Delay(2000);
                        var completedTask = await Task.WhenAny(closeStreamTask, timeoutTask);

                        if (completedTask == timeoutTask)
                        {
                            StatusChanged?.Invoke("Stream close timeout - forcing cleanup");
                        }
                    }
                    catch (Exception ex)
                    {
                        StatusChanged?.Invoke($"Stream close error: {ex.Message}");
                    }
                    finally
                    {
                        _bluetoothStream = null;
                    }
                }

                // Close Bluetooth client with timeout
                if (_bluetoothClient != null)
                {
                    try
                    {
                        if (_bluetoothClient.Connected)
                        {
                            var closeClientTask = Task.Run(() => _bluetoothClient.Close());
                            var timeoutTask = Task.Delay(3000);
                            var completedTask = await Task.WhenAny(closeClientTask, timeoutTask);

                            if (completedTask == timeoutTask)
                            {
                                StatusChanged?.Invoke("Bluetooth client close timeout - forcing disposal");
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        StatusChanged?.Invoke($"Bluetooth close error: {ex.Message}");
                    }

                    try
                    {
                        var disposeTask = Task.Run(() => _bluetoothClient.Dispose());
                        var timeoutTask = Task.Delay(2000);
                        var completedTask = await Task.WhenAny(disposeTask, timeoutTask);

                        if (completedTask == timeoutTask)
                        {
                            StatusChanged?.Invoke("Bluetooth client dispose timeout - cleanup may be incomplete");
                        }
                    }
                    catch (Exception ex)
                    {
                        StatusChanged?.Invoke($"Bluetooth dispose error: {ex.Message}");
                    }
                    finally
                    {
                        _bluetoothClient = null;
                    }
                }

                var deviceName = _connectedDevice?.DeviceName ?? "device";
                _connectedDevice = null;

                await Task.Delay(100);

                StatusChanged?.Invoke($"Disconnected from {deviceName}");
            }
            catch (Exception ex)
            {
                StatusChanged?.Invoke($"Error during disconnect: {ex.Message}");

                // Force cleanup even if there was an error
                _isConnected = false;
                _bluetoothStream = null;
                _bluetoothClient = null;
                _connectedDevice = null;
            }
        }

        /// <summary>
        /// Force immediate disconnect without waiting for graceful cleanup
        /// </summary>
        public void ForceDisconnect()
        {
            try
            {
                _isConnected = false;

                // Cancel handlers immediately
                _globalCancellation?.Cancel();

                // Force cleanup
                try { _bluetoothStream?.Close(); } catch { }
                try { _bluetoothStream?.Dispose(); } catch { }
                try { _bluetoothClient?.Close(); } catch { }
                try { _bluetoothClient?.Dispose(); } catch { }

                _bluetoothStream = null;
                _bluetoothClient = null;
                _connectedDevice = null;

                StatusChanged?.Invoke("Force disconnected");
            }
            catch (Exception ex)
            {
                StatusChanged?.Invoke($"Error in force disconnect: {ex.Message}");
            }
        }

        /// <summary>
        /// Select a specific Bluetooth adapter by index
        /// </summary>
        public bool SelectBluetoothAdapter(int index)
        {
            if (_availableRadios != null && index >= 0 && index < _availableRadios.Length)
            {
                _selectedRadio = _availableRadios[index];
                StatusChanged?.Invoke($"Selected adapter: {_selectedRadio?.Name ?? "None"}");
                return true;
            }
            return false;
        }

        /// <summary>
        /// Select a specific Bluetooth adapter by radio object
        /// </summary>
        public void SelectBluetoothAdapter(BluetoothRadio radio)
        {
            _selectedRadio = radio;
            StatusChanged?.Invoke($"Selected adapter: {radio?.Name ?? "None"}");
        }

        /// <summary>
        /// Refresh adapters and prefer TP-Link dongle
        /// </summary>
        public void RefreshAndSelectTPLinkDongle()
        {
            DetectBluetoothAdapters();
            PreferExternalDongle();
        }

        /// <summary>
        /// Get description of selected adapter
        /// </summary>
        public string GetSelectedAdapterDescription()
        {
            return SelectedAdapterInfo;
        }

        /// <summary>
        /// Pair with a device (simplified implementation)
        /// </summary>
        public async Task<bool> PairWithDeviceAsync(BluetoothDeviceInfo device, string pin = null)
        {
            try
            {
                StatusChanged?.Invoke($"Attempting to pair with {device.DeviceName}...");

                // For HC-05 and similar devices, pairing often happens during connection
                // This is a placeholder - actual pairing depends on device type
                await Task.Delay(1000);

                StatusChanged?.Invoke($"Pairing attempt completed for {device.DeviceName}");
                return true;
            }
            catch (Exception ex)
            {
                StatusChanged?.Invoke($"Pairing failed: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Unpair a device (simplified implementation)
        /// </summary>
        public async Task<bool> UnpairDeviceAsync(BluetoothDeviceInfo device)
        {
            try
            {
                StatusChanged?.Invoke($"Attempting to unpair {device.DeviceName}...");

                // This is a placeholder - actual unpairing is complex
                await Task.Delay(500);

                StatusChanged?.Invoke($"Unpair attempt completed for {device.DeviceName}");
                return true;
            }
            catch (Exception ex)
            {
                StatusChanged?.Invoke($"Unpair failed: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Detect all available Bluetooth adapters
        /// </summary>
        public void DetectBluetoothAdapters()
        {
            try
            {
                _availableRadios = BluetoothRadio.AllRadios;
                StatusChanged?.Invoke($"Detected {_availableRadios.Length} Bluetooth adapter(s)");
                BluetoothAdaptersDiscovered?.Invoke(_availableRadios);
            }
            catch (Exception ex)
            {
                StatusChanged?.Invoke($"Error detecting Bluetooth adapters: {ex.Message}");
                _availableRadios = new BluetoothRadio[0];
            }
        }

        /// <summary>
        /// Prefer external Bluetooth dongle over built-in adapter
        /// </summary>
        public bool PreferExternalDongle()
        {
            if (_availableRadios?.Length > 1)
            {
                // Look for TP-Link or other external dongles
                var externalDongle = _availableRadios.FirstOrDefault(r => IsTPLinkDongle(r));
                if (externalDongle != null)
                {
                    _selectedRadio = externalDongle;
                    StatusChanged?.Invoke($"Selected external dongle: {externalDongle.Name}");
                    return true;
                }
            }

            // Default to first available adapter
            if (_availableRadios?.Length > 0)
            {
                _selectedRadio = _availableRadios[0];
                StatusChanged?.Invoke($"Selected adapter: {_selectedRadio.Name}");
                return true;
            }

            return false;
        }

        /// <summary>
        /// Check if a Bluetooth radio appears to be a TP-Link dongle
        /// </summary>
        private bool IsTPLinkDongle(BluetoothRadio radio)
        {
            try
            {
                var name = radio.Name?.ToLowerInvariant() ?? "";
                var address = radio.LocalAddress?.ToString() ?? "";

                return name.Contains("tp-link") ||
                       name.Contains("tplink") ||
                       address.StartsWith("ac:84:c6"); // Common TP-Link OUI
            }
            catch
            {
                return false;
            }
        }

        #region TeBotRobot Polling

        /// <summary>
        /// Asynchronous handler that periodically polls the TeBotRobot for sensor data
        /// This handler both sends poll requests and reads responses from the TeBotRobot
        /// </summary>
        /// <summary>
        /// Polling handler that sends a poll command to TeBotRobot every 100ms
        /// This handler ONLY SENDS data - the TeBotReceiverHandler reads responses
        /// </summary>
        private async Task TeBotPollingHandler(CancellationToken cancellationToken)
        {
            try
            {
                StatusChanged?.Invoke("📊 TeBotRobot polling task started");
                _isPolling = true;

                // Create 8-byte poll command with first byte as 0x0A (poll command)
                // The protocol requires an 8-byte command where the first byte is 0x0A
                // and the remaining bytes are zeros
                byte[] pollCommand = new byte[8]; // All bytes initialized to 0 by default
                pollCommand[0] = TEBOT_POLL_COMMAND; // 0x0A

                int pollCount = 0;
                int errorCount = 0;
                DateTime lastErrorLogTime = DateTime.MinValue;
                DateTime lastSuccessTime = DateTime.MinValue;

                while (_isConnected && !cancellationToken.IsCancellationRequested)
                {
                    try
                    {
                        // Verify we have a valid connection before attempting to write
                        if (!_isConnected || _bluetoothClient == null || !_bluetoothClient.Connected)
                        {
                            // Connection appears to be lost, wait before checking again
                            await Task.Delay(TEBOT_POLL_INTERVAL_MS * 2, cancellationToken);
                            continue;
                        }

                        // Verify stream is available and writable
                        if (_bluetoothStream == null || !_bluetoothStream.CanWrite)
                        {
                            // No valid stream, wait before checking again
                            await Task.Delay(TEBOT_POLL_INTERVAL_MS, cancellationToken);
                            continue;
                        }

                        bool needsDelayAfterError = false;
                        int errorDelayTime = 0;

                        // Take a snapshot of data outside the lock to avoid await in lock
                        pollCount++;
                        bool shouldLog = pollCount % 10 == 0;

                        if (shouldLog)
                        {
                            Debug.WriteLine($"[TEBOT POLL] Sending poll command #{pollCount}");
                        }

                        // Use lock for the actual write operation only
                        lock (_robotDataLock)
                        {
                            try
                            {
                                // Send the poll command
                                _bluetoothStream.Write(pollCommand, 0, pollCommand.Length);
                                _bluetoothStream.Flush();

                                // Record success
                                lastSuccessTime = DateTime.Now;
                                errorCount = 0; // Reset error count on success

                                if (shouldLog)
                                {
                                    Debug.WriteLine($"[TEBOT POLL] Sent poll command #{pollCount} successfully");
                                }

                                // CRITICAL: Signal the receiver that a poll command has been sent
                                // This semaphore synchronizes the two threads in the protocol:
                                // 1. The sender releases the semaphore after sending a poll command
                                // 2. The receiver waits for this semaphore before reading data
                                // This ensures reception only happens after a poll command is sent
                                _receiveSemaphore.Release();
                            }
                            catch (System.Net.Sockets.SocketException socketEx)
                            {
                                // Handle socket exceptions specifically (common with Bluetooth)
                                errorCount++;

                                // Set flag to delay after we exit the lock
                                needsDelayAfterError = true;
                                errorDelayTime = errorCount > 5 ? TEBOT_POLL_INTERVAL_MS * 5 : 0;

                                // Log error but not too frequently
                                if (DateTime.Now.Subtract(lastErrorLogTime).TotalSeconds >= 5)
                                {
                                    StatusChanged?.Invoke($"⚠️ Bluetooth socket error during poll: {socketEx.Message}");
                                    lastErrorLogTime = DateTime.Now;
                                }
                            }
                        }

                        // Now outside the lock, we can safely await if needed
                        if (needsDelayAfterError && errorDelayTime > 0)
                        {
                            await Task.Delay(errorDelayTime, cancellationToken);
                            continue;
                        }

                        // Wait for the poll interval before next poll - this maintains the 100ms rhythm
                        // We use a dynamic interval that adjusts for processing time
                        var elapsedMs = DateTime.Now.Subtract(lastSuccessTime).TotalMilliseconds;
                        var remainingMs = TEBOT_POLL_INTERVAL_MS - elapsedMs;
                        var waitMs = Math.Max(10, Math.Min(TEBOT_POLL_INTERVAL_MS, remainingMs));
                        await Task.Delay((int)waitMs, cancellationToken);
                    }
                    catch (OperationCanceledException)
                    {
                        break; // Clean exit
                    }
                    catch (Exception ex)
                    {
                        errorCount++;

                        // Log errors but not too frequently
                        if (DateTime.Now.Subtract(lastErrorLogTime).TotalSeconds >= 5)
                        {
                            Debug.WriteLine($"[TEBOT POLL] Error: {ex.Message}");
                            lastErrorLogTime = DateTime.Now;

                            // If we haven't had a successful poll in 30 seconds, notify user
                            if (lastSuccessTime != DateTime.MinValue &&
                                DateTime.Now.Subtract(lastSuccessTime).TotalSeconds > 30)
                            {
                                StatusChanged?.Invoke("⚠️ No successful TeBotRobot polls in 30 seconds");
                            }
                        }

                        // Progressive backoff for errors
                        int delay = Math.Min(TEBOT_POLL_INTERVAL_MS * (1 + errorCount / 2), 2000);
                        await Task.Delay(delay, cancellationToken);
                    }
                }
            }
            catch (Exception ex)
            {
                StatusChanged?.Invoke($"❌ TeBotRobot polling task failed: {ex.Message}");
            }
            finally
            {
                _isPolling = false;
                StatusChanged?.Invoke("📊 TeBotRobot polling task stopped");
            }
        }

        /// <summary>
        /// Starts both TeBotRobot polling and receiver tasks - used for testing or manual start
        /// </summary>
        private void StartTeBotCommunication()
        {
            // Ensure we have a cancellation token
            if (_globalCancellation == null)
                _globalCancellation = new CancellationTokenSource();

            var token = _globalCancellation.Token;

            // Start the polling task if it's not already running
            if (_tebotPollingTask == null || _tebotPollingTask.IsCompleted)
            {
                _tebotPollingTask = Task.Run(async () => await TeBotPollingHandler(token), token);
                StatusChanged?.Invoke("📊 TeBotRobot polling task started");
            }

            // Start the receiver task if it's not already running
            if (_tebotReceiverTask == null || _tebotReceiverTask.IsCompleted)
            {
                _tebotReceiverTask = Task.Run(async () => await TeBotReceiverHandler(token), token);
                StatusChanged?.Invoke("📡 TeBotRobot receiver task started");
            }

            StatusChanged?.Invoke("✅ TeBotRobot communication started (2 threads)");
        }

        #endregion

        #region TeBotRobot Receiver

        /// <summary>
        /// COMMUNICATION PROTOCOL DETAILS:
        /// 
        /// 1. TEBOT ROBOT PROTOCOL:
        ///    - Commands TO TeBotRobot are 8-byte packets where first byte indicates command type
        ///    - Responses FROM TeBotRobot are 16-byte packets with sensor data
        ///    - Poll command (0x0A) requests the latest sensor data from TeBotRobot
        ///    
        /// 2. SENSOR DATA FORMAT (16 bytes):
        ///    - Byte 0:  Status code (ALWAYS 0x00)
        ///    - Byte 1:  Direction (0=stop, 1=forward, 2=backward, 3=left, 4=right)
        ///    - Byte 2:  Speed (0-100)
        ///    - Byte 3:  Battery level (0-100%)
        ///    - Byte 4-5: Ultrasonic distance (cm) - 16-bit little endian
        ///    - Byte 6-7: IR sensor value - 16-bit little endian
        ///    - Byte 8-9: Light sensor value - 16-bit little endian
        ///    - Byte 10:  Button states (bit 0 = button A, bit 1 = button B)
        ///    - Byte 11:  LED state (0=off, 1=on)
        ///    - Byte 12:  Movement status (0=stopped, 1=moving)
        ///    - Byte 13-14: Command counter - 16-bit little endian
        ///    - Byte 15:  Checksum (XOR of all preceding bytes)
        ///    
        /// 3. JSON-RPC FORMATTING:
        ///    - All responses to Scratch are in JSON-RPC 2.0 format
        ///    - Each JSON message must end with a newline character
        ///    - Format: {"jsonrpc":"2.0","result":{"sensors":{...}}}
        ///    - When responding to a request with an ID, include that ID in the response
        /// 
        /// 4. COMMUNICATION TIMING:
        ///    - Reception only happens AFTER the 8-byte transmission to TeBotRobot is complete
        ///    - TeBotRobot will respond with a 16-byte packet after receiving the 8-byte poll command
        ///    - The first byte of the 16-byte response is always 0x00
        ///    - A semaphore synchronizes the two threads: sender signals receiver after sending
        /// </summary>

        /// <summary>
        /// COMMUNICATION ARCHITECTURE OVERVIEW:
        /// 
        /// 1. POLLING THREAD (_tebotPollingTask):
        ///    - Sends 8-byte poll command (first byte 0x0A) every 100ms
        ///    - Only responsible for sending data to TeBotRobot
        ///    - Releases _receiveSemaphore after each send to signal the receiver
        ///    
        /// 2. RECEIVER THREAD (_tebotReceiverTask):
        ///    - Waits for _receiveSemaphore (with timeout) before reading from the stream
        ///    - When signaled, monitors Bluetooth stream for incoming data
        ///    - When data is received, it:
        ///      a) Validates that the first byte is 0x00 as per protocol
        ///      b) Verifies the packet is exactly 16 bytes as expected
        ///      c) Stores valid data in _latestRobotData (thread-safe)
        ///      d) Updates JSON-RPC formatted status using UpdateStatusJson()
        ///      e) Sends raw data to Scratch via DataReceived event
        ///      f) Sends JSON-RPC status via StatusJsonPushed event
        /// </summary>

        /// <summary>
        /// Receiver handler that continuously monitors for data from TeBotRobot
        /// This handler ONLY READS data and converts it to JSON-RPC for Scratch
        /// </summary>
        private async Task TeBotReceiverHandler(CancellationToken cancellationToken)
        {
            try
            {
                StatusChanged?.Invoke("📡 TeBotRobot receiver task started");
                _isReceiving = true;

                // Buffer for reading the TeBotRobot's 16-byte response
                // Make it larger to handle potentially misaligned data
                var buffer = new byte[32];

                // Ring buffer to accumulate data across multiple reads if needed
                List<byte> accumulatedData = new List<byte>(32);

                int cycleCount = 0;
                int errorCount = 0;
                int incompleteCount = 0;
                DateTime lastErrorLogTime = DateTime.MinValue;
                DateTime lastDataReceived = DateTime.MinValue;

                while (_isConnected && !cancellationToken.IsCancellationRequested)
                {
                    try
                    {
                        // Check if connection is valid
                        if (_bluetoothStream == null || !_bluetoothStream.CanRead)
                        {
                            await Task.Delay(100, cancellationToken);
                            continue;
                        }

                        // CRITICAL: Wait for the polling thread to signal that a poll command has been sent
                        // This ensures we only try to read data after sending a poll command, which is 
                        // essential for the protocol. Using a timeout to prevent deadlocks.
                        bool signalReceived = await _receiveSemaphore.WaitAsync(500, cancellationToken);

                        if (!signalReceived)
                        {
                            // No poll command was sent, so don't try to read data yet
                            await Task.Delay(10, cancellationToken);
                            continue;
                        }

                        // Signal received - a poll command was sent, now we expect a response
                        bool dataReceived = false;
                        bool validPacketFound = false;

                        // Clear the accumulated data since we're starting a new read cycle
                        accumulatedData.Clear();

                        // Use a timeout to avoid hanging reads
                        using (var readTimeoutCts = new CancellationTokenSource(2000))
                        using (var combinedCts = CancellationTokenSource.CreateLinkedTokenSource(
                            cancellationToken, readTimeoutCts.Token))
                        {
                            try
                            {
                                // IMPROVED: We'll read in a loop until we get a valid packet or timeout
                                DateTime readStartTime = DateTime.Now;

                                while (!validPacketFound &&
                                      DateTime.Now.Subtract(readStartTime).TotalMilliseconds < 1000 &&
                                      !combinedCts.Token.IsCancellationRequested)
                                {
                                    var readTask = _bluetoothStream.ReadAsync(buffer, 0, buffer.Length, combinedCts.Token);

                                    int bytesRead = await readTask;
                                    if (bytesRead > 0)
                                    {
                                        // Add the read data to our accumulated buffer
                                        for (int i = 0; i < bytesRead; i++)
                                        {
                                            accumulatedData.Add(buffer[i]);
                                        }

                                        // Only log some data (every 10th packet) to avoid flooding logs
                                        cycleCount++;
                                        if (cycleCount % 10 == 0)
                                        {
                                            Debug.WriteLine($"[RECEIVED FROM TEBOTROBOT] Accumulated {accumulatedData.Count} bytes: {BitConverter.ToString(accumulatedData.ToArray())}");
                                        }

                                        dataReceived = true;
                                        lastDataReceived = DateTime.Now;

                                        // Now try to find a valid packet in our accumulated data
                                        validPacketFound = TryExtractValidPacket(accumulatedData.ToArray(), out byte[] validPacket);

                                        if (validPacketFound && validPacket != null)
                                        {
                                            // Process the valid packet
                                            ProcessValidPacket(validPacket, cycleCount, ref lastErrorLogTime);

                                            // Reset error counters on successful packet
                                            errorCount = 0;
                                            incompleteCount = 0;
                                            break;
                                        }
                                        else if (accumulatedData.Count >= 16)
                                        {
                                            // We have enough data but no valid packet was found
                                            incompleteCount++;

                                            if (incompleteCount % 3 == 1 && DateTime.Now.Subtract(lastErrorLogTime).TotalSeconds > 5)
                                            {
                                                StatusChanged?.Invoke($"⚠️ Failed to find valid packet in {accumulatedData.Count} bytes of data");
                                                lastErrorLogTime = DateTime.Now;
                                            }

                                            // Try again with a short delay
                                            await Task.Delay(5, cancellationToken);
                                        }
                                        else
                                        {
                                            // Not enough data yet, continue reading
                                            await Task.Delay(5, cancellationToken);
                                        }
                                    }
                                    else
                                    {
                                        // No data read, wait briefly before trying again
                                        await Task.Delay(10, cancellationToken);
                                    }
                                }

                                // If we get here and haven't processed a valid packet yet
                                if (!validPacketFound && accumulatedData.Count > 0)
                                {
                                    if (accumulatedData.Count == 16)
                                    {
                                        // We have exactly 16 bytes, but they didn't pass validation
                                        // Report this specifically
                                        if (DateTime.Now.Subtract(lastErrorLogTime).TotalSeconds > 5)
                                        {
                                            StatusChanged?.Invoke($"⚠️ Received 16-byte packet but failed validation: First byte is 0x{accumulatedData[0]:X2} instead of 0x00");
                                            lastErrorLogTime = DateTime.Now;
                                        }
                                    }
                                    else
                                    {
                                        // Report incomplete packet
                                        if (DateTime.Now.Subtract(lastErrorLogTime).TotalSeconds > 5)
                                        {
                                            StatusChanged?.Invoke($"⚠️ Received incomplete packet: {accumulatedData.Count} bytes instead of 16 bytes");
                                            lastErrorLogTime = DateTime.Now;
                                        }
                                    }
                                }
                            }
                            catch (OperationCanceledException)
                            {
                                // Read timed out - this is normal if TeBotRobot hasn't sent data yet
                                if (DateTime.Now.Subtract(lastDataReceived).TotalSeconds > 5 &&
                                    lastDataReceived != DateTime.MinValue &&
                                    DateTime.Now.Subtract(lastErrorLogTime).TotalSeconds > 10)
                                {
                                    // Only log occasionally if we haven't received data in a while
                                    Debug.WriteLine("[RECEIVER] No data received in 5 seconds");
                                    lastErrorLogTime = DateTime.Now;
                                }
                            }
                        }

                        // Brief pause between reads to reduce CPU usage, adjusted based on whether we received data
                        await Task.Delay(dataReceived ? 1 : 10, cancellationToken);
                    }
                    catch (OperationCanceledException)
                    {
                        break; // Clean exit
                    }
                    catch (Exception ex)
                    {
                        errorCount++;

                        // Log errors but not too frequently
                        if (DateTime.Now.Subtract(lastErrorLogTime).TotalSeconds >= 5)
                        {
                            Debug.WriteLine($"[RECEIVER] Error: {ex.Message}");
                            lastErrorLogTime = DateTime.Now;
                        }

                        // Wait a bit longer if we're getting errors
                        await Task.Delay(Math.Min(100 * errorCount, 2000), cancellationToken);
                    }
                }
            }
            catch (Exception ex)
            {
                StatusChanged?.Invoke($"❌ TeBotRobot receiver task failed: {ex.Message}");
            }
            finally
            {
                _isReceiving = false;
                StatusChanged?.Invoke("📡 TeBotRobot receiver task stopped");
            }
        }

        /// <summary>
        /// Try to find and extract a valid 16-byte packet from the received data
        /// A valid packet is 16 bytes with the first byte being 0x00
        /// </summary>
        private bool TryExtractValidPacket(byte[] data, out byte[] validPacket)
        {
            validPacket = null;

            // If not enough data, can't be a valid packet
            if (data == null || data.Length < 16)
            {
                return false;
            }

            // Look for a valid packet starting at each position
            for (int i = 0; i <= data.Length - 16; i++)
            {
                // Check if we have a potential packet starting here (first byte = 0x00)
                if (data[i] == 0x00)
                {
                    // Extract 16 bytes starting from this position
                    validPacket = new byte[16];
                    Array.Copy(data, i, validPacket, 0, 16);

                    // Calculate and verify checksum
                    byte calculatedChecksum = 0;
                    for (int j = 0; j < 15; j++)
                    {
                        calculatedChecksum ^= validPacket[j];
                    }

                    // If checksum is valid, we found a valid packet
                    if (calculatedChecksum == validPacket[15])
                    {
                        return true;
                    }
                }
            }

            // No valid packet found
            validPacket = null;
            return false;
        }

        /// <summary>
        /// Process a valid 16-byte packet from the robot
        /// </summary>
        private void ProcessValidPacket(byte[] packet, int cycleCount, ref DateTime lastErrorLogTime)
        {
            if (packet == null || packet.Length != 16 || packet[0] != 0x00)
            {
                // This shouldn't happen since we validate before calling this method
                return;
            }

            // Store the valid packet
            lock (_robotDataLock)
            {
                _latestRobotData = new byte[packet.Length];
                Array.Copy(packet, _latestRobotData, packet.Length);
                _lastRobotDataTime = DateTime.Now;
            }

            // Update cached status JSON for fast status replies
            UpdateStatusJson();

            // IMPORTANT: We no longer send binary data directly to Scratch
            // Instead, we use the StatusJsonPushed event for JSON-RPC formatted data
            // Keep this line if you need it for internal event handling, but not for Scratch
            // DataReceived?.Invoke(packet);

            // Push JSON-RPC status to Scratch on every sensor update
            // Ensure each JSON-RPC message ends with a newline as required by the protocol
            StatusJsonPushed?.Invoke(GetStatusJson() + "\n");

            // Log a successful packet (only occasionally)
            if (cycleCount % 50 == 0)
            {
                StatusChanged?.Invoke($"Received valid 16-byte packet from TeBotRobot");
            }
        }
    

    #endregion

    /// <summary>
    /// COMMUNICATION ARCHITECTURE OVERVIEW:
    /// 
    /// 1. POLLING THREAD (_tebotPollingTask):
    ///    - Sends 8-byte poll command (first byte 0x0A) every 100ms
    ///    - Only responsible for sending data to TeBotRobot
    ///    - Releases _receiveSemaphore after each send to signal the receiver
    ///    
    /// 2. RECEIVER THREAD (_tebotReceiverTask):
    ///    - Waits for _receiveSemaphore (with timeout) before reading from the stream
    ///    - When signaled, monitors Bluetooth stream for incoming data
    ///    - When data is received, it:
    ///      a) Validates that the first byte is 0x00 as per protocol
    ///      b) Verifies the packet is exactly 16 bytes as expected
    ///      c) Stores valid data in _latestRobotData (thread-safe)
    ///      d) Updates JSON-RPC formatted status using UpdateStatusJson()
    ///      e) Sends raw data to Scratch via DataReceived event
    ///      f) Sends JSON-RPC status via StatusJsonPushed event
    ///    
    /// 3. JSON-RPC FORMATTING:
    ///    - The JSON-RPC format follows the TeBot protocol table
    ///    - Statuses include battery level, ultrasonic, IR, light, buttons, etc.
    ///    - Each status update is sent as a complete JSON object with a newline
    ///    - All JSON-RPC messages end with a newline character as required by protocol
    /// </summary>
    /// <summary>
    /// Handle a JSON-RPC request from Scratch and send the appropriate command to the robot
    /// </summary>
    public Task<string> HandleJsonRpcRequest(string jsonRequest)
    {
        if (string.IsNullOrWhiteSpace(jsonRequest))
            return Task.FromResult(CreateJsonRpcErrorResponse("Invalid empty request", -32600));

        try
        {
            // Parse the JSON-RPC request
            var requestObj = JsonConvert.DeserializeObject<Dictionary<string, object>>(jsonRequest);
            
            if (!requestObj.TryGetValue("method", out object methodObj) || !(methodObj is string method))
            {
                return Task.FromResult(CreateJsonRpcErrorResponse("Method not found", -32601));
            }

            // Get the request ID if present (for response correlation)
            string requestId = null;
            if (requestObj.TryGetValue("id", out object idObj))
            {
                requestId = idObj.ToString();
            }

            // Get parameters if present
            Dictionary<string, object> parameters = null;
            if (requestObj.TryGetValue("params", out object paramsObj) && paramsObj != null)
            {
                if (paramsObj is Dictionary<string, object> dict)
                {
                    parameters = dict;
                }
                else if (paramsObj is Newtonsoft.Json.Linq.JObject jobj)
                {
                    parameters = jobj.ToObject<Dictionary<string, object>>();
                }
            }

            // Handle different method types according to the protocol table
            switch (method.ToLowerInvariant())
            {
                case "status":
                    // For status requests, return the latest sensor data
                    return Task.FromResult(GetStatusJson(requestId) + "\n");

                case "moveforward":
                case "movebackward":
                case "turnleft":
                case "turnright":
                case "stop":
                case "setspeed":
                case "setled":
                case "playtone":
                case "setneomatrix":
                    try
                    {
                        // Log the request
                        StatusChanged?.Invoke($"Received JSON-RPC '{method}' command with params: {JsonConvert.SerializeObject(parameters)}");
                        
                        // Create the command packet for the robot
                        byte[] commandPacket = CreateCommandPacket(method, parameters);
                        
                        // Send the command to the robot
                        bool success = SendDataImmediately(commandPacket).Result;
                        
                        if (success)
                        {
                            // Return success response
                            var responseResult = new Dictionary<string, object>
                            {
                                ["success"] = true,
                                ["method"] = method
                            };
                            
                            if (parameters != null)
                                responseResult["params"] = parameters;
                                
                            return Task.FromResult(CreateJsonRpcSuccessResponse(requestId, responseResult) + "\n");
                        }
                        else
                        {
                            // Return error response for command failure
                            return Task.FromResult(CreateJsonRpcErrorResponse("Failed to send command to robot", -32000, requestId) + "\n");
                        }
                    }
                    catch (Exception ex)
                    {
                        // Return error for any exceptions during command execution
                        return Task.FromResult(CreateJsonRpcErrorResponse($"Command error: {ex.Message}", -32002, requestId) + "\n");
                    }

                default:
                    return Task.FromResult(CreateJsonRpcErrorResponse($"Unknown method: {method}", -32601, requestId) + "\n");
            }
        }
        catch (JsonException ex)
        {
            // JSON parsing error
            return Task.FromResult(CreateJsonRpcErrorResponse($"Invalid JSON: {ex.Message}", -32700) + "\n");
        }
        catch (Exception ex)
        {
            // General error
            return Task.FromResult(CreateJsonRpcErrorResponse($"Internal error: {ex.Message}", -32603) + "\n");
        }
    }

    /// <summary>
    /// Create a JSON-RPC success response
    /// </summary>
    private string CreateJsonRpcSuccessResponse(string id, object result)
    {
        var response = new Dictionary<string, object>
        {
            ["jsonrpc"] = "2.0"
        };

        if (id != null)
        {
            response["id"] = id;
        }

        response["result"] = result ?? new Dictionary<string, object>();

        return JsonConvert.SerializeObject(response);
    }

    /// <summary>
    /// Create a JSON-RPC error response
    /// </summary>
    private string CreateJsonRpcErrorResponse(string message, int code, string id = null)
    {
        var response = new Dictionary<string, object>
        {
            ["jsonrpc"] = "2.0",
            ["error"] = new Dictionary<string, object>
            {
                ["code"] = code,
                ["message"] = message
            }
        };

        if (id != null)
        {
            response["id"] = id;
        }

        return JsonConvert.SerializeObject(response);
    }

    /// <summary>
    /// Creates an 8-byte command packet for the robot based on JSON-RPC method and parameters
    /// </summary>
    private byte[] CreateCommandPacket(string method, Dictionary<string, object> parameters)
    {
        byte[] commandPacket = new byte[8]; // All bytes initialized to 0 by default
        
        // Default speed to use if not specified in parameters
        byte speed = 50;
        
        // Extract speed parameter if present
        if (parameters != null && parameters.TryGetValue("speed", out object speedObj))
        {
            // Try to parse the speed parameter (handle both string and numeric formats)
            if (speedObj is long longSpeed)
            {
                speed = (byte)Math.Min(100, Math.Max(0, longSpeed));
            }
            else if (speedObj is int intSpeed)
            {
                speed = (byte)Math.Min(100, Math.Max(0, intSpeed));
            }
            else if (speedObj is double doubleSpeed)
            {
                speed = (byte)Math.Min(100, Math.Max(0, doubleSpeed));
            }
            else if (speedObj is string speedStr && byte.TryParse(speedStr, out byte parsedSpeed))
            {
                speed = Math.Min((byte)100, parsedSpeed);
            }
        }
        
        // Set command type based on method
        switch (method.ToLowerInvariant())
        {
            case "moveforward":
                commandPacket[0] = CMD_MOVE_FORWARD;
                commandPacket[1] = speed;
                break;
                
            case "movebackward":
                commandPacket[0] = CMD_MOVE_BACKWARD;
                commandPacket[1] = speed;
                break;
                
            case "turnleft":
                commandPacket[0] = CMD_TURN_LEFT;
                commandPacket[1] = speed;
                break;
                
            case "turnright":
                commandPacket[0] = CMD_TURN_RIGHT;
                commandPacket[1] = speed;
                break;
                
            case "stop":
                commandPacket[0] = CMD_STOP;
                break;
                
            case "setspeed":
                commandPacket[0] = CMD_SET_SPEED;
                commandPacket[1] = speed;
                break;
                
            case "setled":
                commandPacket[0] = CMD_SET_LED;
                if (parameters != null && parameters.TryGetValue("packed", out object packedObj))
                {
                    // Try to parse the packed LED parameter
                    if (packedObj is long longPacked)
                        commandPacket[1] = (byte)longPacked;
                    else if (packedObj is int intPacked)
                        commandPacket[1] = (byte)intPacked;
                    else if (packedObj is string packedStr && byte.TryParse(packedStr, out byte parsedPacked))
                        commandPacket[1] = parsedPacked;
                }
                break;
                
            case "playtone":
                commandPacket[0] = CMD_PLAY_TONE;
                if (parameters != null)
                {
                    // Get b1, b2, b3 parameters if present
                    if (parameters.TryGetValue("b1", out object b1Obj) && b1Obj is long b1Long)
                        commandPacket[1] = (byte)b1Long;
                    if (parameters.TryGetValue("b2", out object b2Obj) && b2Obj is long b2Long)
                        commandPacket[2] = (byte)b2Long;
                    if (parameters.TryGetValue("b3", out object b3Obj) && b3Obj is long b3Long)
                        commandPacket[3] = (byte)b3Long;
                }
                break;
                
            case "setneomatrix":
                commandPacket[0] = CMD_SET_NEOMATRIX;
                if (parameters != null)
                {
                    // Get p1, p2, p3, p4 parameters if present
                    if (parameters.TryGetValue("p1", out object p1Obj) && p1Obj is long p1Long)
                        commandPacket[1] = (byte)p1Long;
                    if (parameters.TryGetValue("p2", out object p2Obj) && p2Obj is long p2Long)
                        commandPacket[2] = (byte)p2Long;
                    if (parameters.TryGetValue("p3", out object p3Obj) && p3Obj is long p3Long)
                        commandPacket[3] = (byte)p3Long;
                    if (parameters.TryGetValue("p4", out object p4Obj) && p4Obj is long p4Long)
                        commandPacket[4] = (byte)p4Long;
                }
                break;
                
            default:
                // Unknown method, use poll command as fallback
                commandPacket[0] = TEBOT_POLL_COMMAND;
                break;
        }
        
        return commandPacket;
    }
}
}