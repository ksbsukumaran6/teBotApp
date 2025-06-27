# TeBot - Bluetooth Bridge Application

A C# WinForms application that bridges data between Scratch extensions (via WebSocket) and robots (via Bluetooth HC-05), designed for high-speed, reliable communication at 115200 baud.

## Features

### Core Communication
- **Bluetooth HC-05 Integration**: Optimized for 115200 baud rate communication
- **WebSocket Server**: Receives binary data from Scratch extensions
- **Dual Communication Modes**: Master-slave architecture support
- **Real-time Data Processing**: Minimal lag with async, non-blocking operations

### Operating Modes
1. **Normal Mode**: Single 8-byte packet transmission with 82-byte responses
2. **Continuous Mode**: Sends 82-byte commands (2-byte marker + 10×8-byte packets) every 500ms
3. **Listen-Only Mode**: Pure diagnostic mode for monitoring incoming data

### Advanced Features
- **82-byte Response Parsing**: Automatically parses robot responses into 10×8-byte packets
- **Queue-based Data Management**: Handles multiple packets efficiently
- **Connection Health Monitoring**: Automatic detection and recovery from connection issues
- **Robust Error Handling**: Comprehensive timeout and cancellation management
- **UI Thread Safety**: All status updates properly marshaled to UI thread
- **External Bluetooth Adapter Support**: Auto-detection and preference for USB dongles (TP-Link, etc.)
- **Device Pairing Management**: Built-in pairing/unpairing functionality with PIN support
- **Master-Slave Architecture**: Clear role identification and communication protocol

## Technical Specifications

- **Target Framework**: .NET Framework 4.8+
- **Bluetooth Library**: 32feet.NET for Windows Bluetooth stack integration
- **WebSocket Library**: WebSocketSharp for Scratch extension communication
- **Baud Rate**: Optimized for HC-05 at 115200 baud (12x faster than standard 9600)
- **Data Format**: 8-byte packets with support for 82-byte compound messages
- **Transmission Intervals**: 500ms for continuous mode operations
- **Response Timeout**: 500ms optimized for high-speed communication
- **Connection Timeout**: 8-second timeout protection for disconnect operations

## Project Structure

```
TeBot/
├── TeBot/                          # Main application
│   ├── BluetoothManager.cs         # Core Bluetooth communication logic
│   ├── Form1.cs                    # Main UI and application logic
│   ├── Form1.Designer.cs           # UI designer file
│   ├── Program.cs                  # Application entry point
│   └── TeBot.csproj               # Project configuration
├── PROJECT_DOCUMENTATION.md       # Comprehensive project documentation
├── CODE_ANALYSIS.md               # Deep dive into code structure and methods
├── PLANTUML_DIAGRAMS.puml         # System architecture diagrams
├── HC05_BaudRate_Setup.txt        # HC-05 configuration instructions
├── performance-test.html          # WebSocket performance testing tool
└── websocket-test-client.html     # WebSocket client for testing
```

## Quick Start

### Prerequisites
- Windows 10/11 with Bluetooth support
- .NET Framework 4.8 or later
- Visual Studio 2019+ or Visual Studio Code
- HC-05 Bluetooth module configured for 115200 baud

### Installation
1. Clone this repository:
   ```bash
   git clone https://github.com/yourusername/TeBot.git
   cd TeBot
   ```

2. Open `TeBot.sln` in Visual Studio

3. Restore NuGet packages:
   - 32feet.NET
   - WebSocketSharp

4. Build and run the application

### HC-05 Setup
Configure your HC-05 module for optimal performance:
```
AT+UART=115200,0,0    # Set to 115200 baud
AT+NAME=TeBot         # Optional: Set device name
AT+PSWD=1234         # Optional: Set pairing password
```

See `HC05_BaudRate_Setup.txt` for detailed configuration instructions.

## Usage

### Basic Operation
1. **Scan for Devices**: Click "Scan" to discover nearby Bluetooth devices
2. **Connect**: Select your HC-05 module and click "Connect"
3. **Choose Mode**:
   - **Normal**: For single packet testing
   - **Continuous**: For high-speed data streaming
   - **Listen**: For diagnostic monitoring

### WebSocket Integration
- **Default Port**: 8080
- **Protocol**: Binary data transmission (80 bytes) or JSON
- **Format**: 80-byte packets from Scratch extensions representing 10 commands

### Data Formats

#### Scratch to C# (WebSocket)
```
80-byte format:
[10 × 8-byte command packets] - No headers, direct command data
```

#### C# to Arduino (Bluetooth)
```
Individual 8-byte packets sent sequentially:
[cmd_type, param1, param2, param3, param4, param5, param6, param7]
C# splits 80-byte Scratch input into 10×8-byte packets via SendDataArrayAsync()
```

#### Arduino to C# (Bluetooth)
```
8-byte response per command:
[status, direction, speed, battery, distance, led_state, moving, counter]
```

## Architecture

The application follows a master-slave communication pattern:

