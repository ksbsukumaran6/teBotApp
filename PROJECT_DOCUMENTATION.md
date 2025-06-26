# TeBot - WebSocket to Bluetooth Bridge

## Project Overview

TeBot is a C# WinForms application that acts as a bridge between Scratch programming environment and Bluetooth-enabled robots. It receives data from Scratch via WebSocket and forwards it to robots via HC-05 Bluetooth modules, while also handling bidirectional communication.

### Key Features

- **WebSocket Server**: Receives binary data from Scratch extensions
- **Bluetooth Communication**: Connects to HC-05 modules at 115200 baud rate
- **Multi-mode Operation**: Normal, Continuous, and Listen-only modes
- **Real-time Data Processing**: Handles 8-byte data packets with minimal latency
- **Robust Error Handling**: Connection recovery and health monitoring
- **User-friendly Interface**: WinForms GUI with real-time status updates

## Architecture Overview

```plantuml
@startuml TeBot_Architecture
!define RECTANGLE class

package "TeBot Application" {
    RECTANGLE Form1 {
        +WebSocket Server Control
        +Bluetooth Device Management
        +Data Transmission Testing
        +Status Display
    }
    
    RECTANGLE WebSocketDataServer {
        +WebSocket Server
        +Binary Data Reception
        +Event Broadcasting
    }
    
    RECTANGLE BluetoothManager {
        +Device Discovery
        +Connection Management
        +Data Transmission
        +Response Collection
    }
}

package "External Systems" {
    RECTANGLE "Scratch Extension" {
        +WebSocket Client
        +Binary Data Sending
    }
    
    RECTANGLE "HC-05 Bluetooth" {
        +115200 Baud Rate
        +Serial Port Profile
    }
    
    RECTANGLE "Robot Controller" {
        +Command Processing
        +Response Generation
    }
}

"Scratch Extension" --> WebSocketDataServer : Binary Data\n(WebSocket)
WebSocketDataServer --> BluetoothManager : 8-byte Packets
BluetoothManager --> "HC-05 Bluetooth" : Serial Data\n(115200 baud)
"HC-05 Bluetooth" --> "Robot Controller" : Commands
"Robot Controller" --> "HC-05 Bluetooth" : Responses
"HC-05 Bluetooth" --> BluetoothManager : Response Data
BluetoothManager --> Form1 : Status Updates
WebSocketDataServer --> Form1 : Data Events
@enduml
```

## Data Flow Architecture

```plantuml
@startuml TeBot_DataFlow
participant "Scratch" as S
participant "WebSocketServer" as WS
participant "Form1" as F1
participant "BluetoothManager" as BM
participant "HC-05" as HC
participant "Robot" as R

group WebSocket Data Reception
    S -> WS: Binary Data (WebSocket)
    WS -> F1: DataReceived Event
    F1 -> BM: SendDataAsync(byte[8])
    BM -> BM: Queue Data
end

group Bluetooth Transmission
    BM -> BM: TransmissionCallback()
    BM -> BM: Dequeue Data
    BM -> HC: WriteAsync(8 bytes)
    HC -> R: Serial Data (115200 baud)
end

group Response Collection
    R -> HC: Response Data
    HC -> BM: ReadAsync()
    BM -> BM: Process Response
    BM -> F1: DataReceived Event
    F1 -> F1: Update UI
end

group Error Handling
    BM -> BM: Connection Health Check
    alt Connection Failed
        BM -> F1: StatusChanged(Error)
        BM -> BM: Reconnection Logic
    end
end
@enduml
```

## Class Structure

```plantuml
@startuml TeBot_Classes
class Form1 {
    -WebSocketDataServer _webSocketServer
    -BluetoothManager _bluetoothManager
    -int _dataPacketsSent
    -Button[] _controlButtons
    
    +Form1()
    +InitializeComponents()
    +CreateTestButton()
    +btnStartServer_Click()
    +btnConnect_Click()
    +btnTestMultipleArrays_Click()
    -UpdateUI()
    -SetTestButtonEnabled()
}

class WebSocketDataServer {
    -WebSocketServer _server
    -int _port
    -bool _isRunning
    
    +StartAsync(port: int)
    +StopAsync()
    +DataReceived: Action<byte[]>
    +StatusChanged: Action<string>
    -OnMessage(byte[] data)
}

class BluetoothManager {
    -BluetoothClient _bluetoothClient
    -Stream _bluetoothStream
    -ConcurrentQueue<byte[]> _dataQueue
    -Timer _transmissionTimer
    -bool _isConnected
    
    +ConnectToDeviceAsync(device)
    +SendDataAsync(data: byte[])
    +StartContinuousTransmission()
    +StartListenOnlyMode()
    -TransmissionCallback()
    -StartDataReading()
    -IsConnectionHealthy()
}

class BluetoothDeviceInfo {
    +DeviceName: string
    +DeviceAddress: BluetoothAddress
    +Connected: bool
}

Form1 --> WebSocketDataServer : uses
Form1 --> BluetoothManager : uses
BluetoothManager --> BluetoothDeviceInfo : manages
@enduml
```

