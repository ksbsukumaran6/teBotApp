using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using InTheHand.Net.Sockets;

namespace TeBot
{    public partial class Form1 : Form
    {        private WebSocketDataServer _webSocketServer;
        private BluetoothManager _bluetoothManager;
        private int _dataPacketsSent = 0;
        private Button btnTestMultipleArrays;
        private Button btnStartContinuous;
        private Button btnStopContinuous;
        private Button btnStartListen;
        private Button btnStopListen;
        private int _continuousResponseCount = 0;
        public Form1()
        {
            InitializeComponent();
            CreateTestButton(); // Create button first
            InitializeComponents(); // Then initialize other components
        }        private void CreateTestButton()
        {
            try
            {
                // Create the test button programmatically
                btnTestMultipleArrays = new Button
                {
                    Name = "btnTestMultipleArrays",
                    Text = "Test Multiple Arrays",
                    Size = new Size(140, 30),
                    Location = new Point(130, 110),
                    Enabled = false,
                    UseVisualStyleBackColor = true
                };
                
                btnTestMultipleArrays.Click += btnTestMultipleArrays_Click;
                this.Controls.Add(btnTestMultipleArrays);

                // Create continuous transmission buttons
                btnStartContinuous = new Button
                {
                    Name = "btnStartContinuous",
                    Text = "Start Continuous",
                    Size = new Size(120, 30),
                    Location = new Point(280, 110),
                    Enabled = false,
                    UseVisualStyleBackColor = true
                };
                
                btnStartContinuous.Click += btnStartContinuous_Click;
                this.Controls.Add(btnStartContinuous);                btnStopContinuous = new Button
                {
                    Name = "btnStopContinuous",
                    Text = "Stop Continuous",
                    Size = new Size(120, 30),
                    Location = new Point(410, 110),
                    Enabled = false,
                    UseVisualStyleBackColor = true
                };
                
                btnStopContinuous.Click += btnStopContinuous_Click;
                this.Controls.Add(btnStopContinuous);                // Create listen-only mode buttons
                btnStartListen = new Button
                {
                    Name = "btnStartListen",
                    Text = "Start Listen Only",
                    Size = new Size(120, 30),
                    Location = new Point(540, 110),
                    Enabled = false,
                    UseVisualStyleBackColor = true
                };
                
                btnStartListen.Click += btnStartListen_Click;
                this.Controls.Add(btnStartListen);

                btnStopListen = new Button
                {
                    Name = "btnStopListen",
                    Text = "Stop Listen",
                    Size = new Size(120, 30),
                    Location = new Point(670, 110),
                    Enabled = false,
                    UseVisualStyleBackColor = true
                };
                
                btnStopListen.Click += btnStopListen_Click;
                this.Controls.Add(btnStopListen);
            }
            catch (Exception ex)
            {
                // Don't use UpdateStatus here as the text box might not be ready yet
                System.Diagnostics.Debug.WriteLine($"Error creating test buttons: {ex.Message}");
            }
        }private void InitializeComponents()
        {
            // Initialize WebSocket server
            _webSocketServer = new WebSocketDataServer();
            _webSocketServer.DataReceived += OnDataReceived;            // Initialize Bluetooth manager
            _bluetoothManager = new BluetoothManager();
            _bluetoothManager.StatusChanged += OnBluetoothStatusChanged;
            _bluetoothManager.DevicesDiscovered += OnDevicesDiscovered;
            _bluetoothManager.QueueStatus += OnQueueStatusChanged;
            _bluetoothManager.TransmissionStatus += OnTransmissionStatusChanged;
            _bluetoothManager.ContinuousResponseReceived += OnContinuousResponseReceived;

            UpdateStatus("Application started. Click 'Start Server' to begin receiving WebSocket data.");
            
            // Verify button was created
            if (btnTestMultipleArrays != null)
            {
                UpdateStatus("Test button ready for multiple array transmission.");
            }
            else
            {
                UpdateStatus("Warning: Test button not available.");
            }
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
                    // Fire and forget - don't wait for completion to reduce lag
                    _ = Task.Run(async () =>
                    {
                        bool success = await _bluetoothManager.SendDataAsync(data);
                        if (success)
                        {
                            Interlocked.Increment(ref _dataPacketsSent);
                            
                            // Update UI less frequently to reduce overhead
                            if (_dataPacketsSent % 3 == 0) // Update every 3rd packet
                            {
                                BeginInvoke(new Action(() =>
                                {
                                    lblDataCount.Text = $"Data packets sent: {_dataPacketsSent}";
                                }));
                            }
                        }
                    });
                }
                else
                {
                    await Task.Delay(1); // Make method properly async
                    UpdateStatus("Received data but no Bluetooth device connected");
                }
            }
            catch (Exception ex)
            {
                UpdateStatus($"Error processing received data: {ex.Message}");
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
                btnConnect.Enabled = !isConnected && cmbDevices.SelectedItem != null;
                btnDisconnect.Enabled = isConnected;
                
                // Test button
                if (btnTestMultipleArrays != null)
                {
                    btnTestMultipleArrays.Enabled = isConnected && !_bluetoothManager.IsContinuousMode && !_bluetoothManager.IsListenOnlyMode;
                }

                // Continuous mode buttons
                if (btnStartContinuous != null && btnStopContinuous != null)
                {
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
                }

                // Listen-only mode buttons
                if (btnStartListen != null && btnStopListen != null)
                {
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
                    cmbDevices.Items.Add(new DeviceItem { Device = device });
                }
                
                if (devices.Length > 0)
                {
                    btnConnect.Enabled = true;
                    UpdateStatus($"Found {devices.Length} devices. Select one to connect.");
                }
                else
                {
                    UpdateStatus("No COM ports found. Make sure Bluetooth devices are paired and try again.");
                }
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

        private void btnStopServer_Click(object sender, EventArgs e)
        {
            _webSocketServer.Stop();
            btnStartServer.Enabled = true;
            btnStopServer.Enabled = false;
            UpdateStatus("WebSocket server stopped");
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
            if (cmbDevices.SelectedItem is DeviceItem selectedItem)
            {
                try
                {
                    // Disable buttons during connection attempt
                    btnConnect.Enabled = false;
                    SetTestButtonEnabled(false);
                    
                    bool success = await _bluetoothManager.ConnectToDeviceAsync(selectedItem.Device);
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
                
                await _bluetoothManager.DisconnectAsync();
                
                // Button states will be updated by OnBluetoothStatusChanged when "Disconnected" status is received
                UpdateStatus("Disconnection completed.");
            }
            catch (Exception ex)
            {
                UpdateStatus($"Error during disconnection: {ex.Message}");
                // Ensure buttons are in correct state even if error occurred
                UpdateButtonStates(false);
            }
        }private void cmbDevices_SelectedIndexChanged(object sender, EventArgs e)
        {
            // Only enable connect if device is selected AND not currently connected
            btnConnect.Enabled = cmbDevices.SelectedItem != null && !_bluetoothManager.IsConnected;
        }        private async void btnTestMultipleArrays_Click(object sender, EventArgs e)
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
            _webSocketServer?.Dispose();
            _bluetoothManager?.Dispose();
        }

        private class DeviceItem
        {            public BluetoothDeviceInfo Device { get; set; }
            
            public override string ToString()
            {
                return $"{Device.DeviceName} ({Device.DeviceAddress})";
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
                
                UpdateStatus("Started continuous transmission mode (10 packets every 200ms)");
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
    }
}
