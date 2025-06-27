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
    {
        private WebSocketDataServer _webSocketServer;
        private BluetoothManager _bluetoothManager;
        private int _dataPacketsSent = 0;
        private int _continuousResponseCount = 0;

        public Form1()
        {
            InitializeComponent();
            InitializeComponents();
        }        private void InitializeComponents()
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
                
                UpdateStatus("Disconnecting... (timeout in 8 seconds)");
                
                // Use timeout wrapper to prevent hanging
                var disconnectTask = _bluetoothManager.DisconnectAsync();
                var timeoutTask = Task.Delay(8000); // 8 second timeout
                
                var completedTask = await Task.WhenAny(disconnectTask, timeoutTask);
                
                if (completedTask == timeoutTask)
                {
                    // Timeout - force disconnect
                    UpdateStatus("Disconnect timeout - forcing disconnect...");
                    _bluetoothManager.ForceDisconnect();
                    UpdateStatus("Force disconnect completed.");
                }
                else if (disconnectTask.IsFaulted)
                {
                    // Error in disconnect - force disconnect
                    UpdateStatus($"Disconnect error: {disconnectTask.Exception?.GetBaseException()?.Message}");
                    UpdateStatus("Using force disconnect...");
                    _bluetoothManager.ForceDisconnect();
                    UpdateStatus("Force disconnect completed.");
                }
                else
                {
                    // Normal completion
                    UpdateStatus("Disconnection completed normally.");
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
    }
}