## Bluetooth Communication Protocol

### Connection Process

```plantuml
@startuml Bluetooth_Connection
participant "Form1" as F
participant "BluetoothManager" as BM
participant "HC-05" as HC

F -> BM: ConnectToDeviceAsync(device)
BM -> BM: Create new BluetoothClient
BM -> BM: Try Service UUIDs

loop For each Service UUID
    BM -> HC: Connect(endpoint)
    alt Connection Success
        HC -> BM: Stream established
        BM -> BM: Configure timeouts
        BM -> BM: StartTransmissionSystem()
        BM -> BM: StartDataReading()
        BM -> F: StatusChanged("Connected")
    else Connection Failed
        BM -> BM: Try next UUID
    end
end

alt All UUIDs Failed
    BM -> F: StatusChanged("Failed to connect")
end
@enduml
```

### Data Transmission Modes

#### 1. Normal Mode
- Sends individual 8-byte packets
- Waits for responses with 500ms timeout
- Transmission interval: 100ms

#### 2. Continuous Mode
- Sends marker (0xAA, 0x55) + 10 packets (82 bytes total)
- Transmission interval: 200ms
- Optimized for bulk data transfer

#### 3. Listen-Only Mode
- No data transmission
- Only receives and displays robot data
- Used for debugging and testing

## Configuration Constants (115200 Baud Optimized)

```csharp
// Timing Constants for 115200 baud HC-05
private const int TRANSMISSION_INTERVAL_MS = 100;        // Fast transmission
private const int RESPONSE_TIMEOUT_MS = 500;             // Quick response timeout
private const int CONTINUOUS_TRANSMISSION_INTERVAL_MS = 200;  // Continuous mode
private const int CONNECTION_TIMEOUT_MS = 10000;         // Connection attempt
private const int READ_TIMEOUT_MS = 1000;                // Data reading
private const int STREAM_TIMEOUT_MS = 5000;              // Stream operations
private const int INTER_BYTE_DELAY_MS = 0;               // No delay at 115200 baud
```

## State Machine Diagram

```plantuml
@startuml TeBot_StateMachine
[*] --> Disconnected

Disconnected --> Connecting : ConnectToDevice()
Connecting --> Connected : Connection Success
Connecting --> Disconnected : Connection Failed

Connected --> NormalMode : Default State
Connected --> ContinuousMode : StartContinuous()
Connected --> ListenOnlyMode : StartListenOnly()

NormalMode --> ContinuousMode : StartContinuous()
NormalMode --> ListenOnlyMode : StartListenOnly()
NormalMode --> Disconnected : Disconnect()

ContinuousMode --> NormalMode : StopContinuous()
ContinuousMode --> ListenOnlyMode : StartListenOnly()
ContinuousMode --> Disconnected : Disconnect()

ListenOnlyMode --> NormalMode : StopListenOnly()
ListenOnlyMode --> ContinuousMode : StartContinuous()
ListenOnlyMode --> Disconnected : Disconnect()

Connected --> Disconnected : Connection Lost
@enduml
```

## Error Handling and Recovery

```plantuml
@startuml Error_Handling
participant "BluetoothManager" as BM
participant "HC-05" as HC
participant "Form1" as F

group Connection Health Check
    BM -> BM: IsConnectionHealthy()
    alt Health Check Failed
        BM -> F: StatusChanged("Connection issue")
        BM -> BM: Set _isConnected = false
        BM -> BM: Stop data reading
    end
end

group Read Error Handling
    BM -> HC: ReadAsync()
    alt IOException/Timeout
        BM -> F: StatusChanged("Read error")
        BM -> BM: Wait 2 seconds
        BM -> BM: Retry read
    else ObjectDisposedException
        BM -> F: StatusChanged("Stream disposed")
        BM -> BM: Stop connection
    end
end

group Write Error Handling
    BM -> HC: WriteAsync()
    alt Write Failed
        BM -> F: StatusChanged("Send error")
        BM -> BM: Set _isConnected = false
        BM -> BM: Stop transmission
    end
end
@enduml
```

## User Interface Components

### Main Form Layout

```
┌─────────────────────────────────────────────────────────────────────┐
│ TeBot - WebSocket to Bluetooth Bridge                                  │
├─────────────────────────────────────────────────────────────────────┤
│ WebSocket Server                                                        │
│ [Start Server] [Stop Server]                                          │
│                                                                        │
│ Bluetooth                                      Data packets sent: 0    │
│ [Scan Devices] [Test Multiple Arrays]                                 │
│                                                                        │
│ Available Devices:                                                     │
│ [Device Dropdown ▼] [Connect] [Disconnect]                           │
│                                                                        │
│ [Start Continuous] [Stop Continuous] [Start Listen] [Stop Listen]     │
│                                                                        │
│ Status:                                                                │
│ ┌─────────────────────────────────────────────────────────────────┐   │
│ │ [Multi-line status text area with scroll]                      │   │
│ │                                                                 │   │
│ │                                                                 │   │
│ └─────────────────────────────────────────────────────────────────┘   │
└─────────────────────────────────────────────────────────────────────┘
```

