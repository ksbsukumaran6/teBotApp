
namespace TeBot
{
    partial class Form1
    {
        /// <summary>
        /// Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        /// Clean up any resources being used.
        /// </summary>
        /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        /// <summary>
        /// Required method for Designer support - do not modify
        /// the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            this.mainTableLayoutPanel = new System.Windows.Forms.TableLayoutPanel();
            this.headerPanel = new System.Windows.Forms.Panel();
            this.lblDataCount = new System.Windows.Forms.Label();
            this.lblTitle = new System.Windows.Forms.Label();
            this.webSocketGroupBox = new System.Windows.Forms.GroupBox();
            this.webSocketTableLayout = new System.Windows.Forms.TableLayoutPanel();
            this.lblServer = new System.Windows.Forms.Label();
            this.btnStartServer = new System.Windows.Forms.Button();
            this.btnStopServer = new System.Windows.Forms.Button();
            this.bluetoothGroupBox = new System.Windows.Forms.GroupBox();
            this.bluetoothTableLayout = new System.Windows.Forms.TableLayoutPanel();
            this.lblAdapters = new System.Windows.Forms.Label();
            this.cmbBluetoothAdapters = new System.Windows.Forms.ComboBox();
            this.btnRefreshAdapters = new System.Windows.Forms.Button();
            this.lblSelectedAdapter = new System.Windows.Forms.Label();
            this.lblDevices = new System.Windows.Forms.Label();
            this.cmbDevices = new System.Windows.Forms.ComboBox();
            this.btnScanDevices = new System.Windows.Forms.Button();
            this.btnPairDevice = new System.Windows.Forms.Button();
            this.btnUnpairDevice = new System.Windows.Forms.Button();
            this.txtPairingPin = new System.Windows.Forms.TextBox();
            this.lblPairingPin = new System.Windows.Forms.Label();
            this.btnConnect = new System.Windows.Forms.Button();
            this.btnDisconnect = new System.Windows.Forms.Button();
            this.controlsGroupBox = new System.Windows.Forms.GroupBox();
            this.controlsTableLayout = new System.Windows.Forms.TableLayoutPanel();
            this.btnTestMultipleArrays = new System.Windows.Forms.Button();
            this.btnStartContinuous = new System.Windows.Forms.Button();
            this.btnStopContinuous = new System.Windows.Forms.Button();
            this.btnStartListen = new System.Windows.Forms.Button();
            this.btnStopListen = new System.Windows.Forms.Button();
            this.statusGroupBox = new System.Windows.Forms.GroupBox();
            this.txtStatus = new System.Windows.Forms.TextBox();
            this.mainTableLayoutPanel.SuspendLayout();
            this.headerPanel.SuspendLayout();
            this.webSocketGroupBox.SuspendLayout();
            this.webSocketTableLayout.SuspendLayout();
            this.bluetoothGroupBox.SuspendLayout();
            this.bluetoothTableLayout.SuspendLayout();
            this.controlsGroupBox.SuspendLayout();
            this.controlsTableLayout.SuspendLayout();
            this.statusGroupBox.SuspendLayout();
            this.SuspendLayout();
            // 
            // mainTableLayoutPanel
            // 
            this.mainTableLayoutPanel.ColumnCount = 1;
            this.mainTableLayoutPanel.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 100F));
            this.mainTableLayoutPanel.Controls.Add(this.headerPanel, 0, 0);
            this.mainTableLayoutPanel.Controls.Add(this.webSocketGroupBox, 0, 1);
            this.mainTableLayoutPanel.Controls.Add(this.bluetoothGroupBox, 0, 2);
            this.mainTableLayoutPanel.Controls.Add(this.controlsGroupBox, 0, 3);
            this.mainTableLayoutPanel.Controls.Add(this.statusGroupBox, 0, 4);
            this.mainTableLayoutPanel.Dock = System.Windows.Forms.DockStyle.Fill;
            this.mainTableLayoutPanel.Location = new System.Drawing.Point(0, 0);
            this.mainTableLayoutPanel.Margin = new System.Windows.Forms.Padding(8);
            this.mainTableLayoutPanel.Name = "mainTableLayoutPanel";
            this.mainTableLayoutPanel.Padding = new System.Windows.Forms.Padding(8);
            this.mainTableLayoutPanel.RowCount = 5;
            this.mainTableLayoutPanel.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Absolute, 60F));
            this.mainTableLayoutPanel.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Absolute, 80F));
            this.mainTableLayoutPanel.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Absolute, 240F));
            this.mainTableLayoutPanel.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Absolute, 120F));
            this.mainTableLayoutPanel.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 100F));
            this.mainTableLayoutPanel.Size = new System.Drawing.Size(1000, 700);
            this.mainTableLayoutPanel.TabIndex = 0;
            // 
            // headerPanel
            // 
            this.headerPanel.Controls.Add(this.lblTitle);
            this.headerPanel.Controls.Add(this.lblDataCount);
            this.headerPanel.Dock = System.Windows.Forms.DockStyle.Fill;
            this.headerPanel.Location = new System.Drawing.Point(11, 11);
            this.headerPanel.Name = "headerPanel";
            this.headerPanel.Size = new System.Drawing.Size(978, 54);
            this.headerPanel.TabIndex = 0;
            // 
            // lblTitle
            // 
            this.lblTitle.AutoSize = true;
            this.lblTitle.Font = new System.Drawing.Font("Segoe UI", 16F, System.Drawing.FontStyle.Bold);
            this.lblTitle.ForeColor = System.Drawing.Color.DarkBlue;
            this.lblTitle.Location = new System.Drawing.Point(0, 10);
            this.lblTitle.Name = "lblTitle";
            this.lblTitle.Size = new System.Drawing.Size(380, 30);
            this.lblTitle.TabIndex = 0;
            this.lblTitle.Text = "🤖 TeBot - WebSocket to Bluetooth Bridge";
            // 
            // lblDataCount
            // 
            this.lblDataCount.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.lblDataCount.AutoSize = true;
            this.lblDataCount.Font = new System.Drawing.Font("Segoe UI", 10F);
            this.lblDataCount.Location = new System.Drawing.Point(800, 15);
            this.lblDataCount.Name = "lblDataCount";
            this.lblDataCount.Size = new System.Drawing.Size(138, 19);
            this.lblDataCount.TabIndex = 1;
            this.lblDataCount.Text = "Data packets sent: 0";
            // 
            // webSocketGroupBox
            // 
            this.webSocketGroupBox.Controls.Add(this.webSocketTableLayout);
            this.webSocketGroupBox.Dock = System.Windows.Forms.DockStyle.Fill;
            this.webSocketGroupBox.Font = new System.Drawing.Font("Segoe UI", 10F, System.Drawing.FontStyle.Bold);
            this.webSocketGroupBox.Location = new System.Drawing.Point(11, 71);
            this.webSocketGroupBox.Name = "webSocketGroupBox";
            this.webSocketGroupBox.Padding = new System.Windows.Forms.Padding(8);
            this.webSocketGroupBox.Size = new System.Drawing.Size(978, 74);
            this.webSocketGroupBox.TabIndex = 1;
            this.webSocketGroupBox.TabStop = false;
            this.webSocketGroupBox.Text = "🌐 WebSocket Server";
            // 
            // webSocketTableLayout
            // 
            this.webSocketTableLayout.ColumnCount = 3;
            this.webSocketTableLayout.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Absolute, 120F));
            this.webSocketTableLayout.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 50F));
            this.webSocketTableLayout.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 50F));
            this.webSocketTableLayout.Controls.Add(this.lblServer, 0, 0);
            this.webSocketTableLayout.Controls.Add(this.btnStartServer, 1, 0);
            this.webSocketTableLayout.Controls.Add(this.btnStopServer, 2, 0);
            this.webSocketTableLayout.Dock = System.Windows.Forms.DockStyle.Fill;
            this.webSocketTableLayout.Location = new System.Drawing.Point(8, 26);
            this.webSocketTableLayout.Name = "webSocketTableLayout";
            this.webSocketTableLayout.RowCount = 1;
            this.webSocketTableLayout.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 100F));
            this.webSocketTableLayout.Size = new System.Drawing.Size(962, 40);
            this.webSocketTableLayout.TabIndex = 0;
            // 
            // lblServer
            // 
            this.lblServer.Anchor = System.Windows.Forms.AnchorStyles.Left;
            this.lblServer.AutoSize = true;
            this.lblServer.Font = new System.Drawing.Font("Segoe UI", 9F);
            this.lblServer.Location = new System.Drawing.Point(3, 12);
            this.lblServer.Name = "lblServer";
            this.lblServer.Size = new System.Drawing.Size(94, 15);
            this.lblServer.TabIndex = 0;
            this.lblServer.Text = "Server Control:";
            // 
            // btnStartServer
            // 
            this.btnStartServer.Dock = System.Windows.Forms.DockStyle.Fill;
            this.btnStartServer.Font = new System.Drawing.Font("Segoe UI", 9F);
            this.btnStartServer.Location = new System.Drawing.Point(123, 3);
            this.btnStartServer.Name = "btnStartServer";
            this.btnStartServer.Size = new System.Drawing.Size(415, 34);
            this.btnStartServer.TabIndex = 1;
            this.btnStartServer.Text = "▶️ Start WebSocket Server (Port 8080)";
            this.btnStartServer.UseVisualStyleBackColor = true;
            this.btnStartServer.Click += new System.EventHandler(this.btnStartServer_Click);
            // 
            // btnStopServer
            // 
            this.btnStopServer.Dock = System.Windows.Forms.DockStyle.Fill;
            this.btnStopServer.Enabled = false;
            this.btnStopServer.Font = new System.Drawing.Font("Segoe UI", 9F);
            this.btnStopServer.Location = new System.Drawing.Point(544, 3);
            this.btnStopServer.Name = "btnStopServer";
            this.btnStopServer.Size = new System.Drawing.Size(415, 34);
            this.btnStopServer.TabIndex = 2;
            this.btnStopServer.Text = "⏹️ Stop WebSocket Server";
            this.btnStopServer.UseVisualStyleBackColor = true;
            this.btnStopServer.Click += new System.EventHandler(this.btnStopServer_Click);
            // 
            // bluetoothGroupBox
            // 
            this.bluetoothGroupBox.Controls.Add(this.bluetoothTableLayout);
            this.bluetoothGroupBox.Dock = System.Windows.Forms.DockStyle.Fill;
            this.bluetoothGroupBox.Font = new System.Drawing.Font("Segoe UI", 10F, System.Drawing.FontStyle.Bold);
            this.bluetoothGroupBox.Location = new System.Drawing.Point(11, 151);
            this.bluetoothGroupBox.Name = "bluetoothGroupBox";
            this.bluetoothGroupBox.Padding = new System.Windows.Forms.Padding(8);
            this.bluetoothGroupBox.Size = new System.Drawing.Size(978, 234);
            this.bluetoothGroupBox.TabIndex = 2;
            this.bluetoothGroupBox.TabStop = false;
            this.bluetoothGroupBox.Text = "📡 Bluetooth Management";
            // 
            // bluetoothTableLayout
            // 
            this.bluetoothTableLayout.ColumnCount = 4;
            this.bluetoothTableLayout.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Absolute, 120F));
            this.bluetoothTableLayout.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 50F));
            this.bluetoothTableLayout.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Absolute, 100F));
            this.bluetoothTableLayout.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 50F));
            this.bluetoothTableLayout.Controls.Add(this.lblAdapters, 0, 0);
            this.bluetoothTableLayout.Controls.Add(this.cmbBluetoothAdapters, 1, 0);
            this.bluetoothTableLayout.Controls.Add(this.btnRefreshAdapters, 2, 0);
            this.bluetoothTableLayout.Controls.Add(this.lblSelectedAdapter, 3, 0);
            this.bluetoothTableLayout.Controls.Add(this.lblDevices, 0, 1);
            this.bluetoothTableLayout.Controls.Add(this.cmbDevices, 1, 1);
            this.bluetoothTableLayout.Controls.Add(this.btnScanDevices, 2, 1);
            this.bluetoothTableLayout.Controls.Add(this.btnPairDevice, 3, 1);
            this.bluetoothTableLayout.Controls.Add(this.lblPairingPin, 0, 2);
            this.bluetoothTableLayout.Controls.Add(this.txtPairingPin, 1, 2);
            this.bluetoothTableLayout.Controls.Add(this.btnUnpairDevice, 2, 2);
            this.bluetoothTableLayout.Controls.Add(this.btnConnect, 3, 2);
            this.bluetoothTableLayout.Controls.Add(this.btnDisconnect, 1, 3);
            this.bluetoothTableLayout.Dock = System.Windows.Forms.DockStyle.Fill;
            this.bluetoothTableLayout.Location = new System.Drawing.Point(8, 26);
            this.bluetoothTableLayout.Name = "bluetoothTableLayout";
            this.bluetoothTableLayout.RowCount = 4;
            this.bluetoothTableLayout.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 25F));
            this.bluetoothTableLayout.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 25F));
            this.bluetoothTableLayout.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 25F));
            this.bluetoothTableLayout.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 25F));
            this.bluetoothTableLayout.Size = new System.Drawing.Size(962, 200);
            this.bluetoothTableLayout.TabIndex = 0;
            // 
            // lblAdapters
            // 
            this.lblAdapters.Anchor = System.Windows.Forms.AnchorStyles.Left;
            this.lblAdapters.AutoSize = true;
            this.lblAdapters.Font = new System.Drawing.Font("Segoe UI", 9F);
            this.lblAdapters.Location = new System.Drawing.Point(3, 19);
            this.lblAdapters.Name = "lblAdapters";
            this.lblAdapters.Size = new System.Drawing.Size(103, 15);
            this.lblAdapters.TabIndex = 0;
            this.lblAdapters.Text = "🔧 BT Adapter:";
            // 
            // cmbBluetoothAdapters
            // 
            this.cmbBluetoothAdapters.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Left | System.Windows.Forms.AnchorStyles.Right)));
            this.cmbBluetoothAdapters.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.cmbBluetoothAdapters.Font = new System.Drawing.Font("Segoe UI", 9F);
            this.cmbBluetoothAdapters.FormattingEnabled = true;
            this.cmbBluetoothAdapters.Location = new System.Drawing.Point(123, 15);
            this.cmbBluetoothAdapters.Name = "cmbBluetoothAdapters";
            this.cmbBluetoothAdapters.Size = new System.Drawing.Size(365, 23);
            this.cmbBluetoothAdapters.TabIndex = 1;
            this.cmbBluetoothAdapters.SelectedIndexChanged += new System.EventHandler(this.cmbBluetoothAdapters_SelectedIndexChanged);
            // 
            // btnRefreshAdapters
            // 
            this.btnRefreshAdapters.Dock = System.Windows.Forms.DockStyle.Fill;
            this.btnRefreshAdapters.Font = new System.Drawing.Font("Segoe UI", 9F);
            this.btnRefreshAdapters.Location = new System.Drawing.Point(494, 3);
            this.btnRefreshAdapters.Name = "btnRefreshAdapters";
            this.btnRefreshAdapters.Size = new System.Drawing.Size(94, 44);
            this.btnRefreshAdapters.TabIndex = 2;
            this.btnRefreshAdapters.Text = "🔄 Refresh";
            this.btnRefreshAdapters.UseVisualStyleBackColor = true;
            this.btnRefreshAdapters.Click += new System.EventHandler(this.btnRefreshAdapters_Click);
            // 
            // lblSelectedAdapter
            // 
            this.lblSelectedAdapter.Anchor = System.Windows.Forms.AnchorStyles.Left;
            this.lblSelectedAdapter.AutoSize = true;
            this.lblSelectedAdapter.Font = new System.Drawing.Font("Segoe UI", 8F);
            this.lblSelectedAdapter.ForeColor = System.Drawing.Color.DarkGreen;
            this.lblSelectedAdapter.Location = new System.Drawing.Point(594, 18);
            this.lblSelectedAdapter.Name = "lblSelectedAdapter";
            this.lblSelectedAdapter.Size = new System.Drawing.Size(113, 13);
            this.lblSelectedAdapter.TabIndex = 3;
            this.lblSelectedAdapter.Text = "No adapter selected";
            // 
            // lblDevices
            // 
            this.lblDevices.Anchor = System.Windows.Forms.AnchorStyles.Left;
            this.lblDevices.AutoSize = true;
            this.lblDevices.Font = new System.Drawing.Font("Segoe UI", 9F);
            this.lblDevices.Location = new System.Drawing.Point(3, 69);
            this.lblDevices.Name = "lblDevices";
            this.lblDevices.Size = new System.Drawing.Size(89, 15);
            this.lblDevices.TabIndex = 4;
            this.lblDevices.Text = "🤖 BT Devices:";
            // 
            // cmbDevices
            // 
            this.cmbDevices.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Left | System.Windows.Forms.AnchorStyles.Right)));
            this.cmbDevices.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.cmbDevices.Enabled = false;
            this.cmbDevices.Font = new System.Drawing.Font("Segoe UI", 9F);
            this.cmbDevices.FormattingEnabled = true;
            this.cmbDevices.Location = new System.Drawing.Point(123, 65);
            this.cmbDevices.Name = "cmbDevices";
            this.cmbDevices.Size = new System.Drawing.Size(365, 23);
            this.cmbDevices.TabIndex = 5;
            this.cmbDevices.SelectedIndexChanged += new System.EventHandler(this.cmbDevices_SelectedIndexChanged);
            // 
            // btnScanDevices
            // 
            this.btnScanDevices.Dock = System.Windows.Forms.DockStyle.Fill;
            this.btnScanDevices.Font = new System.Drawing.Font("Segoe UI", 9F);
            this.btnScanDevices.Location = new System.Drawing.Point(494, 53);
            this.btnScanDevices.Name = "btnScanDevices";
            this.btnScanDevices.Size = new System.Drawing.Size(94, 44);
            this.btnScanDevices.TabIndex = 6;
            this.btnScanDevices.Text = "🔍 Scan";
            this.btnScanDevices.UseVisualStyleBackColor = true;
            this.btnScanDevices.Click += new System.EventHandler(this.btnScanDevices_Click);
            // 
            // btnPairDevice
            // 
            this.btnPairDevice.Dock = System.Windows.Forms.DockStyle.Fill;
            this.btnPairDevice.Enabled = false;
            this.btnPairDevice.Font = new System.Drawing.Font("Segoe UI", 9F);
            this.btnPairDevice.Location = new System.Drawing.Point(594, 53);
            this.btnPairDevice.Name = "btnPairDevice";
            this.btnPairDevice.Size = new System.Drawing.Size(365, 44);
            this.btnPairDevice.TabIndex = 7;
            this.btnPairDevice.Text = "🔗 Pair Device";
            this.btnPairDevice.UseVisualStyleBackColor = true;
            this.btnPairDevice.Click += new System.EventHandler(this.btnPairDevice_Click);
            // 
            // lblPairingPin
            // 
            this.lblPairingPin.Anchor = System.Windows.Forms.AnchorStyles.Left;
            this.lblPairingPin.AutoSize = true;
            this.lblPairingPin.Font = new System.Drawing.Font("Segoe UI", 9F);
            this.lblPairingPin.Location = new System.Drawing.Point(3, 119);
            this.lblPairingPin.Name = "lblPairingPin";
            this.lblPairingPin.Size = new System.Drawing.Size(79, 15);
            this.lblPairingPin.TabIndex = 8;
            this.lblPairingPin.Text = "🔐 Pair PIN:";
            // 
            // txtPairingPin
            // 
            this.txtPairingPin.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Left | System.Windows.Forms.AnchorStyles.Right)));
            this.txtPairingPin.Font = new System.Drawing.Font("Segoe UI", 9F);
            this.txtPairingPin.Location = new System.Drawing.Point(123, 115);
            this.txtPairingPin.Name = "txtPairingPin";
            this.txtPairingPin.Size = new System.Drawing.Size(365, 23);
            this.txtPairingPin.TabIndex = 9;
            this.txtPairingPin.Text = "1234";
            this.txtPairingPin.TextAlign = System.Windows.Forms.HorizontalAlignment.Center;
            // 
            // btnUnpairDevice
            // 
            this.btnUnpairDevice.Dock = System.Windows.Forms.DockStyle.Fill;
            this.btnUnpairDevice.Enabled = false;
            this.btnUnpairDevice.Font = new System.Drawing.Font("Segoe UI", 9F);
            this.btnUnpairDevice.Location = new System.Drawing.Point(494, 103);
            this.btnUnpairDevice.Name = "btnUnpairDevice";
            this.btnUnpairDevice.Size = new System.Drawing.Size(94, 44);
            this.btnUnpairDevice.TabIndex = 10;
            this.btnUnpairDevice.Text = "� Unpair";
            this.btnUnpairDevice.UseVisualStyleBackColor = true;
            this.btnUnpairDevice.Click += new System.EventHandler(this.btnUnpairDevice_Click);
            // 
            // btnConnect
            // 
            this.btnConnect.Dock = System.Windows.Forms.DockStyle.Fill;
            this.btnConnect.Enabled = false;
            this.btnConnect.Font = new System.Drawing.Font("Segoe UI", 9F);
            this.btnConnect.Location = new System.Drawing.Point(594, 103);
            this.btnConnect.Name = "btnConnect";
            this.btnConnect.Size = new System.Drawing.Size(365, 44);
            this.btnConnect.TabIndex = 11;
            this.btnConnect.Text = "🔌 Connect to Robot";
            this.btnConnect.UseVisualStyleBackColor = true;
            this.btnConnect.Click += new System.EventHandler(this.btnConnect_Click);
            // 
            // btnDisconnect
            // 
            this.btnDisconnect.Dock = System.Windows.Forms.DockStyle.Fill;
            this.btnDisconnect.Enabled = false;
            this.btnDisconnect.Font = new System.Drawing.Font("Segoe UI", 9F);
            this.btnDisconnect.Location = new System.Drawing.Point(123, 153);
            this.btnDisconnect.Name = "btnDisconnect";
            this.btnDisconnect.Size = new System.Drawing.Size(365, 44);
            this.btnDisconnect.TabIndex = 12;
            this.btnDisconnect.Text = "❌ Disconnect from Robot";
            this.btnDisconnect.UseVisualStyleBackColor = true;
            this.btnDisconnect.Click += new System.EventHandler(this.btnDisconnect_Click);
            // 
            // controlsGroupBox
            // 
            this.controlsGroupBox.Controls.Add(this.controlsTableLayout);
            this.controlsGroupBox.Dock = System.Windows.Forms.DockStyle.Fill;
            this.controlsGroupBox.Font = new System.Drawing.Font("Segoe UI", 10F, System.Drawing.FontStyle.Bold);
            this.controlsGroupBox.Location = new System.Drawing.Point(11, 391);
            this.controlsGroupBox.Name = "controlsGroupBox";
            this.controlsGroupBox.Padding = new System.Windows.Forms.Padding(8);
            this.controlsGroupBox.Size = new System.Drawing.Size(978, 114);
            this.controlsGroupBox.TabIndex = 3;
            this.controlsGroupBox.TabStop = false;
            this.controlsGroupBox.Text = "🎮 Robot Control Tests";
            // 
            // controlsTableLayout
            // 
            this.controlsTableLayout.ColumnCount = 5;
            this.controlsTableLayout.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 20F));
            this.controlsTableLayout.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 20F));
            this.controlsTableLayout.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 20F));
            this.controlsTableLayout.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 20F));
            this.controlsTableLayout.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 20F));
            this.controlsTableLayout.Controls.Add(this.btnTestMultipleArrays, 0, 0);
            this.controlsTableLayout.Controls.Add(this.btnStartContinuous, 1, 0);
            this.controlsTableLayout.Controls.Add(this.btnStopContinuous, 2, 0);
            this.controlsTableLayout.Controls.Add(this.btnStartListen, 3, 0);
            this.controlsTableLayout.Controls.Add(this.btnStopListen, 4, 0);
            this.controlsTableLayout.Dock = System.Windows.Forms.DockStyle.Fill;
            this.controlsTableLayout.Location = new System.Drawing.Point(8, 26);
            this.controlsTableLayout.Name = "controlsTableLayout";
            this.controlsTableLayout.RowCount = 1;
            this.controlsTableLayout.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 100F));
            this.controlsTableLayout.Size = new System.Drawing.Size(962, 80);
            this.controlsTableLayout.TabIndex = 0;
            // 
            // btnTestMultipleArrays
            // 
            this.btnTestMultipleArrays.Dock = System.Windows.Forms.DockStyle.Fill;
            this.btnTestMultipleArrays.Enabled = false;
            this.btnTestMultipleArrays.Font = new System.Drawing.Font("Segoe UI", 9F);
            this.btnTestMultipleArrays.Location = new System.Drawing.Point(3, 3);
            this.btnTestMultipleArrays.Name = "btnTestMultipleArrays";
            this.btnTestMultipleArrays.Size = new System.Drawing.Size(186, 74);
            this.btnTestMultipleArrays.TabIndex = 0;
            this.btnTestMultipleArrays.Text = "🧪 Test Multiple Arrays";
            this.btnTestMultipleArrays.UseVisualStyleBackColor = true;
            this.btnTestMultipleArrays.Click += new System.EventHandler(this.btnTestMultipleArrays_Click);
            // 
            // btnStartContinuous
            // 
            this.btnStartContinuous.Dock = System.Windows.Forms.DockStyle.Fill;
            this.btnStartContinuous.Enabled = false;
            this.btnStartContinuous.Font = new System.Drawing.Font("Segoe UI", 9F);
            this.btnStartContinuous.Location = new System.Drawing.Point(195, 3);
            this.btnStartContinuous.Name = "btnStartContinuous";
            this.btnStartContinuous.Size = new System.Drawing.Size(186, 74);
            this.btnStartContinuous.TabIndex = 1;
            this.btnStartContinuous.Text = "🔄 Start Continuous";
            this.btnStartContinuous.UseVisualStyleBackColor = true;
            this.btnStartContinuous.Click += new System.EventHandler(this.btnStartContinuous_Click);
            // 
            // btnStopContinuous
            // 
            this.btnStopContinuous.Dock = System.Windows.Forms.DockStyle.Fill;
            this.btnStopContinuous.Enabled = false;
            this.btnStopContinuous.Font = new System.Drawing.Font("Segoe UI", 9F);
            this.btnStopContinuous.Location = new System.Drawing.Point(387, 3);
            this.btnStopContinuous.Name = "btnStopContinuous";
            this.btnStopContinuous.Size = new System.Drawing.Size(186, 74);
            this.btnStopContinuous.TabIndex = 2;
            this.btnStopContinuous.Text = "⏹️ Stop Continuous";
            this.btnStopContinuous.UseVisualStyleBackColor = true;
            this.btnStopContinuous.Click += new System.EventHandler(this.btnStopContinuous_Click);
            // 
            // btnStartListen
            // 
            this.btnStartListen.Dock = System.Windows.Forms.DockStyle.Fill;
            this.btnStartListen.Enabled = false;
            this.btnStartListen.Font = new System.Drawing.Font("Segoe UI", 9F);
            this.btnStartListen.Location = new System.Drawing.Point(579, 3);
            this.btnStartListen.Name = "btnStartListen";
            this.btnStartListen.Size = new System.Drawing.Size(186, 74);
            this.btnStartListen.TabIndex = 3;
            this.btnStartListen.Text = "👂 Start Listen Only";
            this.btnStartListen.UseVisualStyleBackColor = true;
            this.btnStartListen.Click += new System.EventHandler(this.btnStartListen_Click);
            // 
            // btnStopListen
            // 
            this.btnStopListen.Dock = System.Windows.Forms.DockStyle.Fill;
            this.btnStopListen.Enabled = false;
            this.btnStopListen.Font = new System.Drawing.Font("Segoe UI", 9F);
            this.btnStopListen.Location = new System.Drawing.Point(771, 3);
            this.btnStopListen.Name = "btnStopListen";
            this.btnStopListen.Size = new System.Drawing.Size(188, 74);
            this.btnStopListen.TabIndex = 4;
            this.btnStopListen.Text = "🔇 Stop Listen";
            this.btnStopListen.UseVisualStyleBackColor = true;
            this.btnStopListen.Click += new System.EventHandler(this.btnStopListen_Click);
            // 
            // statusGroupBox
            // 
            this.statusGroupBox.Controls.Add(this.txtStatus);
            this.statusGroupBox.Dock = System.Windows.Forms.DockStyle.Fill;
            this.statusGroupBox.Font = new System.Drawing.Font("Segoe UI", 10F, System.Drawing.FontStyle.Bold);
            this.statusGroupBox.Location = new System.Drawing.Point(11, 511);
            this.statusGroupBox.Name = "statusGroupBox";
            this.statusGroupBox.Padding = new System.Windows.Forms.Padding(8);
            this.statusGroupBox.Size = new System.Drawing.Size(978, 178);
            this.statusGroupBox.TabIndex = 4;
            this.statusGroupBox.TabStop = false;
            this.statusGroupBox.Text = "📊 Status Log";
            // 
            // txtStatus
            // 
            this.txtStatus.BackColor = System.Drawing.Color.Black;
            this.txtStatus.Dock = System.Windows.Forms.DockStyle.Fill;
            this.txtStatus.Font = new System.Drawing.Font("Consolas", 9F);
            this.txtStatus.ForeColor = System.Drawing.Color.Lime;
            this.txtStatus.Location = new System.Drawing.Point(8, 26);
            this.txtStatus.Multiline = true;
            this.txtStatus.Name = "txtStatus";
            this.txtStatus.ReadOnly = true;
            this.txtStatus.ScrollBars = System.Windows.Forms.ScrollBars.Vertical;
            this.txtStatus.Size = new System.Drawing.Size(962, 144);
            this.txtStatus.TabIndex = 0;
            // 
            // Form1
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(8F, 16F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.BackColor = System.Drawing.Color.WhiteSmoke;
            this.ClientSize = new System.Drawing.Size(1000, 700);
            this.Controls.Add(this.mainTableLayoutPanel);
            this.Font = new System.Drawing.Font("Segoe UI", 10F);
            this.MinimumSize = new System.Drawing.Size(800, 600);
            this.Name = "Form1";
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterScreen;
            this.Text = "TeBot - WebSocket to Bluetooth Bridge";
            this.FormClosing += new System.Windows.Forms.FormClosingEventHandler(this.Form1_FormClosing);
            this.Load += new System.EventHandler(this.Form1_Load);
            this.mainTableLayoutPanel.ResumeLayout(false);
            this.headerPanel.ResumeLayout(false);
            this.headerPanel.PerformLayout();
            this.webSocketGroupBox.ResumeLayout(false);
            this.webSocketTableLayout.ResumeLayout(false);
            this.webSocketTableLayout.PerformLayout();
            this.bluetoothGroupBox.ResumeLayout(false);
            this.bluetoothTableLayout.ResumeLayout(false);
            this.bluetoothTableLayout.PerformLayout();
            this.controlsGroupBox.ResumeLayout(false);
            this.controlsTableLayout.ResumeLayout(false);
            this.statusGroupBox.ResumeLayout(false);
            this.statusGroupBox.PerformLayout();
            this.ResumeLayout(false);

        }

        #endregion

        // Main Layout
        private System.Windows.Forms.TableLayoutPanel mainTableLayoutPanel;
        private System.Windows.Forms.Panel headerPanel;
        private System.Windows.Forms.GroupBox webSocketGroupBox;
        private System.Windows.Forms.GroupBox bluetoothGroupBox;
        private System.Windows.Forms.GroupBox controlsGroupBox;
        private System.Windows.Forms.GroupBox statusGroupBox;

        // Header Controls
        private System.Windows.Forms.Label lblTitle;
        private System.Windows.Forms.Label lblDataCount;

        // WebSocket Controls
        private System.Windows.Forms.TableLayoutPanel webSocketTableLayout;
        private System.Windows.Forms.Label lblServer;
        private System.Windows.Forms.Button btnStartServer;
        private System.Windows.Forms.Button btnStopServer;

        // Bluetooth Controls
        private System.Windows.Forms.TableLayoutPanel bluetoothTableLayout;
        private System.Windows.Forms.Label lblAdapters;
        private System.Windows.Forms.ComboBox cmbBluetoothAdapters;
        private System.Windows.Forms.Button btnRefreshAdapters;
        private System.Windows.Forms.Label lblSelectedAdapter;
        private System.Windows.Forms.Label lblDevices;
        private System.Windows.Forms.ComboBox cmbDevices;
        private System.Windows.Forms.Button btnScanDevices;
        private System.Windows.Forms.Button btnPairDevice;
        private System.Windows.Forms.Button btnUnpairDevice;
        private System.Windows.Forms.Label lblPairingPin;
        private System.Windows.Forms.TextBox txtPairingPin;
        private System.Windows.Forms.Button btnConnect;
        private System.Windows.Forms.Button btnDisconnect;

        // Control Test Buttons
        private System.Windows.Forms.TableLayoutPanel controlsTableLayout;
        private System.Windows.Forms.Button btnTestMultipleArrays;
        private System.Windows.Forms.Button btnStartContinuous;
        private System.Windows.Forms.Button btnStopContinuous;
        private System.Windows.Forms.Button btnStartListen;
        private System.Windows.Forms.Button btnStopListen;

        // Status Controls
        private System.Windows.Forms.TextBox txtStatus;
    }
}