- **TeBot (Master)**: Sends commands and processes responses
- **Robot (Slave)**: Receives commands, processes them, and sends back responses
- **Scratch Extension**: Sends data via WebSocket to TeBot

For detailed architecture information, see [PROJECT_DOCUMENTATION.md](PROJECT_DOCUMENTATION.md).

## Performance Characteristics

- **Transmission Rate**: 200ms intervals in continuous mode
- **Data Throughput**: ~410 bytes/second (82 bytes × 5 times/second)
- **Response Timeout**: 500ms (optimized for 115200 baud)
- **Connection Timeout**: 10 seconds for initial pairing
- **Processing Lag**: <10ms for data handling

## Troubleshooting

### Common Issues
1. **Connection Hangs**: Use the improved disconnect logic with timeouts
2. **Data Loss**: Ensure HC-05 is configured for 115200 baud
3. **WebSocket Errors**: Check firewall settings for port 8080
4. **Partial Responses**: Verify robot firmware handles 82-byte commands

### Debug Features
- **Real-time Status Updates**: Monitor all communication in the UI
- **Hex Data Display**: View raw bytes for debugging
- **Connection Health Checks**: Automatic detection of connection issues
- **Listen-Only Mode**: Monitor incoming data without transmission

## Documentation

- **[PROJECT_DOCUMENTATION.md](PROJECT_DOCUMENTATION.md)**: Complete project overview, architecture, and user guide
- **[CODE_ANALYSIS.md](CODE_ANALYSIS.md)**: Detailed code structure and method documentation
- **[PLANTUML_DIAGRAMS.puml](PLANTUML_DIAGRAMS.puml)**: System architecture and sequence diagrams

## Development

### Key Classes
- **`BluetoothManager`**: Core communication logic, device management, and data processing
- **`Form1`**: UI logic, user interactions, and status display
- **WebSocket Integration**: Built-in server for Scratch extension communication

### Testing Tools
- **`websocket-test-client.html`**: Test WebSocket connectivity
- **`performance-test.html`**: Benchmark communication performance

## Project Structure

```
TeBot/
├── Form1.cs              - Main application form and UI logic
├── Form1.Designer.cs     - UI controls and layout
├── WebSocketServer.cs    - TCP server handling WebSocket-like connections
├── BluetoothManager.cs   - Bluetooth device discovery and communication
├── Program.cs            - Application entry point
└── TeBot.csproj          - Project configuration
```

## Technical Details

### WebSocket Server
- Simplified TCP server that accepts binary data
- Listens on port 5000 by default
- Handles multiple concurrent connections
- Thread-safe data processing

### Bluetooth Communication
- Uses .NET SerialPort class for communication
- Automatically scans for available COM ports
- Configurable baud rate (default: 9600)
- Error handling and connection management

### Data Flow
```
WebSocket Client → TCP Server → Data Processing → SerialPort → Bluetooth Device
```

## Recent Updates

### Version 1.5.0 (June 2025)
- **Performance Enhancement**: Optimized continuous transmission interval to 500ms for faster robot communication
- **Improved Responsiveness**: Enhanced real-time communication performance while maintaining reliability  
- **Validated Performance**: Confirmed stable operation at 500ms intervals with high success rates

### Version 1.4.0 (June 2025)  
- **Master-Slave Architecture**: Clear communication protocol with role identification
- **Enhanced Error Handling**: Robust timeout protection and connection recovery
- **Force Disconnect**: Prevents hanging during Bluetooth disconnection operations

## Requirements

- Windows operating system
- .NET Framework 4.7.2 or higher
- Visual Studio 2017 or later
- Paired Bluetooth device with Serial Port Profile (SPP)

## Building the Project

1. Open `TeBot.sln` in Visual Studio
2. Build the solution (Ctrl+Shift+B)
3. Run the application (F5)

## Testing

1. Build and run the TeBot application
2. Start the WebSocket server
3. Connect a Bluetooth device
4. Open `websocket-test-client.html` in a web browser
5. Connect to `ws://localhost:5000`
6. Send test data using the provided buttons

## Common Bluetooth Devices

This application works with any Bluetooth device that supports Serial Port Profile (SPP), including:

- Arduino with Bluetooth modules (HC-05, HC-06)
- ESP32 with Bluetooth Classic
- Bluetooth serial adapters
- Custom embedded devices with Bluetooth

## Troubleshooting

### WebSocket Connection Issues
- Ensure the server is started before connecting clients
- Check Windows Firewall settings for port 5000
- Verify no other applications are using port 5000

### Bluetooth Connection Issues
- Ensure the device is paired in Windows Bluetooth settings
- Check that the device supports Serial Port Profile
- Try different baud rates if connection fails
- Verify the COM port is not in use by other applications

### Data Transfer Issues
- Monitor the status log for error messages
- Ensure the Bluetooth device is connected before sending data
- Check that the receiving device is configured correctly

## License

This project is provided as-is for educational and development purposes.
