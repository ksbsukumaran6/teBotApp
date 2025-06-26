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
{    public class BluetoothManager
    {
        private BluetoothClient _bluetoothClient;
        private BluetoothDeviceInfo _connectedDevice;
        private Stream _bluetoothStream;
        private bool _isConnected;
        private System.Timers.Timer _flushTimer;
          // Data transmission management
        private readonly ConcurrentQueue<byte[]> _dataQueue = new ConcurrentQueue<byte[]>();
        private readonly ConcurrentQueue<byte[]> _receivedDataQueue = new ConcurrentQueue<byte[]>();
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

        public bool IsConnected => _isConnected;
        public string ConnectedDeviceName => _connectedDevice?.DeviceName ?? "None";
        public int QueuedDataCount => _dataQueue.Count;
        public bool IsTransmitting => _isTransmitting;
        public bool IsContinuousMode => _isContinuousMode;
        public bool IsListenOnlyMode => _isListenOnlyMode;
        public bool IsMasterMode => _isMasterMode;

        public BluetoothManager()
        {
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
                StatusChanged?.Invoke("Scanning for Bluetooth devices...");
                
                var devices = await Task.Run(() =>
                {
                    return _bluetoothClient.DiscoverDevices(10, true, true, false);
                });

                StatusChanged?.Invoke($"Found {devices.Length} Bluetooth devices");
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

                // At 115200 baud, we can send data much faster - no inter-byte delays needed
                await _bluetoothStream.WriteAsync(data, 0, data.Length);
                await _bluetoothStream.FlushAsync();
                
                var hexString = BitConverter.ToString(data).Replace("-", " ");
                Debug.WriteLine($"Sent (115200 baud): {hexString}");
                StatusChanged?.Invoke($"Sent (115200 baud): {hexString}");
                
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
                                
                                // Method 1: Simple async read with timeout for HC-05
                                var readTask = _bluetoothStream.ReadAsync(buffer, 0, buffer.Length, cancellationToken);
                                var timeoutTask = Task.Delay(READ_TIMEOUT_MS, cancellationToken);
                                
                                var completedTask = await Task.WhenAny(readTask, timeoutTask);
                                
                                if (completedTask == readTask && !readTask.IsFaulted && !readTask.IsCanceled)
                                {
                                    bytesRead = await readTask;
                                    if (bytesRead > 0)
                                    {
                                        StatusChanged?.Invoke($"Successfully read {bytesRead} bytes");
                                    }
                                }
                                else if (readTask.IsFaulted)
                                {
                                    // Don't try sync read if async failed - just log and continue
                                    StatusChanged?.Invoke($"Async read error: {readTask.Exception?.GetBaseException()?.Message}");
                                    if (_isConnected) // Only delay if still connected
                                    {
                                        await Task.Delay(1000, cancellationToken);
                                    }
                                }
                                else if (readTask.IsCanceled)
                                {
                                    StatusChanged?.Invoke("Read operation cancelled");
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
                                StatusChanged?.Invoke($"[{timestamp}] DATA RECEIVED: {bytesRead} bytes - {rawHex}");
                                
                                if (_isListenOnlyMode)
                                {
                                    // In listen-only mode, show everything
                                    StatusChanged?.Invoke($"LISTEN: Raw bytes: {rawHex}");
                                    StatusChanged?.Invoke($"LISTEN: Buffer now has {dataBuffer.Count} total bytes");
                                    
                                    // Process as 8-byte packets if possible
                                    int packetsProcessed = 0;
                                    while (dataBuffer.Count >= DATA_PACKET_SIZE)
                                    {
                                        var packet = dataBuffer.Take(DATA_PACKET_SIZE).ToArray();
                                        dataBuffer.RemoveRange(0, DATA_PACKET_SIZE);
                                        packetsProcessed++;
                                        
                                        var packetHex = BitConverter.ToString(packet).Replace("-", " ");
                                        StatusChanged?.Invoke($"LISTEN: Packet #{packetsProcessed}: {packetHex}");
                                        DataReceived?.Invoke(packet);
                                    }
                                    
                                    // Show any remaining bytes
                                    if (dataBuffer.Count > 0)
                                    {
                                        var remainingHex = BitConverter.ToString(dataBuffer.ToArray()).Replace("-", " ");
                                        StatusChanged?.Invoke($"LISTEN: Remaining {dataBuffer.Count} bytes: {remainingHex}");
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
                                // No data received, log periodically (every 10 seconds)
                                if (DateTime.Now.Second % 10 == 0 && DateTime.Now.Millisecond < 500)
                                {
                                    StatusChanged?.Invoke($"Still listening... CanRead={_bluetoothStream.CanRead}, Stream type: {_bluetoothStream.GetType().Name}");
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
                if (_isContinuousMode)
                {
                    // Continuous mode processing - look for 82-byte blocks
                    while (dataBuffer.Count >= 82)
                    {
                        var fullResponse = dataBuffer.Take(82).ToArray();
                        dataBuffer.RemoveRange(0, 82);
                        
                        // Parse the 82-byte response into marker + 10 packets
                        var marker = fullResponse.Take(2).ToArray();
                        var markerHex = BitConverter.ToString(marker).Replace("-", " ");
                        
                        StatusChanged?.Invoke($"=== CONTINUOUS MODE: 82-byte block received ===");
                        StatusChanged?.Invoke($"Marker: {markerHex}");
                        
                        // Extract each 8-byte packet and add to queue
                        for (int i = 0; i < 10; i++)
                        {
                            int packetStart = 2 + (i * 8); // Skip 2-byte marker
                            if (packetStart + 8 <= fullResponse.Length)
                            {
                                var packet = new byte[8];
                                Array.Copy(fullResponse, packetStart, packet, 0, 8);
                                _receivedDataQueue.Enqueue(packet);
                                
                                var packetHex = BitConverter.ToString(packet).Replace("-", " ");
                                StatusChanged?.Invoke($"  Packet {i + 1}: {packetHex}");
                            }
                        }
                        
                        StatusChanged?.Invoke($"=== END 82-byte block ===");
                        
                        var responseHex = BitConverter.ToString(fullResponse, 0, Math.Min(16, fullResponse.Length)).Replace("-", " ");
                        Debug.WriteLine($"Processed 82-byte continuous response: {responseHex}...");
                        break; // Process one 82-byte block at a time
                    }
                    
                    // If we have some data but less than 82 bytes, show what we have
                    if (dataBuffer.Count > 0 && dataBuffer.Count < 82)
                    {
                        var partialHex = BitConverter.ToString(dataBuffer.ToArray()).Replace("-", " ");
                        StatusChanged?.Invoke($"Continuous: Partial data ({dataBuffer.Count} bytes): {partialHex}");
                    }
                }
                else
                {
                    // Regular mode: Check for 82-byte slave responses first, then individual packets
                    while (dataBuffer.Count >= 82)
                    {
                        var fullResponse = dataBuffer.Take(82).ToArray();
                        dataBuffer.RemoveRange(0, 82);
                        
                        // Parse the 82-byte slave response into marker + 10 packets
                        var marker = fullResponse.Take(2).ToArray();
                        var markerHex = BitConverter.ToString(marker).Replace("-", " ");
                        
                        StatusChanged?.Invoke($"=== REGULAR MODE: 82-byte slave response received ===");
                        StatusChanged?.Invoke($"Slave sent marker: {markerHex}");
                        StatusChanged?.Invoke($"Parsing slave response as 10x8-byte packets:");
                        
                        // Extract each 8-byte packet from slave response
                        for (int i = 0; i < 10; i++)
                        {
                            int packetStart = 2 + (i * 8); // Skip 2-byte marker
                            if (packetStart + 8 <= fullResponse.Length)
                            {
                                var packet = new byte[8];
                                Array.Copy(fullResponse, packetStart, packet, 0, 8);
                                _receivedDataQueue.Enqueue(packet);
                                
                                var packetHex = BitConverter.ToString(packet).Replace("-", " ");
                                StatusChanged?.Invoke($"  Slave packet {i + 1}: {packetHex}");
                                Debug.WriteLine($"Regular mode slave packet {i + 1}/10: {packetHex}");
                            }
                        }
                        
                        StatusChanged?.Invoke($"=== END slave response (82 bytes) ===");
                        break; // Process one 82-byte block at a time
                    }
                    
                    // If no complete 82-byte blocks, process individual 8-byte packets
                    if (dataBuffer.Count >= DATA_PACKET_SIZE && dataBuffer.Count < 82)
                    {
                        while (dataBuffer.Count >= DATA_PACKET_SIZE)
                        {
                            var packet = dataBuffer.Take(DATA_PACKET_SIZE).ToArray();
                            dataBuffer.RemoveRange(0, DATA_PACKET_SIZE);
                            _receivedDataQueue.Enqueue(packet);
                            
                            var packetHex = BitConverter.ToString(packet).Replace("-", " ");
                            StatusChanged?.Invoke($"Regular: Individual packet: {packetHex}");
                        }
                    }
                    
                    // Show any remaining partial data
                    if (dataBuffer.Count > 0 && dataBuffer.Count < DATA_PACKET_SIZE)
                    {
                        var partialHex = BitConverter.ToString(dataBuffer.ToArray()).Replace("-", " ");
                        StatusChanged?.Invoke($"Regular: Partial data ({dataBuffer.Count} bytes): {partialHex}");
                    }
                }
            }
            catch (Exception ex)
            {
                StatusChanged?.Invoke($"Error processing normal mode data: {ex.Message}");
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
            
            // Start continuous transmission timer (250ms interval for robot processing time)
            _continuousTransmissionTimer = new Timer(ContinuousTransmissionCallback, null, 0, CONTINUOUS_TRANSMISSION_INTERVAL_MS);
            
            StatusChanged?.Invoke("Started MASTER continuous transmission mode (marker + 10 packets every 200ms, optimized for 115200 baud)");
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
        }        private async void ContinuousTransmissionCallback(object state)
        {
            if (!_isConnected || !_isContinuousMode)
                return;

            try
            {
                // Create a single buffer containing marker + all 80 bytes (10 packets)
                // This ensures atomic transmission and prevents marker bytes from being lost
                var totalBuffer = new byte[82]; // 2 bytes marker + 80 bytes data
                totalBuffer[0] = 0xAA; // Marker byte 1
                totalBuffer[1] = 0x55; // Marker byte 2
                
                // Generate 10 packets directly into the buffer after the marker
                for (int i = 0; i < 10; i++)
                {
                    int bufferOffset = 2 + (i * 8); // Start after marker bytes
                    
                    totalBuffer[bufferOffset + 0] = 0x07; // Command identifier
                    totalBuffer[bufferOffset + 1] = (byte)(i + 1); // Packet number within list (1-10)
                    totalBuffer[bufferOffset + 2] = (byte)((_continuousPacketCounter >> 8) & 0xFF); // High byte of counter
                    totalBuffer[bufferOffset + 3] = (byte)(_continuousPacketCounter & 0xFF); // Low byte of counter
                    totalBuffer[bufferOffset + 4] = (byte)(i * 10); // Sequence within packet
                    totalBuffer[bufferOffset + 5] = 0x55; // Test marker
                    totalBuffer[bufferOffset + 6] = 0xBB; // Test marker
                    totalBuffer[bufferOffset + 7] = (byte)(255 - (i * 10)); // Checksum-like value
                    
                    _continuousPacketCounter++;
                }                // Send master command at 115200 baud - much faster than 9600
                if (_bluetoothStream != null && _bluetoothStream.CanWrite)
                {
                    StatusChanged?.Invoke($"MASTER: Sending 82-byte command to slave at 115200 baud...");
                    
                    // At 115200 baud, send entire buffer at once for maximum speed
                    await _bluetoothStream.WriteAsync(totalBuffer, 0, totalBuffer.Length);
                    await _bluetoothStream.FlushAsync();
                    
                    var markerHex = BitConverter.ToString(totalBuffer, 0, 2).Replace("-", " ");
                    var firstPacketHex = BitConverter.ToString(totalBuffer, 2, 8).Replace("-", " ");
                    Debug.WriteLine($"MASTER sent 82-byte command: Marker=[{markerHex}] FirstPacket=[{firstPacketHex}] ...");
                    StatusChanged?.Invoke($"MASTER: Sent command #{_continuousPacketCounter / 10} to slave (Marker: {markerHex}, 82 bytes total)");
                }
                  // Start collecting responses for this batch - much faster at 115200 baud
                _ = Task.Run(() => CollectContinuousResponses(10));
            }
            catch (Exception ex)
            {
                StatusChanged?.Invoke($"Error in continuous transmission: {ex.Message}");
            }
        }        private async Task CollectContinuousResponses(int expectedCount)
        {
            var responses = new List<byte[]>();
            var timeout = DateTime.Now.AddMilliseconds(150); // Much shorter timeout for 115200 baud (was 800ms)
            var allReceivedBytes = new List<byte>();
            
            while (responses.Count < expectedCount && DateTime.Now < timeout && _isConnected && _isContinuousMode)
            {
                if (_receivedDataQueue.TryDequeue(out byte[] receivedData))
                {
                    allReceivedBytes.AddRange(receivedData);
                    
                    // Show each received chunk in debug
                    var hexString = BitConverter.ToString(receivedData).Replace("-", " ");
                    Debug.WriteLine($"Continuous chunk {allReceivedBytes.Count} bytes received: {hexString}");
                    StatusChanged?.Invoke($"Continuous: Received chunk ({receivedData.Length} bytes): {hexString}");
                    
                    // Check if we have received a complete 82-byte response (2-byte marker + 80 bytes data)
                    if (allReceivedBytes.Count >= 82)
                    {
                        var fullResponse = allReceivedBytes.Take(82).ToArray();
                        allReceivedBytes.RemoveRange(0, 82);
                        
                        // Parse the 82-byte response into marker + 10 packets
                        var marker = fullResponse.Take(2).ToArray();
                        var markerHex = BitConverter.ToString(marker).Replace("-", " ");
                        
                        StatusChanged?.Invoke($"=== CONTINUOUS RESPONSE BLOCK (82 bytes) ===");
                        StatusChanged?.Invoke($"Marker: {markerHex}");
                        StatusChanged?.Invoke($"Data packets (10 x 8 bytes):");
                        
                        // Extract and display each 8-byte packet
                        for (int i = 0; i < 10; i++)
                        {
                            int packetStart = 2 + (i * 8); // Skip 2-byte marker
                            if (packetStart + 8 <= fullResponse.Length)
                            {
                                var packet = new byte[8];
                                Array.Copy(fullResponse, packetStart, packet, 0, 8);
                                responses.Add(packet);
                                
                                var packetHex = BitConverter.ToString(packet).Replace("-", " ");
                                StatusChanged?.Invoke($"  Packet {i + 1}: {packetHex}");
                                Debug.WriteLine($"Continuous packet {i + 1}/10: {packetHex}");
                            }
                        }
                        
                        StatusChanged?.Invoke($"=== END RESPONSE BLOCK ===");
                        break; // We've processed a complete 82-byte block
                    }
                    else
                    {
                        // Still collecting bytes for the 82-byte block
                        StatusChanged?.Invoke($"Continuous: Collecting... {allReceivedBytes.Count}/82 bytes received");
                    }
                }
                else
                {
                    await Task.Delay(5); // Shorter delay for 115200 baud
                }
            }
            
            // Fire event with collected responses and show summary
            if (responses.Count > 0)
            {
                ContinuousResponseReceived?.Invoke(responses);
                var totalBytes = responses.Sum(r => r.Length);
                StatusChanged?.Invoke($"Continuous: Successfully parsed {responses.Count}/10 packets ({totalBytes} bytes total) at 115200 baud");
            }
            else if (allReceivedBytes.Count > 0)
            {
                // We received some data but not a complete 82-byte block
                var partialHex = BitConverter.ToString(allReceivedBytes.ToArray()).Replace("-", " ");
                StatusChanged?.Invoke($"Continuous: Incomplete response - received {allReceivedBytes.Count} bytes: {partialHex}");
            }
            else
            {
                StatusChanged?.Invoke($"Continuous: No responses received in 150ms window (115200 baud)");
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
                
                // Dispose timers without waiting
                try { _transmissionTimer?.Dispose(); } catch { }
                try { _continuousTransmissionTimer?.Dispose(); } catch { }
                try { _flushTimer?.Stop(); } catch { }
                
                // Cancel operations
                try { _transmissionCancellation?.Cancel(); } catch { }
                try { _transmissionCancellation?.Dispose(); } catch { }
                
                // Force close stream
                try { _bluetoothStream?.Close(); } catch { }
                try { _bluetoothStream?.Dispose(); } catch { }
                
                // Force close and dispose client
                try { _bluetoothClient?.Close(); } catch { }
                try { _bluetoothClient?.Dispose(); } catch { }
                
                // Clear all references
                _bluetoothStream = null;
                _bluetoothClient = null;
                _connectedDevice = null;
                _transmissionCancellation = null;
                _transmissionTimer = null;
                _continuousTransmissionTimer = null;
                
                // Clear queues
                try
                { 
                    while (_dataQueue.TryDequeue(out _)) { }
                    while (_receivedDataQueue.TryDequeue(out _)) { }
                    _dataAvailable.Reset();
                } 
                catch { }
                
                StatusChanged?.Invoke("Force disconnect completed");
            }
            catch (Exception ex)
            {
                StatusChanged?.Invoke($"Error in force disconnect: {ex.Message}");
            }
        }
    }
}
