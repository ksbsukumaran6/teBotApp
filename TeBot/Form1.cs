using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using InTheHand.Net.Sockets;

namespace TeBot
{    public partial class Form1 : Form
    {
        private WebSocketDataServer _webSocketServer;
        private BluetoothManager _bluetoothManager;
        private int _dataPacketsSent = 0;
        private int _continuousResponseCount = 0;
        private volatile bool _isProcessingCommand = false;

        public Form1()
        {
            InitializeComponent();
            InitializeComponents();
        }        private void InitializeComponents()
        {
            // Initialize WebSocket server
            _webSocketServer = new WebSocketDataServer();
            _webSocketServer.DataReceived += OnDataReceived;
            
            // Subscribe to WebSocket connection events
            DataReceiver.SessionConnected += OnScratchConnected;
            DataReceiver.SessionDisconnected += OnScratchDisconnected;            // Initialize Bluetooth manager
            _bluetoothManager = new BluetoothManager();
            _bluetoothManager.StatusChanged += OnBluetoothStatusChanged;
            _bluetoothManager.DevicesDiscovered += OnDevicesDiscovered;
            _bluetoothManager.QueueStatus += OnQueueStatusChanged;
            _bluetoothManager.TransmissionStatus += OnTransmissionStatusChanged;
            _bluetoothManager.ContinuousResponseReceived += OnContinuousResponseReceived;
            _bluetoothManager.BluetoothAdaptersDiscovered += OnBluetoothAdaptersDiscovered;

            // Initialize adapter UI
            LoadBluetoothAdapters();
            
            UpdateStatus("Application started. Click 'Start Server' to begin receiving WebSocket data.");
            
            // Show initial adapter selection
            UpdateAdapterSelectionUI();
        
        }

        private void OnQueueStatusChanged(int queueCount)
        {
            UpdateStatus($"Queue status: {queueCount} packets waiting");
        }

        private void OnTransmissionStatusChanged(bool isTransmitting)
        {
            UpdateStatus($"Transmission: {(isTransmitting ? "Active" : "Idle")}");
        }        private async void OnDataReceived(byte[] data)
        {
            try
            {
                if (_bluetoothManager.IsConnected)
                {
                    UpdateStatus($"📨 Received {data.Length} bytes from Scratch - processing...");
                    
                    // Support multiple data formats
                    if (data.Length == 8)
                    {
                        // New 8-byte command format
                        await ProcessScratch8ByteCommand(data);
                    }
                    else if (data.Length == 80 && data[0] == 0xAA && data[1] == 0x55)
                    {
                        // 80-byte packet with headers
                        await ProcessScratch80ByteProtocol(data);
                    }
                    else if (data.Length == 80)
                    {
                        // Legacy 80-byte format without headers
                        await ProcessScratchCommand80Bytes(data);
                    }
                    else if (data.Length > 80 && IsJsonData(data))
                    {
                        // JSON format
                        await ProcessScratchCommandJson(data);
                    }
                    else
                    {
                        UpdateStatus($"❌ Invalid data format from Scratch. Expected 8 or 80 bytes, or JSON. Received: {data.Length} bytes");
                        await SendResponseToScratch(new List<byte[]>(), false, "Invalid data format");
                    }
                }
                else
                {
                    UpdateStatus("❌ Received Scratch command but robot not connected");
                    await SendResponseToScratch(new List<byte[]>(), false, "Robot not connected");
                }
            }
            catch (Exception ex)
            {
                UpdateStatus($"❌ Error processing Scratch data: {ex.Message}");
                await SendResponseToScratch(new List<byte[]>(), false, $"Processing error: {ex.Message}");
            }
        }

        private async Task ProcessScratch8ByteCommand(byte[] data)
        {
            // Prevent overlapping command processing to avoid buffer corruption
            if (_isProcessingCommand)
            {
                UpdateStatus($"⚠️ Dropping command - already processing another command (prevents buffer corruption)");
                await SendRawResponseToScratch(new byte[8]); // Send zeros for dropped command
                return;
            }

            _isProcessingCommand = true;
            try
            {
                UpdateStatus($"🔄 Processing 8-byte command from Scratch...");
                
                // Validate 8-byte command
                if (data.Length != 8)
                {
                    throw new ArgumentException($"Expected 8 bytes, received {data.Length} bytes");
                }
                
                // Log the exact command being sent (no offset, no shifting)
                var hexString = BitConverter.ToString(data).Replace("-", " ");
                UpdateStatus($"📋 Sending exact 8 bytes to robot: {hexString}");
                
                // Send the EXACT same 8 bytes to robot immediately (no modifications)
                bool success = await _bluetoothManager.SendDataImmediately(data);
                
                if (success)
                {
                    UpdateStatus($"✅ Command sent to robot successfully");
                    
                    // Wait for robot to respond with 8 bytes
                    var responses = await WaitForResponsesWithTimeout(1, 1000);
                    
                    if (responses.Count > 0)
                    {
                        var robotResponse = responses[0];
                        var robotHex = BitConverter.ToString(robotResponse).Replace("-", " ");
                        UpdateStatus($"🤖 Robot responded: {robotHex}");
                        
                        // Send the robot's response back to Scratch
                        await SendRawResponseToScratch(robotResponse);
                        
                        UpdateStatus($"📤 Sent robot response back to Scratch");
                    }
                    else
                    {
                        UpdateStatus($"⚠️ No response from robot within timeout");
                        await SendRawResponseToScratch(new byte[8]); // Send 8 zeros for no response
                    }
                }
                else
                {
                    UpdateStatus($"❌ Failed to send command to robot");
                    await SendRawResponseToScratch(new byte[8]); // Send 8 zeros for send failure
                }
            }
            catch (Exception ex)
            {
                UpdateStatus($"❌ Error processing 8-byte command: {ex.Message}");
                await SendRawResponseToScratch(new byte[8]); // Send 8 zeros for error
            }
            finally
            {
                _isProcessingCommand = false;
            }
        }
        
        private byte[] CreatePaddedResponse(byte[] robotResponse)
        {
            // Create 80-byte response by repeating the 8-byte robot response 10 times
            var paddedResponse = new byte[80];
            for (int i = 0; i < 10; i++)
            {
                Array.Copy(robotResponse, 0, paddedResponse, i * 8, Math.Min(8, robotResponse.Length));
            }
            return paddedResponse;
        }

        private async Task ProcessScratchCommand80Bytes(byte[] data)
        {
            try
            {
                // Split 80 bytes into 10 packets of 8 bytes each
                var packets = new byte[10][];
                for (int i = 0; i < 10; i++)
                {
                    packets[i] = new byte[8];
                    Array.Copy(data, i * 8, packets[i], 0, 8);
                }

                UpdateStatus($"🔄 Sending 10 packets (80 bytes) to robot...");
                
                // Send to robot and wait for response
                bool success = await _bluetoothManager.SendDataArrayAsync(packets);
                
                if (success)
                {
                    // Wait for robot response (10 packets back)
                    var responses = await WaitForResponsesWithTimeout(10, 2000);
                    
                    UpdateStatus($"✅ Robot responded: {responses.Count}/10 packets received");
                    
                    // Send response back to Scratch
                    await SendResponseToScratch(responses, true, "Success");
                    
                    // Update counters
                    Interlocked.Increment(ref _dataPacketsSent);
                    BeginInvoke(new Action(() =>
                    {
                        lblDataCount.Text = $"Scratch commands processed: {_dataPacketsSent}";
                    }));
                }
                else
                {
                    UpdateStatus("❌ Failed to send data to robot");
                    await SendResponseToScratch(new List<byte[]>(), false, "Robot send failed");
                }
            }
            catch (Exception ex)
            {
                UpdateStatus($"❌ Error in 80-byte processing: {ex.Message}");
                await SendResponseToScratch(new List<byte[]>(), false, $"80-byte processing error: {ex.Message}");
            }
        }

        private async Task ProcessScratchCommandJson(byte[] data)
        {
            try
            {
                string jsonString = Encoding.UTF8.GetString(data);
                UpdateStatus($"📋 Processing JSON command from Scratch...");
                
                // Simple JSON parsing (you can use System.Text.Json for more robust parsing)
                if (jsonString.Contains("\"command\":\"send_data\"") && jsonString.Contains("\"data\":["))
                {
                    // Extract data array from JSON (simplified parsing)
                    var dataStart = jsonString.IndexOf("\"data\":[") + 8;
                    var dataEnd = jsonString.IndexOf("]", dataStart);
                    var dataArrayStr = jsonString.Substring(dataStart, dataEnd - dataStart);
                    
                    var byteValues = dataArrayStr.Split(',').Select(s => byte.Parse(s.Trim())).ToArray();
                    
                    if (byteValues.Length == 80)
                    {
                        await ProcessScratchCommand80Bytes(byteValues);
                    }
                    else
                    {
                        UpdateStatus($"❌ JSON data array must be exactly 80 bytes. Received: {byteValues.Length}");
                        await SendResponseToScratch(new List<byte[]>(), false, $"Invalid data size: {byteValues.Length}");
                    }
                }
                else
                {
                    UpdateStatus("❌ Invalid JSON format from Scratch");
                    await SendResponseToScratch(new List<byte[]>(), false, "Invalid JSON format");
                }
            }
            catch (Exception ex)
            {
                UpdateStatus($"❌ Error parsing JSON: {ex.Message}");
                await SendResponseToScratch(new List<byte[]>(), false, $"JSON parsing error: {ex.Message}");
            }
        }

        private bool IsJsonData(byte[] data)
        {
            try
            {
                string text = Encoding.UTF8.GetString(data);
                return text.TrimStart().StartsWith("{") && text.TrimEnd().EndsWith("}");
            }
            catch
            {
                return false;
            }
        }

        private async Task SendResponseToScratch(List<byte[]> robotResponses, bool success, string message)
        {
            try
            {
                byte[] responseData;
                
                if (robotResponses.Count > 0)
                {
                    // Convert responses back to 80-byte format
                    responseData = new byte[80];
                    for (int i = 0; i < Math.Min(robotResponses.Count, 10); i++)
                    {
                        Array.Copy(robotResponses[i], 0, responseData, i * 8, Math.Min(robotResponses[i].Length, 8));
                    }
                    
                    UpdateStatus($"📤 Sending 80 bytes back to Scratch (from {robotResponses.Count} robot packets)");
                }
                else
                {
                    // Send zeros if no response
                    responseData = new byte[80];
                    UpdateStatus($"📤 Sending empty response to Scratch: {message}");
                }
                
                // Send response back through WebSocket
                await _webSocketServer.SendToAllClientsAsync(responseData);
                
                UpdateStatus($"✅ Response sent to Scratch: Success={success}, {message}");
            }
            catch (Exception ex)
            {
                UpdateStatus($"❌ Error sending response to Scratch: {ex.Message}");
            }
        }

        private async Task SendRawResponseToScratch(byte[] responseData)
        {
            try
            {
                // Ensure we have exactly 8 bytes
                byte[] rawResponse = new byte[8];
                if (responseData != null && responseData.Length > 0)
                {
                    Array.Copy(responseData, 0, rawResponse, 0, Math.Min(responseData.Length, 8));
                }
                
                UpdateStatus($"📤 Sending raw 8-byte response to Scratch");
                
                // Send the raw 8-byte response directly through WebSocket
                await _webSocketServer.SendToAllClientsAsync(rawResponse);
                
                UpdateStatus($"✅ Raw 8-byte response sent to Scratch");
            }
            catch (Exception ex)
            {
                UpdateStatus($"❌ Error sending raw response to Scratch: {ex.Message}");
            }
        }

        private void OnBluetoothStatusChanged(string status)
        {
            UpdateStatus($"Bluetooth: {status}");
            
            // Check for connection state changes and update button states accordingly
            if (status.Contains("Connected to"))
            {
                UpdateButtonStates(true); // Connected
            }
            else if (status.Contains("Disconnected from") || status.Contains("Connection lost") || status.Contains("Failed to connect"))
            {
                UpdateButtonStates(false); // Disconnected
            }
        }
        
        /// <summary>
        /// Update all button states based on connection and mode status
        /// </summary>
        private void UpdateButtonStates(bool isConnected)
        {
            try
            {
                if (InvokeRequired)
                {
                    Invoke(new Action(() => UpdateButtonStates(isConnected)));
                    return;
                }

                // Connection buttons
                var selectedItem = cmbDevices.SelectedItem as BluetoothDeviceItem;
                var hasSelectedDevice = selectedItem?.Device != null;
                var isPairedDevice = selectedItem?.Device?.Authenticated == true;
                
                btnConnect.Enabled = !isConnected && hasSelectedDevice && isPairedDevice;
                btnDisconnect.Enabled = isConnected;
                
                // Test button
                btnTestMultipleArrays.Enabled = isConnected && !_bluetoothManager.IsContinuousMode && !_bluetoothManager.IsListenOnlyMode;

                // Continuous mode buttons
                if (isConnected)
                {
                    btnStartContinuous.Enabled = !_bluetoothManager.IsContinuousMode && !_bluetoothManager.IsListenOnlyMode;
                    btnStopContinuous.Enabled = _bluetoothManager.IsContinuousMode;
                }
                else
                {
                    btnStartContinuous.Enabled = false;
                    btnStopContinuous.Enabled = false;
                }

                // Listen-only mode buttons
                if (isConnected)
                {
                    btnStartListen.Enabled = !_bluetoothManager.IsListenOnlyMode && !_bluetoothManager.IsContinuousMode;
                    btnStopListen.Enabled = _bluetoothManager.IsListenOnlyMode;
                }
                else
                {
                    btnStartListen.Enabled = false;
                    btnStopListen.Enabled = false;
                }
            }
            catch (Exception ex)
            {
                UpdateStatus($"Error updating button states: {ex.Message}");
            }
        }

        private void OnDevicesDiscovered(BluetoothDeviceInfo[] devices)
        {
            Invoke(new Action(() =>
            {
                cmbDevices.Items.Clear();
                foreach (var device in devices)
                {
                    cmbDevices.Items.Add(new BluetoothDeviceItem { Device = device });
                }
                
                if (devices.Length > 0)
                {
                    cmbDevices.Enabled = true; // Enable device selection when devices are found
                    UpdateStatus($"Found {devices.Length} Bluetooth devices. Select one to pair/connect.");
                    
                    // Show breakdown of paired vs unpaired
                    var pairedCount = devices.Count(d => d.Authenticated);
                    var unpairedCount = devices.Length - pairedCount;
                    
                    if (pairedCount > 0 && unpairedCount > 0)
                    {
                        UpdateStatus($"  📱 {pairedCount} already paired, {unpairedCount} available for pairing");
                    }
                    else if (pairedCount > 0)
                    {
                        UpdateStatus($"  ✅ All {pairedCount} devices are already paired");
                    }
                    else
                    {
                        UpdateStatus($"  🔗 All {unpairedCount} devices need pairing before connection");
                    }
                    
                    // Auto-select first device if available
                    if (cmbDevices.Items.Count > 0)
                    {
                        cmbDevices.SelectedIndex = 0;
                        UpdateStatus($"Auto-selected first device: {cmbDevices.Items[0]}");
                    }
                }
                else
                {
                    cmbDevices.Enabled = false; // Disable device selection when no devices found
                    UpdateStatus("❌ No Bluetooth devices found. Make sure devices are discoverable and try scanning again.");
                    UpdateStatus("💡 Tip: Put your robot/device in pairing/discoverable mode and scan again.");
                }
                
                // Update button states after device list changes
                UpdateDeviceSelectionUI();
            }));
        }

        private void UpdateStatus(string message)
        {
            if (InvokeRequired)
            {
                Invoke(new Action(() => UpdateStatus(message)));
                return;
            }

            txtStatus.AppendText($"[{DateTime.Now:HH:mm:ss}] {message}\r\n");
            txtStatus.SelectionStart = txtStatus.Text.Length;
            txtStatus.ScrollToCaret();
        }

        private void btnStartServer_Click(object sender, EventArgs e)
        {
            if (_webSocketServer.Start())
            {
                btnStartServer.Enabled = false;
                btnStopServer.Enabled = true;
                UpdateStatus("WebSocket server started on ws://localhost:5000");
            }
            else
            {
                UpdateStatus("Failed to start WebSocket server");
            }
        }

        private async void btnStopServer_Click(object sender, EventArgs e)
        {
            try
            {
                btnStopServer.Enabled = false;
                UpdateStatus("Stopping WebSocket server...");
                
                // Stop server asynchronously to prevent UI hanging
                await _webSocketServer.StopAsync();
                
                btnStartServer.Enabled = true;
                UpdateStatus("WebSocket server stopped");
            }
            catch (Exception ex)
            {
                UpdateStatus($"Error stopping server: {ex.Message}");
                btnStartServer.Enabled = true;
                btnStopServer.Enabled = true;
            }
        }

        private async void btnScanDevices_Click(object sender, EventArgs e)
        {
            btnScanDevices.Enabled = false;
            try
            {
                await _bluetoothManager.ScanForDevicesAsync();
            }
            finally
            {
                btnScanDevices.Enabled = true;
            }
        }        private async void btnConnect_Click(object sender, EventArgs e)
        {
            var selectedItem = cmbDevices.SelectedItem as BluetoothDeviceItem;
            var selectedDevice = selectedItem?.Device;
            
            if (selectedDevice == null)
            {
                UpdateStatus("❌ Please select a device to connect to.");
                return;
            }
            
            try
            {
                // Disable buttons during connection attempt
                btnConnect.Enabled = false;
                SetTestButtonEnabled(false);
                
                bool success = await _bluetoothManager.ConnectToDeviceAsync(selectedDevice);
                if (success)
                {
                    // Connection successful - enable disconnect and test buttons
                    btnConnect.Enabled = false;
                    btnDisconnect.Enabled = true;
                    SetTestButtonEnabled(true);
                    UpdateStatus("Connected! You can now test multiple array transmission.");
                }
                else
                {
                    // Connection failed - re-enable connect button
                    btnConnect.Enabled = true;
                    btnDisconnect.Enabled = false;
                    SetTestButtonEnabled(false);
                }
            }
            catch (Exception ex)
            {
                UpdateStatus($"Error connecting to device: {ex.Message}");
                // Error occurred - re-enable connect button
                btnConnect.Enabled = true;
                btnDisconnect.Enabled = false;
                SetTestButtonEnabled(false);
            }
        }        private void SetTestButtonEnabled(bool enabled)
        {
            // Use the new comprehensive button state method instead
            UpdateButtonStates(_bluetoothManager?.IsConnected ?? false);
        }        private async void btnDisconnect_Click(object sender, EventArgs e)
        {
            try
            {
                // Disable buttons during disconnection
                btnDisconnect.Enabled = false;
                UpdateButtonStates(false);
                
                UpdateStatus("Disconnecting... (timeout in 5 seconds)");
                
                // Use a shorter timeout and force async execution to prevent GUI hang
                var disconnectTask = Task.Run(async () =>
                {
                    try
                    {
                        await _bluetoothManager.DisconnectAsync();
                        return true;
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Disconnect error: {ex.Message}");
                        return false;
                    }
                });
                
                var timeoutTask = Task.Delay(5000); // 5 second timeout
                
                var completedTask = await Task.WhenAny(disconnectTask, timeoutTask);
                
                if (completedTask == timeoutTask)
                {
                    // Timeout - force disconnect immediately without waiting
                    UpdateStatus("Disconnect timeout - forcing immediate disconnect...");
                    
                    // Force disconnect in background task to avoid GUI hang
                    _ = Task.Run(() =>
                    {
                        try
                        {
                            _bluetoothManager.ForceDisconnect();
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"Force disconnect error: {ex.Message}");
                        }
                    });
                    
                    UpdateStatus("Force disconnect initiated.");
                }
                else if (disconnectTask.IsFaulted)
                {
                    // Error in disconnect - force disconnect
                    UpdateStatus($"Disconnect error: {disconnectTask.Exception?.GetBaseException()?.Message}");
                    UpdateStatus("Using force disconnect...");
                    
                    // Force disconnect in background
                    _ = Task.Run(() =>
                    {
                        try
                        {
                            _bluetoothManager.ForceDisconnect();
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"Force disconnect error: {ex.Message}");
                        }
                    });
                    
                    UpdateStatus("Force disconnect initiated.");
                }
                else
                {
                    // Normal completion
                    var disconnectResult = await disconnectTask;
                    if (disconnectResult)
                    {
                        UpdateStatus("Disconnection completed normally.");
                    }
                    else
                    {
                        UpdateStatus("Disconnection completed with warnings.");
                    }
                }
                
                // Always ensure buttons are in correct state
                UpdateButtonStates(false);
            }
            catch (Exception ex)
            {
                UpdateStatus($"Error during disconnection: {ex.Message}");
                // Final fallback - force disconnect
                try
                {
                    _bluetoothManager.ForceDisconnect();
                    UpdateStatus("Force disconnect completed after error.");
                }
                catch (Exception forceEx)
                {
                    UpdateStatus($"Force disconnect also failed: {forceEx.Message}");
                }
                // Ensure buttons are in correct state even if error occurred
                UpdateButtonStates(false);
            }
        }        private void cmbDevices_SelectedIndexChanged(object sender, EventArgs e)
        {
            UpdateDeviceSelectionUI();
            UpdateButtonStates(_bluetoothManager?.IsConnected ?? false);
        }private async void btnTestMultipleArrays_Click(object sender, EventArgs e)
        {
            if (!_bluetoothManager.IsConnected)
            {
                UpdateStatus("Cannot test: Not connected to Bluetooth device");
                return;
            }            try
            {
                UpdateStatus("Starting multiple arrays test: 10 lists of 8 bytes each...");

                // Create one array with 10 packets of 8 bytes each (send all at once)
                var allPackets = new byte[10][];
                
                for (int packetIndex = 0; packetIndex < 10; packetIndex++)
                {
                    var packet = new byte[8];
                    packet[0] = 0x07; // Command identifier
                    packet[1] = (byte)(packetIndex + 1); // Packet number (1-10)
                    packet[2] = 0xFF; // Test marker
                    packet[3] = 0xAA; // Test marker
                    packet[4] = (byte)(packetIndex * 10); // Sequence number
                    packet[5] = 0x55; // Test marker
                    packet[6] = 0xBB; // Test marker
                    packet[7] = (byte)(255 - (packetIndex * 10)); // Checksum-like value
                    
                    allPackets[packetIndex] = packet;
                }

                UpdateStatus($"Created 10 packets of 8 bytes each - sending all in one shot");

                // Send all 10 packets at once with 150ms intervals between them
                bool sendSuccess = await _bluetoothManager.SendDataArrayAsync(allPackets);
                  if (sendSuccess)
                {
                    UpdateStatus("All 10 packets queued for transmission with 150ms intervals");
                    
                    // Wait for all 10 responses with 100ms timeout per response (total 1000ms)
                    var responses = await WaitForResponsesWithTimeout(10, 1000);
                    
                    UpdateStatus($"Test completed! Sent 10 packets, received {responses.Count} responses");                    // Display all responses as a formatted list
                    if (responses.Count > 0)
                    {
                        UpdateStatus("=== RECEIVED RESPONSE LIST ===");
                        UpdateStatus($"Total responses: {responses.Count}/10");
                        
                        // Show all responses in a compact format
                        for (int i = 0; i < responses.Count; i++)
                        {
                            var hexString = BitConverter.ToString(responses[i]).Replace("-", " ");
                            UpdateStatus($"[{i + 1:D2}] {hexString}");
                        }
                        
                        UpdateStatus("=== END OF LIST ===");
                    }
                    else
                    {
                        UpdateStatus("No responses received (timeout or connection issue)");
                    }
                    
                    if (responses.Count < 10)
                    {
                        UpdateStatus($"Missing {10 - responses.Count} responses");
                    }
                }
                else
                {
                    UpdateStatus("Failed to send packet array");
                }

                UpdateStatus($"Success rate: {(sendSuccess ? "Sent successfully" : "Send failed")}");
            }
            catch (Exception ex)
            {
                UpdateStatus($"Error in multiple arrays test: {ex.Message}");
            }
        }        /// <summary>
        /// Wait for a specific number of responses with a timeout
        /// Uses BluetoothManager's received data queue for actual responses
        /// </summary>
        private async Task<List<byte[]>> WaitForResponsesWithTimeout(int expectedCount, int timeoutMs)
        {
            var responses = new List<byte[]>();
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            
            UpdateStatus($"Waiting for {expectedCount} responses (timeout: {timeoutMs}ms)");
              while (responses.Count < expectedCount && stopwatch.ElapsedMilliseconds < timeoutMs)
            {
                // Try to get received data from BluetoothManager
                if (_bluetoothManager.TryGetReceivedData(out byte[] receivedData))
                {
                    responses.Add(receivedData);
                    
                    // Only show progress for every 5th response or at milestones
                    if (responses.Count % 5 == 0 || responses.Count == expectedCount)
                    {
                        UpdateStatus($"  Progress: {responses.Count}/{expectedCount} responses received");
                    }
                }
                else
                {
                    await Task.Delay(10); // Small delay to prevent tight loop
                }
            }
            
            if (responses.Count < expectedCount)
            {
                UpdateStatus($"Timeout: Only received {responses.Count}/{expectedCount} responses in {stopwatch.ElapsedMilliseconds}ms");
            }
            else
            {
                UpdateStatus($"Successfully received all {responses.Count} responses in {stopwatch.ElapsedMilliseconds}ms");
            }
            
            return responses;        }

        private void Form1_FormClosing(object sender, FormClosingEventArgs e)
        {
            try
            {
                // Use force disconnect to avoid hanging during app exit
                if (_bluetoothManager?.IsConnected == true)
                {
                    _bluetoothManager.ForceDisconnect();
                }
                
                _webSocketServer?.Dispose();
                _bluetoothManager?.Dispose();
            }
            catch (Exception ex)
            {
                // Don't prevent closing even if cleanup fails
                System.Diagnostics.Debug.WriteLine($"Error during form closing: {ex.Message}");
            }
        }

        private void Form1_Load(object sender, EventArgs e)
        {

        }

        private void btnStartContinuous_Click(object sender, EventArgs e)
        {
            if (!_bluetoothManager.IsConnected)
            {
                UpdateStatus("Cannot start continuous mode: Not connected to Bluetooth device");
                return;
            }

            try
            {
                _continuousResponseCount = 0;
                _bluetoothManager.StartContinuousTransmission();
                
                // Update all button states based on new mode
                UpdateButtonStates(true);
                
                UpdateStatus("Started continuous transmission mode (10 packets every 500ms)");
            }
            catch (Exception ex)
            {
                UpdateStatus($"Error starting continuous mode: {ex.Message}");
                UpdateButtonStates(true); // Ensure buttons are in correct state
            }
        }

        private void btnStopContinuous_Click(object sender, EventArgs e)
        {
            try
            {
                _bluetoothManager.StopContinuousTransmission();
                
                // Update all button states based on new mode
                UpdateButtonStates(true);
                
                UpdateStatus($"Stopped continuous transmission mode. Total response lists received: {_continuousResponseCount}");
            }
            catch (Exception ex)
            {
                UpdateStatus($"Error stopping continuous mode: {ex.Message}");
                UpdateButtonStates(true); // Ensure buttons are in correct state
            }
        }

        private void OnContinuousResponseReceived(List<byte[]> responses)
        {
            _continuousResponseCount++;
            
            if (InvokeRequired)
            {
                Invoke(new Action(() => OnContinuousResponseReceived(responses)));
                return;
            }

            // Display response list every 10th reception to avoid flooding the UI
            if (_continuousResponseCount % 10 == 0)
            {
                UpdateStatus($"=== CONTINUOUS RESPONSE LIST #{_continuousResponseCount} ===");
                UpdateStatus($"Received {responses.Count}/10 responses:");
                
                for (int i = 0; i < Math.Min(responses.Count, 10); i++)
                {
                    var hexString = BitConverter.ToString(responses[i]).Replace("-", " ");
                    UpdateStatus($"[{i + 1:D2}] {hexString}");
                }
                
                if (responses.Count < 10)
                {
                    UpdateStatus($"Missing {10 - responses.Count} responses");
                }
                
                UpdateStatus("=== END OF LIST ===");
            }
            else
            {
                // Just show a summary for other responses
                UpdateStatus($"Continuous #{_continuousResponseCount}: {responses.Count}/10 responses received");
            }
        }

        private void btnStartListen_Click(object sender, EventArgs e)
        {
            try
            {
                _bluetoothManager.StartListenOnlyMode();
                
                // Update all button states based on new mode
                UpdateButtonStates(true);
                
                UpdateStatus("Started LISTEN-ONLY mode - just receiving data from robot...");
            }
            catch (Exception ex)
            {
                UpdateStatus($"Error starting listen-only mode: {ex.Message}");
                UpdateButtonStates(true); // Ensure buttons are in correct state
            }
        }

        private void btnStopListen_Click(object sender, EventArgs e)
        {
            try
            {
                _bluetoothManager.StopListenOnlyMode();
                
                // Update all button states based on new mode
                UpdateButtonStates(true);
                
                UpdateStatus("Stopped listen-only mode - normal operation resumed");
            }
            catch (Exception ex)
            {
                UpdateStatus($"Error stopping listen-only mode: {ex.Message}");
                UpdateButtonStates(true); // Ensure buttons are in correct state
            }
        }

        // Bluetooth Adapter Management Event Handlers
        private void btnRefreshAdapters_Click(object sender, EventArgs e)
        {
            try
            {
                UpdateStatus("🔄 Refreshing Bluetooth adapters...");
                RefreshBluetoothAdapters();
            }
            catch (Exception ex)
            {
                UpdateStatus($"Error refreshing adapters: {ex.Message}");
            }
        }

        private void cmbBluetoothAdapters_SelectedIndexChanged(object sender, EventArgs e)
        {
            try
            {
                if (cmbBluetoothAdapters.SelectedIndex >= 0)
                {
                    var success = _bluetoothManager.SelectBluetoothAdapter(cmbBluetoothAdapters.SelectedIndex);
                    if (success)
                    {
                        UpdateAdapterSelectionUI();
                        UpdateStatus($"✅ Bluetooth adapter selected: {cmbBluetoothAdapters.SelectedItem}");
                    }
                    else
                    {
                        UpdateStatus("❌ Failed to select Bluetooth adapter");
                    }
                }
            }
            catch (Exception ex)
            {
                UpdateStatus($"Error selecting adapter: {ex.Message}");
            }
        }

        private void OnBluetoothAdaptersDiscovered(InTheHand.Net.Bluetooth.BluetoothRadio[] adapters)
        {
            if (InvokeRequired)
            {
                Invoke(new Action<InTheHand.Net.Bluetooth.BluetoothRadio[]>(OnBluetoothAdaptersDiscovered), adapters);
                return;
            }

            try
            {
                cmbBluetoothAdapters.Items.Clear();
                
                for (int i = 0; i < adapters.Length; i++)
                {
                    var adapter = adapters[i];
                    var isTPLink = adapter.Name?.ToLower().Contains("tp-link") == true ||
                                   adapter.Name?.ToLower().Contains("usb") == true ||
                                   adapter.Name?.ToLower().Contains("dongle") == true;
                    
                    var displayName = $"[{i + 1}] {adapter.Name}";
                    if (isTPLink)
                    {
                        displayName += " 🔥"; // Fire emoji for TP-Link/USB dongles
                    }
                    
                    cmbBluetoothAdapters.Items.Add(displayName);
                }

                // Auto-select the currently selected adapter
                var selectedAdapter = _bluetoothManager.SelectedBluetoothAdapter;
                if (selectedAdapter != null)
                {
                    for (int i = 0; i < adapters.Length; i++)
                    {
                        if (adapters[i].LocalAddress == selectedAdapter.LocalAddress)
                        {
                            cmbBluetoothAdapters.SelectedIndex = i;
                            break;
                        }
                    }
                }

                UpdateAdapterSelectionUI();
                UpdateStatus($"Found {adapters.Length} Bluetooth adapter(s)");
            }
            catch (Exception ex)
            {
                UpdateStatus($"Error updating adapter list: {ex.Message}");
            }
        }

        private void RefreshBluetoothAdapters()
        {
            try
            {
                _bluetoothManager.RefreshAndSelectTPLinkDongle();
            }
            catch (Exception ex)
            {
                UpdateStatus($"Error refreshing adapters: {ex.Message}");
            }
        }

        private void UpdateAdapterSelectionUI()
        {
            try
            {
                if (InvokeRequired)
                {
                    Invoke(new Action(UpdateAdapterSelectionUI));
                    return;
                }

                var selectedInfo = _bluetoothManager.GetSelectedAdapterDescription();
                lblSelectedAdapter.Text = selectedInfo;
                
                // Update UI based on adapter type
                var isTPLinkSelected = selectedInfo.Contains("🔥 TP-Link") || selectedInfo.Contains("USB Dongle");
                if (isTPLinkSelected)
                {
                    lblSelectedAdapter.ForeColor = Color.DarkGreen;
                    lblSelectedAdapter.Font = new Font(lblSelectedAdapter.Font, FontStyle.Bold);
                }
                else
                {
                    lblSelectedAdapter.ForeColor = Color.DarkBlue;
                    lblSelectedAdapter.Font = new Font(lblSelectedAdapter.Font, FontStyle.Regular);
                }
            }
            catch (Exception ex)
            {
                UpdateStatus($"Error updating adapter UI: {ex.Message}");
            }
        }

        private async void btnPairDevice_Click(object sender, EventArgs e)
        {
            try
            {
                var selectedItem = cmbDevices.SelectedItem as BluetoothDeviceItem;
                var selectedDevice = selectedItem?.Device;
                
                if (selectedDevice == null)
                {
                    UpdateStatus("❌ Please select a device to pair.");
                    return;
                }

                string pin = txtPairingPin.Text.Trim();
                if (string.IsNullOrEmpty(pin))
                {
                    UpdateStatus("❌ Please enter a pairing PIN.");
                    return;
                }

                UpdateStatus($"🔗 Attempting to pair with {selectedDevice.DeviceName}...");
                btnPairDevice.Enabled = false;

                bool success = await _bluetoothManager.PairWithDeviceAsync(selectedDevice, pin);
                
                if (success)
                {
                    UpdateStatus($"✅ Successfully paired with {selectedDevice.DeviceName}");
                    
                    // Update the device authentication status and refresh UI
                    selectedDevice.Refresh();
                    UpdateDeviceSelectionUI();
                }
                else
                {
                    UpdateStatus($"❌ Failed to pair with {selectedDevice.DeviceName}");
                    btnPairDevice.Enabled = true;
                }
            }
            catch (Exception ex)
            {
                UpdateStatus($"❌ Error during pairing: {ex.Message}");
                btnPairDevice.Enabled = true;
            }
        }

        private async void btnUnpairDevice_Click(object sender, EventArgs e)
        {
            try
            {
                var selectedItem = cmbDevices.SelectedItem as BluetoothDeviceItem;
                var selectedDevice = selectedItem?.Device;
                
                if (selectedDevice == null)
                {
                    UpdateStatus("❌ Please select a device to unpair.");
                    return;
                }

                UpdateStatus($"🔓 Attempting to unpair {selectedDevice.DeviceName}...");
                btnUnpairDevice.Enabled = false;

                bool success = await _bluetoothManager.UnpairDeviceAsync(selectedDevice);
                
                if (success)
                {
                    UpdateStatus($"✅ Successfully unpaired {selectedDevice.DeviceName}");
                    
                    // Update the device authentication status and refresh UI
                    selectedDevice.Refresh();
                    UpdateDeviceSelectionUI();
                }
                else
                {
                    UpdateStatus($"❌ Failed to unpair {selectedDevice.DeviceName}");
                    btnUnpairDevice.Enabled = true;
                }
            }
            catch (Exception ex)
            {
                UpdateStatus($"❌ Error during unpairing: {ex.Message}");
                btnUnpairDevice.Enabled = true;
            }
        }

        private void LoadBluetoothAdapters()
        {
            try
            {
                _bluetoothManager.DetectBluetoothAdapters();
                var adapters = _bluetoothManager.AvailableBluetoothAdapters;
                
                cmbBluetoothAdapters.Items.Clear();
                
                if (adapters.Length == 0)
                {
                    UpdateStatus("❌ No Bluetooth adapters found. Please check your Bluetooth hardware.");
                    lblSelectedAdapter.Text = "No adapters available";
                    lblSelectedAdapter.ForeColor = Color.Red;
                    return;
                }

                for (int i = 0; i < adapters.Length; i++)
                {
                    var adapter = adapters[i];
                    var displayName = $"[{i + 1}] {adapter.Name} ({adapter.LocalAddress})";
                    cmbBluetoothAdapters.Items.Add(displayName);
                }

                // Auto-select the preferred adapter (external dongles like TP-Link preferred)
                bool success = _bluetoothManager.PreferExternalDongle();
                if (success && _bluetoothManager.SelectedBluetoothAdapter != null)
                {
                    // Find the selected adapter in the combo box
                    var selectedAdapter = _bluetoothManager.SelectedBluetoothAdapter;
                    for (int i = 0; i < adapters.Length; i++)
                    {
                        if (adapters[i].LocalAddress == selectedAdapter.LocalAddress)
                        {
                            cmbBluetoothAdapters.SelectedIndex = i;
                            break;
                        }
                    }
                }
                else if (cmbBluetoothAdapters.Items.Count > 0)
                {
                    cmbBluetoothAdapters.SelectedIndex = 0;
                }

                UpdateAdapterSelectionUI();
                UpdateStatus($"✅ Found {adapters.Length} Bluetooth adapter(s).");
            }
            catch (Exception ex)
            {
                UpdateStatus($"❌ Error loading Bluetooth adapters: {ex.Message}");
            }
        }

        private class BluetoothDeviceItem
        {
            public BluetoothDeviceInfo Device { get; set; }
            
            public override string ToString()
            {
                var deviceName = string.IsNullOrEmpty(Device.DeviceName) ? "Unknown Device" : Device.DeviceName;
                var pairedStatus = Device.Authenticated ? "✅" : "🔗";
                return $"{pairedStatus} {deviceName} ({Device.DeviceAddress})";
            }
        }

        private void UpdateDeviceSelectionUI()
        {
            var selectedItem = cmbDevices.SelectedItem as BluetoothDeviceItem;
            var selectedDevice = selectedItem?.Device;
            
            if (selectedDevice == null)
            {
                btnPairDevice.Enabled = false;
                btnUnpairDevice.Enabled = false;
                btnConnect.Enabled = false;
                btnPairDevice.Text = "🔗 Pair Device";
                return;
            }

            // Check if device is already paired
            bool isPaired = selectedDevice.Authenticated;
            
            if (isPaired)
            {
                btnPairDevice.Text = "✅ Already Paired";
                btnPairDevice.Enabled = false;
                btnUnpairDevice.Enabled = true;
                btnConnect.Enabled = !_bluetoothManager.IsConnected;
                
                UpdateStatus($"Selected paired device: {selectedDevice.DeviceName} ({selectedDevice.DeviceAddress})");
            }
            else
            {
                btnPairDevice.Text = "🔗 Pair Device";
                btnPairDevice.Enabled = true;
                btnUnpairDevice.Enabled = false;
                btnConnect.Enabled = false;
                
                UpdateStatus($"Selected unpaired device: {selectedDevice.DeviceName} ({selectedDevice.DeviceAddress}) - pair first");
            }
        }

        private async Task ProcessScratch80ByteProtocol(byte[] data)
        {
            try
            {
                // Parse 80-byte structured packet
                // Bytes 0-1: Header (0xAA, 0x55)
                // Bytes 2-3: Request ID
                // Byte 4: Data length
                // Bytes 5-6: Timestamp
                // Byte 7: Reserved
                // Bytes 8-71: Command data (64 bytes max)
                // Byte 72: Checksum
                // Bytes 73-79: Padding
                
                if (data[0] != 0xAA || data[1] != 0x55)
                {
                    UpdateStatus("❌ Invalid packet header");
                    await SendResponseToScratch(new List<byte[]>(), false, "Invalid header");
                    return;
                }
                
                int requestId = BitConverter.ToUInt16(data, 2);
                int dataLength = data[4];
                int timestamp = BitConverter.ToUInt16(data, 5);
                byte checksum = data[72];
                
                // Verify checksum
                byte calculatedChecksum = 0;
                for (int i = 0; i < 72; i++)
                {
                    calculatedChecksum ^= data[i];
                }
                
                if (checksum != calculatedChecksum)
                {
                    UpdateStatus($"❌ Checksum mismatch. Expected: {calculatedChecksum:X2}, Got: {checksum:X2}");
                    await SendResponseToScratch(new List<byte[]>(), false, "Checksum error");
                    return;
                }
                
                // Extract command data
                byte[] commandData = new byte[dataLength];
                Array.Copy(data, 8, commandData, 0, Math.Min(dataLength, 64));
                
                UpdateStatus($"✅ Valid packet: ID={requestId}, Length={dataLength}, Commands={dataLength}");
                
                // Process the commands and send to robot
                await ProcessCommandsToRobot(commandData, requestId);
            }
            catch (Exception ex)
            {
                UpdateStatus($"❌ Error in protocol processing: {ex.Message}");
                await SendResponseToScratch(new List<byte[]>(), false, $"Protocol error: {ex.Message}");
            }
        }

        private async Task ProcessCommandsToRobot(byte[] commands, int requestId)
        {
            try
            {
                // Split commands into 8-byte packets (max 8 packets from 64 bytes)
                var packets = new List<byte[]>();
                
                for (int i = 0; i < commands.Length; i += 8)
                {
                    byte[] packet = new byte[8];
                    int bytesToCopy = Math.Min(8, commands.Length - i);
                    Array.Copy(commands, i, packet, 0, bytesToCopy);
                    
                    // Pad remaining bytes with zeros if needed
                    for (int j = bytesToCopy; j < 8; j++)
                    {
                        packet[j] = 0x00;
                    }
                    
                    packets.Add(packet);
                }
                
                UpdateStatus($"🔄 Sending {packets.Count} packets to robot (Request ID: {requestId})...");
                
                // Send to robot and wait for response
                bool success = await _bluetoothManager.SendDataArrayAsync(packets.ToArray());
                
                if (success)
                {
                    // Wait for robot response
                    var responses = await WaitForResponsesWithTimeout(packets.Count, 3000);
                    
                    UpdateStatus($"✅ Robot responded: {responses.Count}/{packets.Count} packets received");
                    
                    // Send structured response back to Scratch
                    await SendStructuredResponseToScratch(responses, requestId, true, "Success");
                    
                    // Update counters
                    Interlocked.Increment(ref _dataPacketsSent);
                    BeginInvoke(new Action(() =>
                    {
                        lblDataCount.Text = $"Scratch commands processed: {_dataPacketsSent}";
                    }));
                }
                else
                {
                    UpdateStatus("❌ Failed to send data to robot");
                    await SendStructuredResponseToScratch(new List<byte[]>(), requestId, false, "Robot send failed");
                }
            }
            catch (Exception ex)
            {
                UpdateStatus($"❌ Error processing commands: {ex.Message}");
                await SendStructuredResponseToScratch(new List<byte[]>(), requestId, false, $"Command error: {ex.Message}");
            }
        }

        private async Task SendStructuredResponseToScratch(List<byte[]> robotResponses, int requestId, bool success, string message)
        {
            try
            {
                // Create 80-byte response packet
                byte[] responsePacket = new byte[80];
                
                // Header
                responsePacket[0] = 0xBB; // Response header
                responsePacket[1] = 0x66; // Response header
                
                // Request ID (echo back)
                BitConverter.GetBytes((ushort)requestId).CopyTo(responsePacket, 2);
                
                // Status
                responsePacket[4] = (byte)(success ? 0x01 : 0x00);
                responsePacket[5] = (byte)robotResponses.Count;
                
                // Timestamp
                BitConverter.GetBytes((ushort)(DateTime.Now.Ticks & 0xFFFF)).CopyTo(responsePacket, 6);
                
                // Robot response data (up to 64 bytes)
                int dataOffset = 8;
                foreach (var response in robotResponses.Take(8)) // Max 8 responses
                {
                    if (dataOffset + 8 <= 72) // Leave space for checksum and padding
                    {
                        response.CopyTo(responsePacket, dataOffset);
                        dataOffset += 8;
                    }
                }
                
                // Checksum
                byte checksum = 0;
                for (int i = 0; i < 72; i++)
                {
                    checksum ^= responsePacket[i];
                }
                responsePacket[72] = checksum;
                
                // Padding (bytes 73-79 already initialized to 0)
                
                // Send response back to Scratch
                await _webSocketServer.SendToAllClientsAsync(responsePacket);
                
                UpdateStatus($"📤 Sent response to Scratch: ID={requestId}, Success={success}, Data={robotResponses.Count} packets");
            }
            catch (Exception ex)
            {
                UpdateStatus($"❌ Error sending structured response: {ex.Message}");
            }
        }

        private void OnScratchConnected(string sessionId)
        {
            UpdateStatus($"🔗 Scratch connected (Session: {sessionId})");
        }

        private void OnScratchDisconnected(string sessionId)
        {
            UpdateStatus($"🔌 Scratch disconnected (Session: {sessionId}) - Stopping all robot operations");
            
            try
            {
                // CRITICAL: Immediately clear all queues to prevent further commands
                _bluetoothManager?.ClearQueue();
                
                // Stop any continuous operations
                if (_bluetoothManager?.IsContinuousMode == true)
                {
                    _ = Task.Run(() =>
                    {
                        try
                        {
                            _bluetoothManager.StopContinuousTransmission();
                            UpdateStatus("🛑 Stopped continuous mode due to Scratch disconnect");
                        }
                        catch (Exception ex)
                        {
                            UpdateStatus($"⚠️ Error stopping continuous mode: {ex.Message}");
                        }
                    });
                }
                
                // Stop any listen-only operations
                if (_bluetoothManager?.IsListenOnlyMode == true)
                {
                    _ = Task.Run(() =>
                    {
                        try
                        {
                            _bluetoothManager.StopListenOnlyMode();
                            UpdateStatus("🛑 Stopped listen-only mode due to Scratch disconnect");
                        }
                        catch (Exception ex)
                        {
                            UpdateStatus($"⚠️ Error stopping listen mode: {ex.Message}");
                        }
                    });
                }
                
                UpdateStatus("✅ All operations stopped - no further commands will be sent to robot");
            }
            catch (Exception ex)
            {
                UpdateStatus($"❌ Error during Scratch disconnect cleanup: {ex.Message}");
            }
        }
    }
}
