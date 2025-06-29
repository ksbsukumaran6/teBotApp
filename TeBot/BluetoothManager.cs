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
        
        // OPTIMIZED: Separate async handlers for send/receive
        private readonly ConcurrentQueue<byte[]> _sendQueue = new ConcurrentQueue<byte[]>();
        private readonly ConcurrentQueue<byte[]> _receiveQueue = new ConcurrentQueue<byte[]>();
        
        // Async task management
        private CancellationTokenSource _globalCancellation;
        private Task _bluetoothReceiverTask;
        private Task _dataProcessorTask;
        private Task _senderTask;
        
        // Synchronization
        private readonly SemaphoreSlim _sendSemaphore = new SemaphoreSlim(0);
        private readonly SemaphoreSlim _receiveSemaphore = new SemaphoreSlim(0);
        
        // State tracking
        private bool _isReceiving = false;
        private bool _isProcessing = false;
        private bool _isSending = false;
        
        // NEW: Track Scratch connection state to prevent sending when disconnected
        private bool _isScratchConnected = false;
        
        // CRITICAL: Always preserve most recent robot data stream for safety
        private byte[] _latestRobotData = null;
        private DateTime _lastRobotDataTime = DateTime.MinValue;
        private readonly object _robotDataLock = new object();
        
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
        public event Action<int> QueueStatus;
        public event Action<BluetoothRadio[]> BluetoothAdaptersDiscovered;

        public bool IsConnected => _isConnected;
        public string ConnectedDeviceName => _connectedDevice?.DeviceName ?? "None";
        public int QueuedDataCount => _sendQueue.Count;
        public bool IsReceiving => _isReceiving;
        public bool IsProcessing => _isProcessing;
        public bool IsSending => _isSending;
        
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
            
            StatusChanged?.Invoke("🚀 Starting optimized async handlers...");
            
            // CRITICAL DEBUG: Verify stream state before starting handlers
            var streamInfo = _bluetoothStream != null ? 
                $"Stream exists: CanRead={_bluetoothStream.CanRead}, CanWrite={_bluetoothStream.CanWrite}" : 
                "Stream is NULL!";
            StatusChanged?.Invoke($"🔍 STREAM STATE: {streamInfo}");
            
            var clientInfo = _bluetoothClient != null ? 
                $"Client exists: Connected={_bluetoothClient.Connected}" : 
                "Client is NULL!";
            StatusChanged?.Invoke($"🔍 CLIENT STATE: {clientInfo}");
            
            // HANDLER 1: Dedicated Bluetooth receiver (highest priority)
            _bluetoothReceiverTask = Task.Run(async () => await BluetoothReceiverHandler(token), token);
            StatusChanged?.Invoke("📡 RECEIVER TASK CREATED");
            
            // HANDLER 2: Data processor (processes received data and forwards to Scratch)
            _dataProcessorTask = Task.Run(async () => await DataProcessorHandler(token), token);
            StatusChanged?.Invoke("⚙️ PROCESSOR TASK CREATED");
            
            // HANDLER 3: Sender (handles outgoing commands)
            _senderTask = Task.Run(async () => await SenderHandler(token), token);
            StatusChanged?.Invoke("📤 SENDER TASK CREATED");
            
            StatusChanged?.Invoke("✅ Optimized async handlers started - send/receive now fully separated");
        }
        
        /// <summary>
        /// Stop all async handlers
        /// </summary>
        private async void StopOptimizedAsyncHandlers()
        {
            try
            {
                StatusChanged?.Invoke("🛑 Stopping async handlers...");

                _globalCancellation?.Cancel();

                var tasks = new[] { _bluetoothReceiverTask, _dataProcessorTask, _senderTask }
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

                _isReceiving = false;
                _isProcessing = false;
                _isSending = false;

                StatusChanged?.Invoke("✅ All async handlers stopped");
            }
            catch (Exception ex)
            {
                StatusChanged?.Invoke($"❌ Error stopping handlers: {ex.Message}");
            }
        }
        
        /// <summary>
        /// HANDLER 1: Dedicated Bluetooth receiver - continuously reads from stream
        /// Now expects incoming data as hex string, decodes to bytes, and enqueues
        private async Task BluetoothReceiverHandler(CancellationToken cancellationToken)
        {
            StatusChanged?.Invoke("📡 🔥 RECEIVER HANDLER STARTED - Thread ID: " + System.Threading.Thread.CurrentThread.ManagedThreadId);

            // CRITICAL DEBUG: Verify stream state before starting
            var streamInfo = _bluetoothStream != null ?
                $"CanRead={_bluetoothStream.CanRead}, CanWrite={_bluetoothStream.CanWrite}" :
                "Stream is NULL!";
            StatusChanged?.Invoke($"📡 🔍 STREAM STATE: {streamInfo}");

            var clientInfo = _bluetoothClient != null ?
                $"Connected={_bluetoothClient.Connected}" :
                "Client is NULL!";
            StatusChanged?.Invoke($"📡 🔍 CLIENT STATE: {clientInfo}");

            StatusChanged?.Invoke($"📡 🔍 _isConnected FLAG: {_isConnected}");

            _isReceiving = true;

            var buffer = new byte[16];
            int cycleCount = 0;
            int timeoutCount = 0;

            try
            {
                while (_isConnected && !cancellationToken.IsCancellationRequested)
                {
                    try
                    {
                        cycleCount++;
                        StatusChanged?.Invoke($"📡 [DEBUG] Read attempt #{cycleCount}");

                        if (_bluetoothStream?.CanRead == true)
                        {
                            StatusChanged?.Invoke($"📡 [DEBUG] Attempting to read from stream...");
                            // Read as usual
                            var readTask = _bluetoothStream.ReadAsync(buffer, 0, buffer.Length, cancellationToken);
                            var timeoutTask = Task.Delay(2000, cancellationToken);
                            var completedTask = await Task.WhenAny(readTask, timeoutTask);

                            if (completedTask == readTask && !readTask.IsFaulted)
                            {
                                int bytesRead = await readTask;
                                StatusChanged?.Invoke($"📡 [DEBUG] Read completed, bytesRead={bytesRead}");

                                if (bytesRead > 0)
                                {
                                    // Convert bytes to hex string, then back to bytes (simulate hex string transfer)
                                    string hexString = HexUtils.BytesToHexString(buffer.Take(bytesRead).ToArray());
                                    var receivedData = HexUtils.HexStringToBytes(hexString);

                                    _receiveQueue.Enqueue(receivedData);
                                    _receiveSemaphore.Release();

                                    StatusChanged?.Invoke($"📡 🎯 ROBOT DATA DETECTED! {bytesRead} bytes → {hexString}");
                                    timeoutCount = 0;
                                }
                                else
                                {
                                    StatusChanged?.Invoke($"📡 [DEBUG] Zero bytes read from stream.");
                                }
                            }
                            else if (completedTask == timeoutTask)
                            {
                                timeoutCount++;
                                StatusChanged?.Invoke($"📡 [DEBUG] Read timeout #{timeoutCount}");
                            }
                            else if (readTask.IsFaulted)
                            {
                                StatusChanged?.Invoke($"📡 [DEBUG] Read task faulted: {readTask.Exception?.GetBaseException()?.Message}");
                                await Task.Delay(1000, cancellationToken);
                            }
                        }
                        else
                        {
                            StatusChanged?.Invoke($"📡 [DEBUG] Stream not readable - CanRead={_bluetoothStream?.CanRead}");
                        }

                        await Task.Delay(1, cancellationToken);
                    }
                    catch (OperationCanceledException)
                    {
                        StatusChanged?.Invoke("📡 Receiver cancelled - clean exit");
                        break;
                    }
                    catch (Exception ex)
                    {
                        StatusChanged?.Invoke($"📡 [DEBUG] Receiver error: {ex.Message}");
                        StatusChanged?.Invoke($"📡 [DEBUG] Error type: {ex.GetType().Name}");
                        StatusChanged?.Invoke($"📡 [DEBUG] StackTrace: {ex.StackTrace}");
                        await Task.Delay(100, cancellationToken);
                    }
                }
            }
            finally
            {
                _isReceiving = false;
                StatusChanged?.Invoke($"📡 Bluetooth receiver handler stopped after {cycleCount} cycles, {timeoutCount} timeouts");
            }
        }
        
        /// <summary>
        /// HANDLER 2: Data processor - processes queued receive data and forwards to Scratch
        /// CRITICAL: Always preserve sensor data regardless of Scratch connection state
        /// </summary>
        private async Task DataProcessorHandler(CancellationToken cancellationToken)
        {
            StatusChanged?.Invoke("⚙️ Data processor handler started");
            _isProcessing = true;
            
            try
            {
                while (_isConnected && !cancellationToken.IsCancellationRequested)
                {
                    try
                    {
                        // Wait for received data (with timeout)
                        await _receiveSemaphore.WaitAsync(100, cancellationToken);
                        
                        // Process all available data from robot
                        while (_receiveQueue.TryDequeue(out byte[] receivedData))
                        {
                            var hex = BitConverter.ToString(receivedData).Replace("-", " ");
                            StatusChanged?.Invoke($"⚙️ PROCESSING ROBOT DATA: {receivedData.Length} bytes → {hex}");
                            
                            // CRITICAL: Always preserve most recent robot data stream for safety
                            lock (_robotDataLock)
                            {
                                _latestRobotData = new byte[receivedData.Length];
                                Array.Copy(receivedData, _latestRobotData, receivedData.Length);
                                _lastRobotDataTime = DateTime.Now;
                            }
                            
                            StatusChanged?.Invoke($"💾 PRESERVED most recent robot data: {receivedData.Length} bytes");
                            
                            // ALWAYS forward to Scratch immediately if connected
                            // This ensures real-time data stream updates for safety
                            DataReceived?.Invoke(receivedData);
                            
                            if (_isScratchConnected)
                            {
                                StatusChanged?.Invoke($"✅ FORWARDED to Scratch: {receivedData.Length} bytes");
                            }
                            else
                            {
                                StatusChanged?.Invoke($"📡 PRESERVED (Scratch disconnected): {receivedData.Length} bytes");
                            }
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        break; // Clean exit
                    }
                    catch (Exception ex)
                    {
                        StatusChanged?.Invoke($"⚙️ Processor error: {ex.Message}");
                        await Task.Delay(100, cancellationToken);
                    }
                }
            }
            finally
            {
                _isProcessing = false;
                StatusChanged?.Invoke("⚙️ Data processor handler stopped");
            }
        }
        
        /// <summary>
        /// HANDLER 3: Sender - handles outgoing commands to robot
        /// POLICY: Only send commands TO robot when Scratch is connected
        /// </summary>
        private async Task SenderHandler(CancellationToken cancellationToken)
        {
            StatusChanged?.Invoke("📤 Sender handler started");
            _isSending = true;
            
            try
            {
                while (_isConnected && !cancellationToken.IsCancellationRequested)
                {
                    try
                    {
                        // Wait for send data (with timeout)
                        await _sendSemaphore.WaitAsync(25, cancellationToken);
                        
                        // Send all queued data - but only if Scratch is connected
                        while (_sendQueue.TryDequeue(out byte[] sendData))
                        {
                            if (!_isScratchConnected)
                            {
                                // SAFETY: Scratch is disconnected - discard COMMAND data only
                                var discardHex = BitConverter.ToString(sendData).Replace("-", " ");
                                StatusChanged?.Invoke($"🚫 DISCARDING COMMAND: {sendData.Length} bytes (Scratch disconnected) → {discardHex}");
                                continue; // Skip sending this command
                            }

                            if (_bluetoothStream?.CanWrite == true)
                            {
                                // Send the raw bytes directly to Arduino
                                StatusChanged?.Invoke($"📤 SENDING RAW BYTES: {BitConverter.ToString(sendData)}");
                                await _bluetoothStream.WriteAsync(sendData, 0, sendData.Length, cancellationToken);
                                await _bluetoothStream.FlushAsync(cancellationToken);

                                StatusChanged?.Invoke($"✅ Command sent to robot as raw bytes: {sendData.Length} bytes");
                                QueueStatus?.Invoke(_sendQueue.Count);
                            }
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        break; // Clean exit
                    }
                    catch (Exception ex)
                    {
                        StatusChanged?.Invoke($"📤 Sender error: {ex.Message}");
                        await Task.Delay(100, cancellationToken);
                    }
                }
            }
            finally
            {
                _isSending = false;
                StatusChanged?.Invoke("📤 Sender handler stopped");
            }
        }

        /// <summary>
        /// Send data immediately - optimized bridge from Scratch to robot
        /// Accepts either a byte array or a hex string (for minimal data traffic)
        /// POLICY: Only send commands when Scratch is connected
        /// </summary>
        public Task<bool> SendDataImmediately(string hex)
        {
            if (string.IsNullOrWhiteSpace(hex))
                return Task.FromResult(false);

            var data = HexUtils.HexStringToBytes(hex);
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
                StatusChanged?.Invoke("Not connected to any Bluetooth device");
                return Task.FromResult(false);
            }

            if (!_isScratchConnected)
            {
                var hex = BitConverter.ToString(data).Replace("-", " ");
                StatusChanged?.Invoke($"🚫 BLOCKED COMMAND: {data.Length} bytes (Scratch disconnected) → {hex}");
                return Task.FromResult(false);
            }

            try
            {
                // Convert to hex string for logging
                var hex = HexUtils.BytesToHexString(data);
                StatusChanged?.Invoke($"🚀 QUEUED COMMAND for immediate send: {data.Length} bytes → {hex}");

                // OPTIMIZED: Use async sender queue for immediate processing
                _sendQueue.Enqueue(data);
                _sendSemaphore.Release(); // Signal sender handler immediately
                return Task.FromResult(true);
            }
            catch (Exception ex)
            {
                StatusChanged?.Invoke($"Error queuing data: {ex.Message}");
                return Task.FromResult(false);
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
                
                // Fire event immediately to ensure Scratch gets most recent robot state
                DataReceived?.Invoke(latestData);
                
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
    }
}
