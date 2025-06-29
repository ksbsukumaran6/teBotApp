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

        public Form1()
        {
            InitializeComponent();
            InitializeComponents();
        }

        private void InitializeComponents()
        {
            // Initialize WebSocket server
            _webSocketServer = new WebSocketDataServer();
            _webSocketServer.DataReceived += OnDataReceived;
            
            // Subscribe to WebSocket connection events
            DataReceiver.SessionConnected += OnScratchConnected;
            DataReceiver.SessionDisconnected += OnScratchDisconnected;
            
            // Initialize Bluetooth manager
            _bluetoothManager = new BluetoothManager();
            _bluetoothManager.StatusChanged += OnBluetoothStatusChanged;
            _bluetoothManager.DevicesDiscovered += OnDevicesDiscovered;
            _bluetoothManager.DataReceived += OnRobotDataReceived; // Forward robot data to Scratch
            _bluetoothManager.BluetoothAdaptersDiscovered += OnBluetoothAdaptersDiscovered;

            // Initialize adapter UI
            LoadBluetoothAdapters();
            
            UpdateStatus("Application started. Click 'Start Server' to begin receiving WebSocket data.");
            
            // Show initial adapter selection
            UpdateAdapterSelectionUI();
        }

        private async void OnDataReceived(byte[] data)
        {
            try
            {
                if (_bluetoothManager.IsConnected)
                {
                    // Convert the received bytes to a string (assuming UTF-8 or ASCII encoding)
                    string hexString = Encoding.UTF8.GetString(data); // or Encoding.ASCII.GetString(data)
                    UpdateStatus($"� Received hex string from Scratch: {hexString}");

                    // Send the hex string directly
                    bool success = await _bluetoothManager.SendDataImmediately(hexString);

                    if (success)
                    {
                        UpdateStatus($"✅ Command sent to robot successfully");
                        await Task.Delay(100); // 100ms delay between commands
                    }
                    else
                    {
                        UpdateStatus($"❌ Failed to send command to robot");
                    }
                }
                else
                {
                    UpdateStatus("❌ Received Scratch data but robot not connected");
                }
            }
            catch (Exception ex)
            {
                UpdateStatus($"❌ Error forwarding Scratch data: {ex.Message}");
            }
        }

        private void OnScratchConnected(string sessionId)
        {
            UpdateStatus($"🌟 Scratch connected (Session: {sessionId})");
            UpdateStatus($"   WebSocket server can now send data to Scratch");
            
            // Notify BluetoothManager that Scratch is connected
            _bluetoothManager?.OnScratchConnected();
        }

        private void OnScratchDisconnected(string sessionId)
        {
            UpdateStatus($"❌ Scratch disconnected (Session: {sessionId})");
            UpdateStatus($"   Robot data will not reach Scratch until reconnected");
            
            // Notify BluetoothManager that Scratch is disconnected and clear pending commands
            _bluetoothManager?.OnScratchDisconnected();
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
        }       
         private async void btnConnect_Click(object sender, EventArgs e)
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

                bool success = await _bluetoothManager.ConnectToDeviceAsync(selectedDevice);
                if (success)
                {
                    // Connection successful - enable disconnect button
                    btnConnect.Enabled = false;
                    btnDisconnect.Enabled = true;
                    UpdateStatus("Connected! Ready for Scratch commands.");
                }
                else
                {
                    // Connection failed - re-enable connect button
                    btnConnect.Enabled = true;
                    btnDisconnect.Enabled = false;
                }
            }
            catch (Exception ex)
            {
                UpdateStatus($"Error connecting to device: {ex.Message}");
                // Error occurred - re-enable connect button
                btnConnect.Enabled = true;
                btnDisconnect.Enabled = false;
            }
        }

        private async void btnDisconnect_Click(object sender, EventArgs e)
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
        }

        private void cmbDevices_SelectedIndexChanged(object sender, EventArgs e)
        {
            UpdateDeviceSelectionUI();
            UpdateButtonStates(_bluetoothManager?.IsConnected ?? false);
        }

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

   

        private void Form1_FormClosing(object sender, FormClosingEventArgs e)
        {
            try
            {
                // Ensure Bluetooth disconnects and resources are released
                _bluetoothManager?.DisconnectAsync().Wait();
            }
            catch (Exception ex)
            {
                UpdateStatus($"Error during closing: {ex.Message}");
            }
        }

        /// <summary>
        /// Handle robot data with immediate forwarding to Scratch - simple bridge
        /// </summary>
        private async void OnRobotDataReceived(byte[] robotData)
        {
            try
            {
                // CRITICAL DEBUG: Confirm event was received
                UpdateStatus($"🚨 FORM1: OnRobotDataReceived() called with {robotData.Length} bytes");
                
                var robotHex = BitConverter.ToString(robotData).Replace("-", " ");
                UpdateStatus($"🤖 Received {robotData.Length} bytes from robot: {robotHex}");
                
                // Check if this is a sensor response packet (starts with 0x10)
                bool isSensorResponse = robotData.Length >= 1 && robotData[0] == 0x10;
                
                if (isSensorResponse)
                {
                    UpdateStatus($"🎯 SENSOR DATA - forwarding immediately to Scratch");
                }
                else
                {
                    UpdateStatus($"📋 OTHER DATA - forwarding to Scratch");
                }
                
                // Check if WebSocket server is running and has clients
                if (_webSocketServer == null)
                {
                    UpdateStatus($"❌ WebSocket server is null - cannot send data");
                    return;
                }
                
                UpdateStatus($"📡 Attempting to send {robotData.Length} bytes to WebSocket clients...");
                
                try
                {
                    // Send ALL robot data immediately to Scratch (simplified approach)
                    await _webSocketServer.SendToAllClientsAsync(robotData);
                    UpdateStatus($"✅ Robot data forwarded to Scratch: {robotHex}");
                }
                catch (Exception wsEx)
                {
                    UpdateStatus($"❌ WebSocket send failed: {wsEx.Message}");
                    UpdateStatus($"   Exception type: {wsEx.GetType().Name}");
                    if (wsEx.InnerException != null)
                    {
                        UpdateStatus($"   Inner exception: {wsEx.InnerException.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                UpdateStatus($"❌ Error in OnRobotDataReceived: {ex.Message}");
                UpdateStatus($"   Exception type: {ex.GetType().Name}");
            }
        }
    }
}