### Button State Management

```plantuml
@startuml Button_States
state ServerStopped {
    btnStartServer : Enabled
    btnStopServer : Disabled
}

state ServerRunning {
    btnStartServer : Disabled
    btnStopServer : Enabled
}

state BluetoothDisconnected {
    btnConnect : Disabled
    btnDisconnect : Disabled
    btnTestMultipleArrays : Disabled
    btnStartContinuous : Disabled
    btnStopContinuous : Disabled
    btnStartListen : Disabled
    btnStopListen : Disabled
}

state BluetoothConnected {
    btnConnect : Disabled
    btnDisconnect : Enabled
    btnTestMultipleArrays : Enabled
    btnStartContinuous : Enabled
    btnStopContinuous : Disabled
    btnStartListen : Enabled
    btnStopListen : Disabled
}

state ContinuousMode {
    btnStartContinuous : Disabled
    btnStopContinuous : Enabled
    btnTestMultipleArrays : Disabled
    btnStartListen : Disabled
    btnStopListen : Disabled
}

state ListenOnlyMode {
    btnStartListen : Disabled
    btnStopListen : Enabled
    btnStartContinuous : Disabled
    btnTestMultipleArrays : Disabled
}
@enduml
```

## Performance Characteristics

### 115200 Baud Rate Performance

| Operation | Time | Description |
|-----------|------|-------------|
| Single 8-byte packet | ~0.7ms | Transmission time |
| 82-byte continuous block | ~7ms | Marker + 10 packets |
| Response timeout | 500ms | Maximum wait time |
| Transmission interval | 100ms | Between packets |
| Continuous interval | 200ms | Between blocks |

### Memory Usage

- **Data Queue**: ConcurrentQueue<byte[]> for thread-safe operations
- **Buffer Size**: 256 bytes for incoming data
- **Packet Size**: Fixed 8 bytes per data packet
- **Response Collection**: List<byte[]> with automatic cleanup

## Threading Model

```plantuml
@startuml Threading_Model
participant "UI Thread" as UI
participant "WebSocket Thread" as WS
participant "Transmission Timer" as TT
participant "Data Reading Task" as DR
participant "Continuous Timer" as CT

UI -> WS: Start WebSocket Server
WS -> WS: Listen for connections

UI -> TT: Start Transmission System
TT -> TT: Process queue every 100ms

UI -> DR: Start Data Reading
DR -> DR: Continuous read loop

alt Continuous Mode
    UI -> CT: Start Continuous Timer
    CT -> CT: Send blocks every 200ms
    UI -> TT: Stop normal transmission
end

WS -> UI: Invoke(StatusUpdate)
TT -> UI: Invoke(StatusUpdate)
DR -> UI: Invoke(DataReceived)
CT -> UI: Invoke(StatusUpdate)
@enduml
```

## Security Considerations

1. **Bluetooth Pairing**: HC-05 modules should be paired before connection
2. **Data Validation**: All incoming data is validated for correct size (8 bytes)
3. **Connection Authentication**: Uses standard Bluetooth authentication
4. **Error Boundaries**: Robust exception handling prevents crashes

## Troubleshooting Guide

### Common Issues

1. **Connection Timeout**
   - Check HC-05 is powered and paired
   - Verify baud rate is set to 115200
   - Ensure correct COM port mapping

2. **Data Not Sending**
   - Verify connection status
   - Check transmission queue status
   - Monitor error messages in status area

3. **No Data Received**
   - Use Listen-Only mode for testing
   - Check robot is sending responses
   - Verify data format (8-byte packets)

### Diagnostic Features

- **Real-time Status**: Continuous status updates in UI
- **Debug Output**: Detailed logging to Debug console
- **Connection Health**: Automatic health monitoring
- **Queue Status**: Real-time queue size display

## Development Setup

### Prerequisites

- Visual Studio 2019 or later
- .NET Framework 4.7.2 or later
- 32feet.NET Bluetooth library
- WebSocketSharp library

### Build Configuration

```xml
<PropertyGroup>
  <TargetFramework>net472</TargetFramework>
  <Platform>AnyCPU</Platform>
  <OutputType>WinExe</OutputType>
</PropertyGroup>
```

### Dependencies

```xml
<PackageReference Include="32feet.NET" Version="3.5.0.0" />
<PackageReference Include="WebSocketSharp" Version="1.0.3-rc11" />
```

## Future Enhancements

1. **Multiple Robot Support**: Connect to multiple HC-05 modules simultaneously
2. **Data Logging**: Save transmission logs to file
3. **Configuration UI**: Runtime configuration of timeouts and intervals
4. **Protocol Extensions**: Support for different packet sizes and formats
5. **Wireless Alternatives**: WiFi and other wireless communication options

## Conclusion

TeBot provides a robust, high-performance bridge between Scratch programming environment and Bluetooth robots. The optimized 115200 baud rate configuration ensures minimal latency and maximum throughput for real-time robotics applications.
