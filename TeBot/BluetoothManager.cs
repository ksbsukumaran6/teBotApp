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
    public class BluetoothManager
    {
        private BluetoothClient _bluetoothClient;
        private BluetoothDeviceInfo _connectedDevice;
        private Stream _bluetoothStream;
        private bool _isConnected;
        private System.Timers.Timer _flushTimer;
          // Data transmission management
        private readonly ConcurrentQueue<byte[]> _dataQueue = new ConcurrentQueue<byte[]>();
        private readonly ConcurrentQueue<byte[]> _receivedDataQueue = new ConcurrentQueue<byte[]>();
        private readonly SemaphoreSlim _responseAvailableSemaphore = new SemaphoreSlim(0);
        private readonly List<List<byte[]>> _receivedLists = new List<List<byte[]>>();
        private Timer _transmissionTimer;
        private CancellationTokenSource _transmissionCancellation;
        private CancellationTokenSource _readingCancellation;
        private readonly ManualResetEventSlim _dataAvailable = new ManualResetEventSlim(false);
        private readonly object _transmissionLock = new object();
        private readonly object _receivedListsLock = new object();
        private bool _isTransmitting = false;
        private bool _isReading = false;        // Constants optimized for 115200 baud HC-05 (12x faster than 9600)
        // At 115200 baud: ~11520 bytes/sec, so 8 bytes = ~0.7ms, 82 bytes = ~7ms
        private const int TRANSMISSION_INTERVAL_MS = 100; // Fast for 115200 baud 
        private const int RESPONSE_TIMEOUT_MS = 500; // Shorter timeout for fast baud rate (500ms)
        private const int DATA_PACKET_SIZE = 8;
        private const int CONTINUOUS_TRANSMISSION_INTERVAL_MS = 200; // Fast for 115200 baud (200ms)
        private const int CONNECTION_TIMEOUT_MS = 10000; // Standard connection timeout (10 seconds)
        private const int READ_TIMEOUT_MS = 1000; // Shorter read timeout for 115200 baud (1 second)
        private const int STREAM_TIMEOUT_MS = 5000; // Standard stream timeout (5 seconds)
        private const int INTER_BYTE_DELAY_MS = 0; // No delay needed at 115200 baud

        public event Action<string> StatusChanged;
        public event Action<BluetoothDeviceInfo[]> DevicesDiscovered;
        public event Action<byte[]> DataReceived;
        public event Action<int> QueueStatus; // Reports queue count
        public event Action<bool> TransmissionStatus; // Reports transmission state

        // Continuous transmission management
        private Timer _continuousTransmissionTimer;
        private bool _isContinuousMode = false;
        private int _continuousPacketCounter = 0;
          // Events for continuous mode
        public event Action<List<byte[]>> ContinuousResponseReceived; // Fired when a complete response list is received
        
        // Listen-only mode for testing
        private bool _isListenOnlyMode = false;
        
        // Master/Slave role identification
        private bool _isMasterMode = true; // TeBot acts as master by default
        
        // Bluetooth adapter management for external dongles
        private BluetoothRadio[] _availableRadios;
        private BluetoothRadio _selectedRadio;
        
        // Events for adapter discovery
        public event Action<BluetoothRadio[]> BluetoothAdaptersDiscovered;

        public bool IsConnected => _isConnected;
        public string ConnectedDeviceName => _connectedDevice?.DeviceName ?? "None";
        public int QueuedDataCount => _dataQueue.Count;
        public bool IsTransmitting => _isTransmitting;
        public bool IsContinuousMode => _isContinuousMode;
        public bool IsListenOnlyMode => _isListenOnlyMode;
        public bool IsMasterMode => _isMasterMode;
        
        // Additional properties for adapter management
        public BluetoothRadio[] AvailableBluetoothAdapters => _availableRadios ?? new BluetoothRadio[0];
        public BluetoothRadio SelectedBluetoothAdapter => _selectedRadio;
        public string SelectedAdapterInfo => _selectedRadio != null ? 
            $"{_selectedRadio.Name} ({_selectedRadio.LocalAddress})" : "None";

        public BluetoothManager()
        {
            // Initialize and detect Bluetooth adapters first
            DetectBluetoothAdapters();
            
            _bluetoothClient = new BluetoothClient();
            
            // Setup periodic flush timer for better performance
            _flushTimer = new System.Timers.Timer(50); // Flush every 50ms
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
        }        public async Task<bool> ConnectToDeviceAsync(BluetoothDeviceInfo device)
        {
            try
            {
                if (_isConnected)
                {
                    await DisconnectAsync();
                }

                // Create a new BluetoothClient instance to avoid ObjectDisposedException
                // after reconnecting (the previous client may have been disposed)
                _bluetoothClient?.Dispose();
                _bluetoothClient = new BluetoothClient();

                StatusChanged?.Invoke($"Connecting to {device.DeviceName}...");

                // Common Bluetooth service UUIDs
                var serviceUuids = new[]
                {
                    BluetoothService.SerialPort,           // SPP - Serial Port Profile
                    new Guid("0000ffe0-0000-1000-8000-00805f9b34fb"), // Common UART service
                    new Guid("6e400001-b5a3-f393-e0a9-e50e24dcca9e")  // Nordic UART service
                };

                Exception lastException = null;
                
                foreach (var serviceUuid in serviceUuids)
                {                    try
                    {
                        var endpoint = new BluetoothEndPoint(device.DeviceAddress, serviceUuid);
                          // Add timeout wrapper for connection attempt
                        var connectTask = Task.Run(() =>
                        {
                            _bluetoothClient.Connect(endpoint);
                        });
                          var timeoutTask = Task.Delay(CONNECTION_TIMEOUT_MS); // Use constant (15 seconds)
                        var completedTask = await Task.WhenAny(connectTask, timeoutTask);
                        
                        if (completedTask == timeoutTask)
                        {
                            throw new TimeoutException($"Connection timeout after {CONNECTION_TIMEOUT_MS/1000} seconds using {serviceUuid}");
                        }
                        
                        if (connectTask.IsFaulted)
                        {
                            throw connectTask.Exception?.GetBaseException() ?? new Exception("Connection failed");
                        }
                        
                        _bluetoothStream = _bluetoothClient.GetStream();
                          // HC-05 specific stream settings                       
                         if (_bluetoothStream.CanTimeout)
                        {
                            _bluetoothStream.WriteTimeout = STREAM_TIMEOUT_MS; // Use constant
                            _bluetoothStream.ReadTimeout = STREAM_TIMEOUT_MS;  // Use constant
                        }
                          _connectedDevice = device;
                        _isConnected = true;
                        
                        StatusChanged?.Invoke($"Connected to {device.DeviceName} (optimized for 115200 baud)");
                        Debug.WriteLine($"Successfully connected using service UUID: {serviceUuid}");
                        
                        // Start the transmission system and data reading
                        StartTransmissionSystem();
                        StartDataReading();
                        
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
                return false;
            }
            catch (Exception ex)
            {
                StatusChanged?.Invoke($"Error connecting to device: {ex.Message}");
                return false;            }
        }        public async Task<bool> SendDataAsync(byte[] data)
        {
            // Validate data size
            if (data == null || data.Length != DATA_PACKET_SIZE)
            {
                StatusChanged?.Invoke($"Invalid data size. Expected {DATA_PACKET_SIZE} bytes, got {data?.Length ?? 0}");
                return false;
            }

            if (!_isConnected)
            {
                StatusChanged?.Invoke("Not connected to any Bluetooth device");
                return false;
            }

            // Queue the data for transmission
            await Task.Run(() => {
                _dataQueue.Enqueue(data);
                _dataAvailable.Set();
            });
            
            QueueStatus?.Invoke(_dataQueue.Count);
            Debug.WriteLine($"Queued {data.Length} bytes for transmission. Queue size: {_dataQueue.Count}");
            return true;
        }

        /// <summary>
        /// Send data immediately - simple bridge from Scratch to robot
        /// </summary>
        public async Task<bool> SendDataImmediately(byte[] data)
        {
            if (data == null || data.Length == 0)
            {
                return false;
            }

            if (!_isConnected)
            {
                StatusChanged?.Invoke("Not connected to any Bluetooth device");
                return false;
            }

            try
            {
                // SIMPLE: Just send the data directly to robot
                return await SendDataDirectlyAsync(data);
            }
            catch (Exception ex)
            {
                StatusChanged?.Invoke($"Error sending data: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Clear any stale data from the receive buffer to ensure clean communication
        /// </summary>
        private void ClearReceiveBuffer()
        {
            try
            {
                int clearedFromQueue = 0;
                while (_receivedDataQueue.TryDequeue(out byte[] _))
                {
                    clearedFromQueue++;
                }
                
                if (clearedFromQueue > 0)
                {
                    StatusChanged?.Invoke($"Cleared {clearedFromQueue} stale packets from receive queue");
                    Debug.WriteLine($"Cleared {clearedFromQueue} stale packets from receive queue");
                }
            }
            catch (Exception ex)
            {
                StatusChanged?.Invoke($"Error clearing receive buffer: {ex.Message}");
            }
        }

        /// <summary>
        /// Clear all queued commands (called when Scratch blocks are deactivated)
        /// </summary>
        public void ClearQueue()
        {
            int clearedCount = 0;
            while (_dataQueue.TryDequeue(out byte[] _))
            {
                clearedCount++;
            }
            
            QueueStatus?.Invoke(0);
            Debug.WriteLine($"Cleared {clearedCount} commands from queue");
            StatusChanged?.Invoke($"Cleared {clearedCount} queued commands");
        }

        /// <summary>
        /// Send a 2D array of data (e.g., dataToSend[10][8])
        /// </summary>
        public async Task<bool> SendDataArrayAsync(byte[][] dataArray)
        {
            if (dataArray == null || dataArray.Length == 0)
            {
                StatusChanged?.Invoke("No data to send");
                return false;
            }

            if (!_isConnected)
            {
                StatusChanged?.Invoke("Not connected to any Bluetooth device");
                return false;
            }            int validPackets = 0;
            await Task.Run(() => {
                foreach (var packet in dataArray)
                {
                    if (packet != null && packet.Length == DATA_PACKET_SIZE)
                    {
                        _dataQueue.Enqueue(packet);
                        validPackets++;
                    }
                    else
                    {
                        StatusChanged?.Invoke($"Skipped invalid packet: {packet?.Length ?? 0} bytes");
                    }
                }
            });

            if (validPackets > 0)
            {
                _dataAvailable.Set();
                QueueStatus?.Invoke(_dataQueue.Count);
                StatusChanged?.Invoke($"Queued {validPackets} data packets for transmission");
                return true;
            }

            return false;
        }        /// <summary>
        /// Send multiple 2D arrays of data and receive corresponding response lists
        /// </summary>       
        public async Task<List<List<byte[]>>> SendMultipleDataArraysAsync(List<byte[][]> dataArrays)
        {
            var responseLists = new List<List<byte[]>>();
            
            if (!_isConnected || dataArrays == null || dataArrays.Count == 0)
            {
                StatusChanged?.Invoke("Cannot send multiple arrays: not connected or no data provided");
                return responseLists;
            }

            try
            {
                // Clear previous received lists
                lock (_receivedListsLock)
                {
                    _receivedLists.Clear();
                }

                StatusChanged?.Invoke($"Starting transmission of {dataArrays.Count} data arrays with 150ms intervals");

                for (int i = 0; i < dataArrays.Count; i++)
                {
                    var dataArray = dataArrays[i];
                    if (dataArray == null) 
                    {
                        StatusChanged?.Invoke($"Skipping null array {i + 1}");
                        responseLists.Add(new List<byte[]>());
                        continue;
                    }

                    StatusChanged?.Invoke($"Sending array {i + 1}/{dataArrays.Count} with {dataArray.Length} packets");

                    // Send all packets from this array in one shot
                    await SendDataArrayAsync(dataArray);
                    
                    // Wait for responses with 100ms timeout per packet
                    var responses = await WaitForResponsesAsync(dataArray.Length, TimeSpan.FromMilliseconds(100 * dataArray.Length));
                    
                    lock (_receivedListsLock)
                    {
                        _receivedLists.Add(responses);
                        responseLists.Add(new List<byte[]>(responses));
                    }
                    
                    StatusChanged?.Invoke($"Array {i + 1}: Sent {dataArray.Length} packets, received {responses.Count} responses");
                    
                    // Wait 150ms before sending next array (except for the last one)
                    if (i < dataArrays.Count - 1)
                    {
                        await Task.Delay(150);
                    }
                }

                StatusChanged?.Invoke($"Completed transmission of all arrays. Total response lists: {responseLists.Count}");
                return responseLists;
            }
            catch (Exception ex)
            {
                StatusChanged?.Invoke($"Error sending multiple data arrays: {ex.Message}");
                return responseLists;
            }
        }

        /// <summary>
        /// Helper method to wait for a specific number of responses
        /// </summary>
        private async Task<List<byte[]>> WaitForResponsesAsync(int expectedCount, TimeSpan timeout)
        {
            var responses = new List<byte[]>();
            var stopwatch = Stopwatch.StartNew();

            StatusChanged?.Invoke($"Waiting for {expectedCount} responses (timeout: {timeout.TotalSeconds}s)");

            while (responses.Count < expectedCount && stopwatch.Elapsed < timeout && _isConnected)
            {
                if (_receivedDataQueue.TryDequeue(out byte[] receivedData))
                {
                    responses.Add(receivedData);
                    var hexString = BitConverter.ToString(receivedData).Replace("-", " ");
                    Debug.WriteLine($"Response {responses.Count}/{expectedCount}: {hexString}");
                    
                    // Report progress every 5 responses or at completion
                    if (responses.Count % 5 == 0 || responses.Count == expectedCount)
                    {
                        StatusChanged?.Invoke($"Received {responses.Count}/{expectedCount} responses");
                    }
                }
                else
                {
                    await Task.Delay(10); // Small delay to prevent tight loop
                }
            }

            if (responses.Count < expectedCount)
            {
                StatusChanged?.Invoke($"Response timeout: received {responses.Count}/{expectedCount} responses in {stopwatch.Elapsed.TotalSeconds:F1}s");
            }
            else
            {
                StatusChanged?.Invoke($"Successfully received all {responses.Count} responses in {stopwatch.Elapsed.TotalSeconds:F1}s");
            }

            return responses;
        }

        /// <summary>
        /// Get all received response lists
        /// </summary>
        public List<List<byte[]>> GetReceivedLists()
        {
            lock (_receivedListsLock)
            {
                return new List<List<byte[]>>(_receivedLists);
            }
        }

        private void StartTransmissionSystem()
        {
            _transmissionCancellation = new CancellationTokenSource();
            
            // Start transmission timer
            _transmissionTimer = new Timer(TransmissionCallback, null, 0, TRANSMISSION_INTERVAL_MS);
            
            StatusChanged?.Invoke("Transmission system started");
        }

        private async void TransmissionCallback(object state)
        {
            if (!_isConnected || _transmissionCancellation.Token.IsCancellationRequested)
                return;

            lock (_transmissionLock)
            {
                if (_isTransmitting)
                    return; // Skip if previous transmission is still in progress
                    
                _isTransmitting = true;
                TransmissionStatus?.Invoke(true);
            }

            try
            {
                if (_dataQueue.TryDequeue(out byte[] data))
                {
                    await SendDataDirectlyAsync(data);
                    QueueStatus?.Invoke(_dataQueue.Count);
                    
                    // Wait for response
                    await WaitForResponseAsync();
                }
            }
            catch (Exception ex)
            {
                StatusChanged?.Invoke($"Transmission error: {ex.Message}");
            }
            finally
            {
                lock (_transmissionLock)
                {
                    _isTransmitting = false;
                    TransmissionStatus?.Invoke(false);
                }
            }
        }        private async Task<bool> SendDataDirectlyAsync(byte[] data)
        {
            try
            {
                if (_bluetoothStream == null || !_bluetoothStream.CanWrite)
                    return false;

                // SIMPLE: Write data and flush immediately
                await _bluetoothStream.WriteAsync(data, 0, data.Length);
                await _bluetoothStream.FlushAsync();
                
                return true;
            }
            catch (Exception ex)
            {
                StatusChanged?.Invoke($"Error sending data: {ex.Message}");
                _isConnected = false;
                return false;
            }
        }

        private async Task WaitForResponseAsync()
        {
            var timeout = DateTime.Now.AddMilliseconds(RESPONSE_TIMEOUT_MS);
            
            while (DateTime.Now < timeout && _isConnected)
            {
                if (_receivedDataQueue.TryDequeue(out byte[] receivedData))
                {
                    var hexString = BitConverter.ToString(receivedData).Replace("-", " ");
                    Debug.WriteLine($"Received response: {hexString}");
                    StatusChanged?.Invoke($"Received: {hexString}");
                    DataReceived?.Invoke(receivedData);
                    return;
                }
                
                await Task.Delay(10); // Check every 10ms
            }
              // Timeout occurred
            StatusChanged?.Invoke("Response timeout - no data received within 500ms (115200 baud)");
        }        private void StartDataReading()
        {
            Task.Run(async () =>
            {
                var buffer = new byte[256]; // Smaller buffer for HC-05
                var dataBuffer = new List<byte>();
                
                StatusChanged?.Invoke("Started data reading - listening for robot data (115200 baud optimized)...");
                
                while (_isConnected && (_transmissionCancellation?.Token.IsCancellationRequested != true))
                {
                    try
                    {
                        // Check connection health periodically
                        if (!IsConnectionHealthy())
                        {
                            StatusChanged?.Invoke("Connection health check failed - stopping data reading");
                            _isConnected = false;
                            break;
                        }
                        
                        if (_bluetoothStream != null && _bluetoothStream.CanRead)
                        {
                            // Try a more conservative approach for HC-05
                            int bytesRead = 0;
                            
                            try
                            {
                                // Create a new cancellation token if needed
                                var cancellationToken = _transmissionCancellation?.Token ?? CancellationToken.None;
                                
                                // Use longer timeout when we might expect robot responses
                                int readTimeout = _dataQueue.Count > 0 ? 3000 : READ_TIMEOUT_MS; // 3s if commands pending
                                
                                // Method 1: Simple async read with adaptive timeout for HC-05
                                var readTask = _bluetoothStream.ReadAsync(buffer, 0, buffer.Length, cancellationToken);
                                var timeoutTask = Task.Delay(readTimeout, cancellationToken);
                                
                                var completedTask = await Task.WhenAny(readTask, timeoutTask);
                                
                                if (completedTask == readTask && !readTask.IsFaulted && !readTask.IsCanceled)
                                {
                                    bytesRead = await readTask;
                                    if (bytesRead > 0)
                                    {
                                        StatusChanged?.Invoke($"📡 Successfully read {bytesRead} bytes from robot");
                                    }
                                }
                                else if (completedTask == timeoutTask)
                                {
                                    // Timeout occurred - log it for diagnosis
                                    if (_dataQueue.Count > 0)
                                    {
                                        StatusChanged?.Invoke($"⏱️ Read timeout ({readTimeout}ms) - commands pending but no robot response");
                                    }
                                    // Don't log timeouts when no commands are pending (normal idle state)
                                }
                                else if (readTask.IsFaulted)
                                {
                                    // Don't try sync read if async failed - just log and continue
                                    StatusChanged?.Invoke($"❌ Async read error: {readTask.Exception?.GetBaseException()?.Message}");
                                    if (_isConnected) // Only delay if still connected
                                    {
                                        await Task.Delay(1000, cancellationToken);
                                    }
                                }
                                else if (readTask.IsCanceled)
                                {
                                    StatusChanged?.Invoke("🚫 Read operation cancelled");
                                    break;
                                }
                            }
                            catch (OperationCanceledException)
                            {
                                StatusChanged?.Invoke("Data reading cancelled");
                                break;
                            }
                            catch (Exception ex)
                            {
                                StatusChanged?.Invoke($"Read error: {ex.Message}");
                                if (_isConnected) // Only delay if still connected
                                {
                                    await Task.Delay(1000);
                                }
                            }
                            
                            if (bytesRead > 0)
                            {
                                dataBuffer.AddRange(buffer.Take(bytesRead));
                                
                                // Show all raw received data with timestamp
                                var timestamp = DateTime.Now.ToString("HH:mm:ss.fff");
                                var rawHex = BitConverter.ToString(buffer, 0, bytesRead).Replace("-", " ");
                                Debug.WriteLine($"[{timestamp}] Robot data received ({bytesRead} bytes): {rawHex}");
                                StatusChanged?.Invoke($"[{timestamp}] 🤖 ROBOT DATA: {bytesRead} bytes - {rawHex}");
                                
                                if (_isListenOnlyMode)
                                {
                                    // In listen-only mode, show everything and stream all data
                                    StatusChanged?.Invoke($"LISTEN: Raw bytes: {rawHex}");
                                    StatusChanged?.Invoke($"LISTEN: Buffer now has {dataBuffer.Count} total bytes");
                                    
                                    // Send ALL data as byte stream (same as normal mode)
                                    if (dataBuffer.Count > 0)
                                    {
                                        var allData = dataBuffer.ToArray();
                                        dataBuffer.Clear();
                                        
                                        var streamHex = BitConverter.ToString(allData).Replace("-", " ");
                                        StatusChanged?.Invoke($"LISTEN: Streaming {allData.Length} bytes: {streamHex}");
                                        DataReceived?.Invoke(allData);
                                    }
                                }
                                else
                                {
                                    // Normal mode processing
                                    ProcessNormalModeData(dataBuffer);
                                }
                            }
                            else
                            {
                                // No data received this cycle - only log periodically (every 30 seconds) to avoid spam
                                if (DateTime.Now.Second % 30 == 0 && DateTime.Now.Millisecond < 500)
                                {
                                    var queueInfo = _dataQueue.Count > 0 ? $" ({_dataQueue.Count} commands pending)" : "";
                                    StatusChanged?.Invoke($"🔍 Still listening for robot responses{queueInfo}... CanRead={_bluetoothStream.CanRead}");
                                }
                            }
                        }
                        else
                        {
                            StatusChanged?.Invoke("Stream not available for reading");
                        }
                        
                        // Only delay if still connected
                        if (_isConnected)
                        {
                            await Task.Delay(500); // Don't use cancellation token for this delay
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        StatusChanged?.Invoke("Data reading operation cancelled");
                        break;
                    }
                    catch (Exception ex)
                    {
                        StatusChanged?.Invoke($"Error reading robot data: {ex.Message}");
                        Debug.WriteLine($"Full exception: {ex}");
                        
                        // Check if it's a connection-related error
                        if (ex is IOException || ex is ObjectDisposedException || ex.Message.Contains("timeout"))
                        {
                            StatusChanged?.Invoke("Connection issue detected - stream may be unstable");
                            
                            // Check if we're still officially connected
                            if (_bluetoothClient?.Connected == false)
                            {
                                StatusChanged?.Invoke("Bluetooth client disconnected - stopping data reading");
                                _isConnected = false;
                                break;
                            }
                        }
                        
                        // Only delay if still connected
                        if (_isConnected)
                        {
                            await Task.Delay(2000); // Don't use cancellation token for error delays
                        }
                    }
                }
                
                StatusChanged?.Invoke("Stopped data reading");
            });
        }

        private void ProcessNormalModeData(List<byte> dataBuffer)
        {
            try
            {
                // PURE BYTE STREAMING: Send ALL received data to Scratch immediately
                if (dataBuffer.Count > 0)
                {
                    // Convert entire buffer to byte array and send to Scratch
                    var allData = dataBuffer.ToArray();
                    dataBuffer.Clear(); // Clear buffer after processing
                    
                    // Add to received queue for legacy compatibility
                    _receivedDataQueue.Enqueue(allData);
                    
                    var dataHex = BitConverter.ToString(allData).Replace("-", " ");
                    StatusChanged?.Invoke($"Streaming {allData.Length} bytes to Scratch: {dataHex}");
                    
                    // Fire event to Form1 with complete byte stream
                    DataReceived?.Invoke(allData);
                }
            }
            catch (Exception ex)
            {
                StatusChanged?.Invoke($"Error processing byte stream: {ex.Message}");
            }
        }

        /// <summary>
        /// Get all received data packets
        /// </summary>
        public List<byte[]> GetReceivedData()
        {
            var receivedData = new List<byte[]>();
            while (_receivedDataQueue.TryDequeue(out byte[] data))
            {
                receivedData.Add(data);
            }
            return receivedData;
        }        public async Task DisconnectAsync()
        {
            try
            {
                StatusChanged?.Invoke("Disconnecting...");
                
                // Set disconnected state immediately to stop all loops and operations
                _isConnected = false;
                
                // Stop transmission system first (this should be fast)
                StopTransmissionSystem();
                
                // Close and dispose stream with timeout
                if (_bluetoothStream != null)
                {
                    try
                    {
                        // Use a timeout for stream operations
                        var closeStreamTask = Task.Run(() =>
                        {
                            _bluetoothStream.Close();
                            _bluetoothStream.Dispose();
                        });
                        
                        var timeoutTask = Task.Delay(2000); // 2 second timeout
                        var completedTask = await Task.WhenAny(closeStreamTask, timeoutTask);
                        
                        if (completedTask == timeoutTask)
                        {
                            StatusChanged?.Invoke("Stream close timeout - forcing cleanup");
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Error closing stream: {ex.Message}");
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
                        // Only try to close if still connected
                        if (_bluetoothClient.Connected)
                        {
                            var closeClientTask = Task.Run(() => _bluetoothClient.Close());
                            var timeoutTask = Task.Delay(3000); // 3 second timeout
                            var completedTask = await Task.WhenAny(closeClientTask, timeoutTask);
                            
                            if (completedTask == timeoutTask)
                            {
                                StatusChanged?.Invoke("Bluetooth client close timeout - forcing disposal");
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Error closing Bluetooth client: {ex.Message}");
                        StatusChanged?.Invoke($"Bluetooth close error: {ex.Message}");
                    }
                    
                    // Always dispose the client
                    try
                    {
                        var disposeTask = Task.Run(() => _bluetoothClient.Dispose());
                        var timeoutTask = Task.Delay(2000); // 2 second timeout for dispose
                        var completedTask = await Task.WhenAny(disposeTask, timeoutTask);
                        
                        if (completedTask == timeoutTask)
                        {
                            StatusChanged?.Invoke("Bluetooth client dispose timeout - cleanup may be incomplete");
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Error disposing Bluetooth client: {ex.Message}");
                        StatusChanged?.Invoke($"Bluetooth dispose error: {ex.Message}");
                    }
                    finally
                    {
                        _bluetoothClient = null;
                    }
                }

                var deviceName = _connectedDevice?.DeviceName ?? "device";
                _connectedDevice = null;
                
                // Short delay for final cleanup
                await Task.Delay(100);
                
                StatusChanged?.Invoke($"Disconnected from {deviceName}");
            }
            catch (Exception ex)
            {
                StatusChanged?.Invoke($"Error during disconnect: {ex.Message}");
                Debug.WriteLine($"Disconnect error: {ex}");
                
                // Force cleanup even if there was an error
                _isConnected = false;
                _bluetoothStream = null;
                _bluetoothClient = null;
                _connectedDevice = null;
            }
        }        private void StopTransmissionSystem()
        {
            try
            {
                StatusChanged?.Invoke("Stopping transmission system...");
                
                // Stop timers first (should be immediate)
                try
                {
                    _transmissionTimer?.Dispose();
                    _transmissionTimer = null;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error disposing transmission timer: {ex.Message}");
                }
                
                try
                {
                    _continuousTransmissionTimer?.Dispose();
                    _continuousTransmissionTimer = null;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error disposing continuous timer: {ex.Message}");
                }
                
                // Cancel operations (should be immediate)
                try
                {
                    _transmissionCancellation?.Cancel();
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error cancelling transmission: {ex.Message}");
                }
                
                // Dispose cancellation token (should be immediate)
                try
                {
                    _transmissionCancellation?.Dispose();
                    _transmissionCancellation = null;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error disposing cancellation token: {ex.Message}");
                }
                
                // Stop flush timer (should be immediate)
                try
                {
                    _flushTimer?.Stop();
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error stopping flush timer: {ex.Message}");
                }
                
                // Reset mode flags (immediate)
                _isContinuousMode = false;
                _isListenOnlyMode = false;
                
                // Clear queues (should be fast)
                try
                {
                    while (_dataQueue.TryDequeue(out _)) { }
                    while (_receivedDataQueue.TryDequeue(out _)) { }
                    _dataAvailable.Reset();
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error clearing queues: {ex.Message}");
                }
                
                // Update transmission state (immediate)
                try
                {
                    lock (_transmissionLock)
                    {
                        _isTransmitting = false;
                        TransmissionStatus?.Invoke(false);
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error updating transmission status: {ex.Message}");
                }
                
                StatusChanged?.Invoke("Transmission system stopped");
            }
            catch (Exception ex)
            {
                StatusChanged?.Invoke($"Error stopping transmission system: {ex.Message}");
                Debug.WriteLine($"StopTransmissionSystem error: {ex}");
                
                // Force reset even if there were errors
                _isContinuousMode = false;
                _isListenOnlyMode = false;
                _isTransmitting = false;
                _transmissionTimer = null;
                _continuousTransmissionTimer = null;
                _transmissionCancellation = null;
            }
        }

        public void Dispose()
        {
            try
            {
                // Use force disconnect to avoid hanging in Dispose
                ForceDisconnect();
                
                // Dispose remaining resources
                try { _flushTimer?.Dispose(); } catch { }
                try { _dataAvailable?.Dispose(); } catch { }
                try { _responseAvailableSemaphore?.Dispose(); } catch { }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error in Dispose: {ex.Message}");
            }
        }

        /// <summary>
        /// Try to get received data from the queue
        /// </summary>
        public bool TryGetReceivedData(out byte[] data)
        {
            return _receivedDataQueue.TryDequeue(out data);
        }        /// <summary>
        /// Start continuous transmission mode - sends 10 packets every 200ms (Master mode)
        /// TeBot acts as master, sending 82-byte commands to slave robot
        /// </summary>
        public void StartContinuousTransmission()
        {
            if (!_isConnected)
            {
                StatusChanged?.Invoke("Cannot start continuous mode: not connected");
                return;
            }

            if (_isContinuousMode)
            {
                StatusChanged?.Invoke("Continuous transmission already running");
                return;
            }

            // Stop the regular transmission system to avoid conflicts
            _transmissionTimer?.Dispose();
            _transmissionTimer = null;
            
            // Clear any pending packets in the regular queue
            while (_dataQueue.TryDequeue(out _)) { }
            QueueStatus?.Invoke(0);            _isContinuousMode = true;
            _continuousPacketCounter = 0;
            
            // Start continuous transmission timer (one-shot mode, we'll restart it after each cycle)
            _continuousTransmissionTimer = new Timer(ContinuousTransmissionCallback, null, 0, Timeout.Infinite);
            
            StatusChanged?.Invoke("Started MASTER continuous transmission mode (82-byte send + wait for 82-byte response every 500ms)");
        }        /// <summary>
        /// Stop continuous transmission mode
        /// </summary>
        public void StopContinuousTransmission()
        {
            if (!_isContinuousMode)
            {
                StatusChanged?.Invoke("Continuous transmission not running");
                return;
            }

            _isContinuousMode = false;
            _continuousTransmissionTimer?.Dispose();
            _continuousTransmissionTimer = null;
            
            // Restart the regular transmission system
            if (_isConnected && _transmissionTimer == null)
            {
                _transmissionTimer = new Timer(TransmissionCallback, null, 0, TRANSMISSION_INTERVAL_MS);
                StatusChanged?.Invoke("Regular transmission system restarted");
            }            
            StatusChanged?.Invoke("Stopped continuous transmission mode");
        }

        private async void ContinuousTransmissionCallback(object state)
        {
            if (!_isConnected || !_isContinuousMode)
                return;

            var cycleStartTime = DateTime.Now;
            var consecutiveTimeouts = 0;
            const int maxConsecutiveTimeouts = 5; // Stop after 5 consecutive timeouts

            try
            {
                var startTime = DateTime.Now;
                
                // Create 82-byte master command (marker + 10 packets of 8 bytes each)
                var masterCommand = new byte[82]; // 2 bytes marker + 80 bytes data
                masterCommand[0] = 0xAA; // Marker byte 1
                masterCommand[1] = 0x55; // Marker byte 2
                
                // Generate 10 packets directly into the buffer after the marker
                for (int i = 0; i < 10; i++)
                {
                    int bufferOffset = 2 + (i * 8); // Start after marker bytes
                    
                    masterCommand[bufferOffset + 0] = 0x07; // Command identifier
                    masterCommand[bufferOffset + 1] = (byte)(i + 1); // Packet number within list (1-10)
                    masterCommand[bufferOffset + 2] = (byte)((_continuousPacketCounter >> 8) & 0xFF); // High byte of counter
                    masterCommand[bufferOffset + 3] = (byte)(_continuousPacketCounter & 0xFF); // Low byte of counter
                    masterCommand[bufferOffset + 4] = (byte)(i * 10); // Sequence within packet
                    masterCommand[bufferOffset + 5] = 0x55; // Test marker
                    masterCommand[bufferOffset + 6] = 0xBB; // Test marker
                    masterCommand[bufferOffset + 7] = (byte)(255 - (i * 10)); // Checksum-like value
                    
                    _continuousPacketCounter++;
                }

                // STEP 1: Send 82-byte master command
                if (_bluetoothStream != null && _bluetoothStream.CanWrite)
                {
                    StatusChanged?.Invoke($"MASTER: Sending 82-byte command #{_continuousPacketCounter / 10}...");
                    
                    await _bluetoothStream.WriteAsync(masterCommand, 0, masterCommand.Length);
                    await _bluetoothStream.FlushAsync();
                    
                    var markerHex = BitConverter.ToString(masterCommand, 0, 2).Replace("-", " ");
                    Debug.WriteLine($"MASTER sent 82-byte command: Marker=[{markerHex}] at {DateTime.Now:HH:mm:ss.fff}");
                }
                
                // STEP 2: Wait for exactly 82 bytes response (timeout: 220ms - increased for better reliability)
                Debug.WriteLine($"MASTER: Waiting for slave response (timeout: 220ms) at {DateTime.Now:HH:mm:ss.fff}");
                var response = await WaitForSlaveResponse(220);
                
                if (response != null && response.Length == 82)
                {
                    StatusChanged?.Invoke($"✅ MASTER: Received 82-byte slave response");
                    consecutiveTimeouts = 0; // Reset timeout counter on successful response
                    
                    // Process the 82-byte response (marker + 10 packets)
                    var responseMarker = BitConverter.ToString(response, 0, 2).Replace("-", " ");
                    Debug.WriteLine($"SLAVE response received: Marker=[{responseMarker}] at {DateTime.Now:HH:mm:ss.fff}");
                    
                    // Parse response into individual packets and fire event
                    var responsePackets = new List<byte[]>();
                    for (int i = 0; i < 10; i++)
                    {
                        int packetStart = 2 + (i * 8); // Skip 2-byte marker
                        if (packetStart + 8 <= response.Length)
                        {
                            var packet = new byte[8];
                            Array.Copy(response, packetStart, packet, 0, 8);
                            responsePackets.Add(packet);
                        }
                    }
                    
                    // Fire event with the response packets
                    if (responsePackets.Count > 0)
                    {
                        ContinuousResponseReceived?.Invoke(responsePackets);
                    }
                }
                else
                {
                    consecutiveTimeouts++;
                    StatusChanged?.Invoke($"❌ MASTER: No valid 82-byte response from slave (timeout 220ms) - consecutive timeouts: {consecutiveTimeouts}");
                    Debug.WriteLine($"MASTER: Response timeout at {DateTime.Now:HH:mm:ss.fff} - consecutive: {consecutiveTimeouts}");
                    
                    // Stop continuous transmission if too many consecutive timeouts
                    if (consecutiveTimeouts >= maxConsecutiveTimeouts)
                    {
                        StatusChanged?.Invoke($"🔴 MASTER: Stopping continuous transmission after {maxConsecutiveTimeouts} consecutive timeouts");
                        StopContinuousTransmission();
                        return;
                    }
                }
                
                // STEP 3: Ensure we wait until the full 500ms interval is complete
                var elapsed = DateTime.Now - startTime;
                var remainingTime = TimeSpan.FromMilliseconds(500) - elapsed;
                
                if (remainingTime.TotalMilliseconds > 10) // Only wait if there's meaningful time left
                {
                    Debug.WriteLine($"MASTER: Waiting {remainingTime.TotalMilliseconds:F0}ms to complete 500ms interval (elapsed: {elapsed.TotalMilliseconds:F0}ms)");
                    await Task.Delay(remainingTime);
                }
                else if (remainingTime.TotalMilliseconds < 0)
                {
                    Debug.WriteLine($"MASTER: Cycle took {elapsed.TotalMilliseconds:F0}ms - {Math.Abs(remainingTime.TotalMilliseconds):F0}ms over target 500ms");
                }
                
                // STEP 4: Schedule next transmission (if still in continuous mode)
                if (_isConnected && _isContinuousMode && _continuousTransmissionTimer != null)
                {
                    _continuousTransmissionTimer.Change(0, Timeout.Infinite);
                }
            }
            catch (Exception ex)
            {
                StatusChanged?.Invoke($"Error in continuous transmission: {ex.Message}");
                
                // Still try to schedule next transmission if we're still connected
                if (_isConnected && _isContinuousMode && _continuousTransmissionTimer != null)
                {
                    _continuousTransmissionTimer.Change(500, Timeout.Infinite);
                }
            }
        }
        
        
        /// <summary>
        /// Start listen-only mode - just receive and display data from robot
        /// </summary>
        public void StartListenOnlyMode()
        {
            if (!_isConnected)
            {
                StatusChanged?.Invoke("Cannot start listen mode: not connected");
                return;
            }

            if (_isListenOnlyMode)
            {
                StatusChanged?.Invoke("Listen-only mode already running");
                return;
            }

            // Stop all transmission systems
            _transmissionTimer?.Dispose();
            _transmissionTimer = null;
            _continuousTransmissionTimer?.Dispose();
            _continuousTransmissionTimer = null;
            _isContinuousMode = false;
            
            // Clear any pending packets
            while (_dataQueue.TryDequeue(out _)) { }
            while (_receivedDataQueue.TryDequeue(out _)) { }
            QueueStatus?.Invoke(0);

            _isListenOnlyMode = true;
            
            StatusChanged?.Invoke("Started LISTEN-ONLY mode - waiting for robot data...");
        }

        /// <summary>
        /// Stop listen-only mode
        /// </summary>
        public void StopListenOnlyMode()
        {
            if (!_isListenOnlyMode)
            {
                StatusChanged?.Invoke("Listen-only mode not running");
                return;
            }

            _isListenOnlyMode = false;
            
            // Restart the regular transmission system
            if (_isConnected && _transmissionTimer == null)
            {
                _transmissionTimer = new Timer(TransmissionCallback, null, 0, TRANSMISSION_INTERVAL_MS);
                StatusChanged?.Invoke("Regular transmission system restarted");
            }
            
            StatusChanged?.Invoke("Stopped listen-only mode");
        }
        
        /// <summary>
        /// Check if the Bluetooth connection is healthy
        /// </summary>
        private bool IsConnectionHealthy()
        {
            try
            {
                if (!_isConnected || _bluetoothClient == null || _bluetoothStream == null)
                {
                    return false;
                }
                
                // Check if client is still connected
                if (!_bluetoothClient.Connected)
                {
                    StatusChanged?.Invoke("Bluetooth client reports disconnected");
                    return false;
                }
                
                // Check if stream is still available
                if (!_bluetoothStream.CanRead || !_bluetoothStream.CanWrite)
                {
                    StatusChanged?.Invoke("Bluetooth stream is no longer readable/writable");
                    return false;
                }
                
                return true;
            }
            catch (Exception ex)
            {
                StatusChanged?.Invoke($"Connection health check failed: {ex.Message}");
                return false;
            }
        }
        
        /// <summary>
        /// Force immediate disconnect without waiting for graceful cleanup
        /// Use this if regular DisconnectAsync() hangs
        /// </summary>
        public void ForceDisconnect()
        {
            try
            {
                StatusChanged?.Invoke("Force disconnecting...");
                
                // Immediately set disconnected state
                _isConnected = false;
                
                // Force stop transmission system
                _isContinuousMode = false;
                _isListenOnlyMode = false;
                _isTransmitting = false;
                
                // Dispose timers without waiting - use parallel execution for speed
                Task.Run(() =>
                {
                    try { _transmissionTimer?.Dispose(); } catch { }
                    try { _continuousTransmissionTimer?.Dispose(); } catch { }
                    try { _flushTimer?.Stop(); } catch { }
                });
                
                // Cancel operations immediately
                try { _transmissionCancellation?.Cancel(); } catch { }
                try { _transmissionCancellation?.Dispose(); } catch { }
                
                // Force close stream and client in parallel
                Task.Run(() =>
                {
                    try { _bluetoothStream?.Close(); } catch { }
                    try { _bluetoothStream?.Dispose(); } catch { }
                });
                
                Task.Run(() =>
                {
                    try { _bluetoothClient?.Close(); } catch { }
                    try { _bluetoothClient?.Dispose(); } catch { }
                });
                
                // Clear all references immediately
                _bluetoothStream = null;
                _bluetoothClient = null;
                _connectedDevice = null;
                _transmissionCancellation = null;
                _transmissionTimer = null;
                _continuousTransmissionTimer = null;
                
                // Clear queues quickly
                try
                { 
                    while (_dataQueue.TryDequeue(out _)) { }
                    while (_receivedDataQueue.TryDequeue(out _)) { }
                    _dataAvailable.Reset();
                    
                    // Clear any pending semaphore signals
                    while (_responseAvailableSemaphore.CurrentCount > 0)
                    {
                        _responseAvailableSemaphore.Wait(0);
                    }
                } 
                catch { }
                
                StatusChanged?.Invoke("Force disconnect completed");
            }
            catch (Exception ex)
            {
                StatusChanged?.Invoke($"Error in force disconnect: {ex.Message}");
                // Even if force disconnect fails, ensure critical flags are reset
                _isConnected = false;
                _isContinuousMode = false;
                _isListenOnlyMode = false;
                _isTransmitting = false;
            }
        }

        /// <summary>
        /// Detect all available Bluetooth adapters (built-in + external dongles)
        /// </summary>
        public void DetectBluetoothAdapters()
        {
            try
            {
                StatusChanged?.Invoke("Detecting Bluetooth adapters...");
                
                var radios = BluetoothRadio.AllRadios;
                _availableRadios = radios;
                
                StatusChanged?.Invoke($"Found {radios.Length} Bluetooth adapter(s):");
                
                for (int i = 0; i < radios.Length; i++)
                {
                    var radio = radios[i];
                    var adapterInfo = $"[{i + 1}] {radio.Name} - {radio.LocalAddress} " +
                                     $"(Mode: {radio.Mode})";
                    StatusChanged?.Invoke($"  {adapterInfo}");
                    Debug.WriteLine($"Bluetooth Adapter {i + 1}: {adapterInfo}");
                    
                    // Check if this looks like a TP-Link dongle
                    if (IsTPLinkDongle(radio))
                    {
                        StatusChanged?.Invoke($"  ⭐ Detected TP-Link USB dongle: {radio.Name}");
                    }
                }
                
                // Auto-select the best adapter if none is selected
                if (_selectedRadio == null && radios.Length > 0)
                {
                    PreferExternalDongle();
                }
                
                BluetoothAdaptersDiscovered?.Invoke(radios);
            }
            catch (Exception ex)
            {
                StatusChanged?.Invoke($"Error detecting Bluetooth adapters: {ex.Message}");
                Debug.WriteLine($"DetectBluetoothAdapters error: {ex}");
                _availableRadios = new BluetoothRadio[0];
            }
        }

        /// <summary>
        /// Check if a Bluetooth radio appears to be a TP-Link dongle
        /// </summary>
        private bool IsTPLinkDongle(BluetoothRadio radio)
        {
            try
            {
                var name = radio.Name?.ToLower() ?? "";
                var manufacturer = radio.Manufacturer.ToString().ToLower();
                
                // Check for TP-Link specific identifiers
                if (name.Contains("tp-link") || name.Contains("tplink") ||
                    manufacturer.Contains("tp-link") || manufacturer.Contains("tplink"))
                {
                    return true;
                }
                
                // TP-Link dongles often use these chip manufacturers
                if (manufacturer.Contains("realtek") || 
                    manufacturer.Contains("cambridge_silicon_radio") ||
                    manufacturer.Contains("broadcom") ||
                    manufacturer.Contains("csr"))
                {
                    // Additional checks for USB dongles
                    if (name.Contains("usb") || name.Contains("dongle") || name.Contains("external"))
                    {
                        return true;
                    }
                }
                
                // Check for common USB Bluetooth dongle patterns
                if (name.Contains("bluetooth usb") || 
                    name.Contains("usb bluetooth") ||
                    name.Contains("external bluetooth") ||
                    name.Contains("bluetooth dongle"))
                {
                    return true;
                }
                
                return false;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Select a specific Bluetooth adapter by index
        /// </summary>
        public bool SelectBluetoothAdapter(int adapterIndex)
        {
            try
            {
                if (_availableRadios == null || adapterIndex < 0 || adapterIndex >= _availableRadios.Length)
                {
                    StatusChanged?.Invoke($"Invalid adapter index: {adapterIndex}");
                    return false;
                }

                var radio = _availableRadios[adapterIndex];
                
                // Try to enable the adapter if needed
                StatusChanged?.Invoke($"Selecting adapter: {radio.Name}");
                try
                {
                    if (radio.Mode != RadioMode.Connectable)
                    {
                        radio.Mode = RadioMode.Connectable;
                    }
                }
                catch (Exception ex)
                {
                    StatusChanged?.Invoke($"Could not enable adapter {radio.Name}: {ex.Message}");
                    // Continue anyway - some adapters don't allow mode changes
                }

                _selectedRadio = radio;
                
                // Create a new BluetoothClient - we can't specify which adapter directly with 32feet.NET
                // but we can set the selected radio for reference
                _bluetoothClient?.Dispose();
                _bluetoothClient = new BluetoothClient();
                
                var dongleType = IsTPLinkDongle(radio) ? "TP-Link USB" : "Built-in";
                StatusChanged?.Invoke($"✅ Selected {dongleType} Bluetooth adapter: {radio.Name} ({radio.LocalAddress})");
                StatusChanged?.Invoke($"   Mode: {radio.Mode}");
                StatusChanged?.Invoke($"   Manufacturer: {radio.Manufacturer}");
                return true;
            }
            catch (Exception ex)
            {
                StatusChanged?.Invoke($"Error selecting Bluetooth adapter: {ex.Message}");
                Debug.WriteLine($"SelectBluetoothAdapter error: {ex}");
                return false;
            }
        }

        /// <summary>
        /// Auto-detect and prefer external TP-Link dongle over built-in Bluetooth
        /// </summary>
        public bool PreferExternalDongle()
        {
            try
            {
                if (_availableRadios == null || _availableRadios.Length == 0)
                {
                    StatusChanged?.Invoke("No Bluetooth adapters found");
                    return false;
                }

                if (_availableRadios.Length == 1)
                {
                    StatusChanged?.Invoke("Only one Bluetooth adapter found - using it");
                    return SelectBluetoothAdapter(0);
                }

                StatusChanged?.Invoke($"Multiple adapters found ({_availableRadios.Length}), prioritizing external TP-Link dongles...");

                // Priority 1: Look for TP-Link dongles first (highest priority)
                for (int i = 0; i < _availableRadios.Length; i++)
                {
                    var radio = _availableRadios[i];
                    if (IsTPLinkDongle(radio))
                    {
                        var name = radio.Name?.ToLower() ?? "";
                        if (name.Contains("tp-link") || name.Contains("tplink"))
                        {
                            StatusChanged?.Invoke($"� Found TP-Link dongle: {radio.Name} - selecting it as highest priority");
                            return SelectBluetoothAdapter(i);
                        }
                    }
                }

                // Priority 2: Look for any external USB dongles
                for (int i = 0; i < _availableRadios.Length; i++)
                {
                    var radio = _availableRadios[i];
                    if (IsTPLinkDongle(radio))
                    {
                        StatusChanged?.Invoke($"🔍 Found external USB dongle: {radio.Name} - selecting it");
                        return SelectBluetoothAdapter(i);
                    }
                }

                // Priority 3: Look for any USB-related adapters
                for (int i = 0; i < _availableRadios.Length; i++)
                {
                    var radio = _availableRadios[i];
                    var name = radio.Name?.ToLower() ?? "";
                    
                    if (name.Contains("usb") || name.Contains("dongle") || name.Contains("external"))
                    {
                        StatusChanged?.Invoke($"🔍 Found USB adapter: {radio.Name} - selecting it");
                        return SelectBluetoothAdapter(i);
                    }
                }

                // Default to first adapter if no external dongles found
                StatusChanged?.Invoke("No external dongles detected, using first available adapter (likely built-in)");
                return SelectBluetoothAdapter(0);
            }
            catch (Exception ex)
            {
                StatusChanged?.Invoke($"Error preferring external dongle: {ex.Message}");
                Debug.WriteLine($"PreferExternalDongle error: {ex}");
                return false;
            }
        }

        /// <summary>
        /// Get detailed information about all Bluetooth adapters
        /// </summary>
        public List<string> GetBluetoothAdapterDetails()
        {
            var details = new List<string>();
            
            try
            {
                if (_availableRadios == null || _availableRadios.Length == 0)
                {
                    details.Add("No Bluetooth adapters found");
                    return details;
                }

                for (int i = 0; i < _availableRadios.Length; i++)
                {
                    var radio = _availableRadios[i];
                    var isSelected = radio == _selectedRadio ? " ⭐ SELECTED" : "";
                    var isTPLink = IsTPLinkDongle(radio) ? " 🔥 TP-LINK DONGLE" : "";
                    
                    details.Add($"[{i + 1}] {radio.Name}{isSelected}{isTPLink}");
                    details.Add($"    Address: {radio.LocalAddress}");
                    details.Add($"    Manufacturer: {radio.Manufacturer}");
                    details.Add($"    Mode: {radio.Mode}");
                    details.Add($"    Class of Device: {radio.ClassOfDevice}");
                    details.Add($"    LMP Version: {radio.LmpVersion}");
                    details.Add("");
                }
            }
            catch (Exception ex)
            {
                details.Add($"Error getting adapter details: {ex.Message}");
            }
            
            return details;
        }

        /// <summary>
        /// Force refresh adapters and select TP-Link dongle if available
        /// </summary>
        public void RefreshAndSelectTPLinkDongle()
        {
            try
            {
                StatusChanged?.Invoke("🔄 Refreshing Bluetooth adapters...");
                DetectBluetoothAdapters();
                
                if (_availableRadios != null && _availableRadios.Length > 0)
                {
                    // Force re-selection with preference for TP-Link
                    _selectedRadio = null;
                    PreferExternalDongle();
                    
                    var selectedInfo = SelectedAdapterInfo;
                    StatusChanged?.Invoke($"✅ Adapter selection complete: {selectedInfo}");
                }
                else
                {
                    StatusChanged?.Invoke("❌ No Bluetooth adapters found after refresh");
                }
            }
            catch (Exception ex)
            {
                StatusChanged?.Invoke($"Error refreshing adapters: {ex.Message}");
            }
        }

        /// <summary>
        /// Get human-readable adapter information including dongle type
        /// </summary>
        public string GetSelectedAdapterDescription()
        {
            if (_selectedRadio == null)
                return "No adapter selected";
                
            var dongleType = IsTPLinkDongle(_selectedRadio) ? "🔥 TP-Link USB Dongle" : "💻 Built-in Bluetooth";
            return $"{dongleType}: {_selectedRadio.Name} ({_selectedRadio.LocalAddress})";
        }
        
        /// <summary>
        /// Pair with a Bluetooth device using PIN or SSP
        /// </summary>
        public async Task<bool> PairWithDeviceAsync(BluetoothDeviceInfo device, string pin = "1234")
        {
            try
            {
                StatusChanged?.Invoke($"Attempting to pair with {device.DeviceName}...");
                
                // Check if device is already paired
                if (device.Authenticated)
                {
                    StatusChanged?.Invoke($"✅ {device.DeviceName} is already paired");
                    return true;
                }

                // Attempt pairing
                bool pairingResult = false;
                
                await Task.Run(() =>
                {
                    try
                    {
                        // Try pairing with PIN
                        pairingResult = BluetoothSecurity.PairRequest(device.DeviceAddress, pin);
                    }
                    catch (Exception ex)
                    {
                        StatusChanged?.Invoke($"PIN pairing failed: {ex.Message}");
                        
                        // Try without PIN (for SSP devices)
                        try
                        {
                            pairingResult = BluetoothSecurity.PairRequest(device.DeviceAddress, null);
                        }
                        catch (Exception ex2)
                        {
                            StatusChanged?.Invoke($"SSP pairing also failed: {ex2.Message}");
                        }
                    }
                });

                if (pairingResult)
                {
                    StatusChanged?.Invoke($"🎉 Successfully paired with {device.DeviceName}!");
                    
                    // Refresh device to get updated authentication status
                    device.Refresh();
                    
                    return true;
                }
                else
                {
                    StatusChanged?.Invoke($"❌ Failed to pair with {device.DeviceName}");
                    return false;
                }
            }
            catch (Exception ex)
            {
                StatusChanged?.Invoke($"Error during pairing: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Remove pairing with a Bluetooth device
        /// </summary>
        public async Task<bool> UnpairDeviceAsync(BluetoothDeviceInfo device)
        {
            try
            {
                StatusChanged?.Invoke($"Removing pairing with {device.DeviceName}...");
                
                bool result = await Task.Run(() =>
                {
                    try
                    {
                        return BluetoothSecurity.RemoveDevice(device.DeviceAddress);
                    }
                    catch (Exception ex)
                    {
                        StatusChanged?.Invoke($"Unpair error: {ex.Message}");
                        return false;
                    }
                });

                if (result)
                {
                    StatusChanged?.Invoke($"✅ Successfully unpaired {device.DeviceName}");
                    device.Refresh();
                    return true;
                }
                else
                {
                    StatusChanged?.Invoke($"❌ Failed to unpair {device.DeviceName}");
                    return false;
                }
            }
            catch (Exception ex)
            {
                StatusChanged?.Invoke($"Error during unpairing: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Get list of paired devices
        /// </summary>
        public BluetoothDeviceInfo[] GetPairedDevices()
        {
            try
            {
                var pairedDevices = _bluetoothClient.DiscoverDevices(10, true, false, false);
                StatusChanged?.Invoke($"Found {pairedDevices.Length} paired device(s)");
                return pairedDevices;
            }
            catch (Exception ex)
            {
                StatusChanged?.Invoke($"Error getting paired devices: {ex.Message}");
                return new BluetoothDeviceInfo[0];
            }
        }

        /// <summary>
        /// Check if a device is paired and authenticated
        /// </summary>
        public bool IsDevicePaired(BluetoothDeviceInfo device)
        {
            try
            {
                device.Refresh(); // Update authentication status
                return device.Authenticated;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Wait for exactly 82 bytes response from slave device within specified timeout
        /// This method works with the existing data processing pipeline by checking the received queue
        /// </summary>
        /// <param name="timeoutMs">Timeout in milliseconds</param>
        /// <returns>82-byte response array or null if timeout/error</returns>
        private async Task<byte[]> WaitForSlaveResponse(int timeoutMs)
        {
            var startTime = DateTime.Now;
            
            try
            {
                // Wait for the semaphore to be signaled (indicating a complete 82-byte response is available)
                var waitSuccess = await _responseAvailableSemaphore.WaitAsync(timeoutMs);
                
                if (!waitSuccess)
                {
                    var timeoutElapsed = DateTime.Now - startTime;
                    Debug.WriteLine($"WaitForSlaveResponse: Timeout after {timeoutElapsed.TotalMilliseconds:F0}ms - no complete response available");
                    
                    // Check if we have any partial data in the queue that might indicate communication is working
                    var queueCount = _receivedDataQueue.Count;
                    if (queueCount > 0)
                    {
                        Debug.WriteLine($"WaitForSlaveResponse: Queue has {queueCount} packets - partial response detected");
                    }
                    
                    return null;
                }
                
                // If we get here, a complete 82-byte response should be available as 10 packets
                var collectedPackets = new List<byte[]>();
                var maxRetries = 3; // Allow a few retries in case of timing issues
                var retryCount = 0;
                
                // Collect exactly 10 packets to reconstruct the 82-byte response
                while (collectedPackets.Count < 10 && retryCount < maxRetries)
                {
                    for (int i = collectedPackets.Count; i < 10; i++)
                    {
                        if (_receivedDataQueue.TryDequeue(out byte[] packet))
                        {
                            collectedPackets.Add(packet);
                        }
                        else
                        {
                            // This might happen if packets are still being processed - wait a bit
                            if (retryCount < maxRetries - 1)
                            {
                                Debug.WriteLine($"WaitForSlaveResponse: Missing packet {i + 1}/10 - retrying (attempt {retryCount + 1}/{maxRetries})");
                                await Task.Delay(5); // Small delay to allow processing
                                break;
                            }
                            else
                            {
                                Debug.WriteLine($"WaitForSlaveResponse: Missing packet {i + 1}/10 - data integrity issue after {maxRetries} attempts");
                            }
                        }
                    }
                    
                    if (collectedPackets.Count < 10)
                    {
                        retryCount++;
                    }
                }
                
                if (collectedPackets.Count == 10)
                {
                    // Reconstruct the original 82-byte response with AA 55 marker
                    var response = new byte[82];
                    response[0] = 0xAA; // Marker
                    response[1] = 0x55; // Marker
                    
                    // Copy the 10 packets (80 bytes) after the marker
                    for (int i = 0; i < 10; i++)
                    {
                        Array.Copy(collectedPackets[i], 0, response, 2 + (i * 8), 8);
                    }
                    
                    var elapsed = DateTime.Now - startTime;
                    Debug.WriteLine($"WaitForSlaveResponse: Got complete 82-byte response after {elapsed.TotalMilliseconds:F0}ms (retries: {retryCount})");
                    return response;
                }
                else
                {
                    // Put back any packets we collected but couldn't complete the response
                    foreach (var packet in collectedPackets)
                    {
                        _receivedDataQueue.Enqueue(packet);
                    }
                    
                    Debug.WriteLine($"WaitForSlaveResponse: Incomplete response - only got {collectedPackets.Count}/10 packets after {maxRetries} attempts");
                    return null;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"WaitForSlaveResponse: Exception - {ex.Message}");
                return null;
            }
        }
    }
}
