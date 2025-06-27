# TeBot Protocol Correction Summary

## Issue Identified
The Arduino code was expecting 80-byte packets with specific headers (0xAA 0x55), but the C# BluetoothManager actually sends individual 8-byte packets sequentially.

## Root Cause Analysis

### C# BluetoothManager Behavior
- **DATA_PACKET_SIZE = 8**: Defined as constant in BluetoothManager.cs
- **SendDataArrayAsync()**: Queues each 8-byte packet individually
- **TransmissionCallback()**: Sends one 8-byte packet at a time via SendDataDirectlyAsync()
- **Transmission Interval**: 100ms between packets

### Form1.cs Processing
- Receives 80-byte input from Scratch via WebSocket
- Splits into 10×8-byte packets using: `byte[][] packets = SplitIntoPackets(data, 8);`
- Calls: `await _bluetoothManager.SendDataArrayAsync(packets);`

### Original Arduino Issue
- Expected 80-byte packets with header format: [0xAA, 0x55, requestId, dataLength, ...]
- Used complex packet parsing with checksums
- Mismatched with actual C# transmission protocol

## Solution Implemented

### New Arduino Code (arduino_tebot_code_8byte.ino)
- **Simplified Protocol**: Expects individual 8-byte command packets
- **No Headers**: Direct command processing without special markers
- **8-Byte Commands**: [cmd_type, param1, param2, param3, param4, param5, param6, param7]
- **8-Byte Responses**: [status, direction, speed, battery, distance, led_state, moving, counter]
- **Real-time Processing**: Immediate response to each command

### Protocol Flow (Corrected)
1. **Scratch → C# WebSocket**: 80-byte binary packet
2. **C# Processing**: Split into 10×8-byte packets
3. **C# → Arduino Bluetooth**: Individual 8-byte packets (100ms intervals)
4. **Arduino Processing**: Process each 8-byte command immediately
5. **Arduino → C# Bluetooth**: 8-byte response per command
6. **C# → Scratch WebSocket**: Combined 80-byte response

## Technical Details

### C# BluetoothManager Constants
```csharp
private const int DATA_PACKET_SIZE = 8;              // Individual packet size
private const int TRANSMISSION_INTERVAL_MS = 100;    // 100ms between packets
private const int RESPONSE_TIMEOUT_MS = 500;         // Response timeout
```

### Arduino Command Processing
```cpp
const int PACKET_SIZE = 8;        // Each packet is 8 bytes
const int RESPONSE_SIZE = 8;      // Response packet size
const int COMMAND_TIMEOUT = 1000; // Auto-stop timeout
```

### Bluetooth Configuration
- **Baud Rate**: 115200 (both C# and Arduino)
- **Hardware**: HC-05 module
- **Flow Control**: None
- **Data Bits**: 8, Stop Bits: 1, Parity: None

## Command Types Supported

| Command | Value | Description | Parameters |
|---------|-------|-------------|------------|
| STOP | 0x00 | Stop all motors | None |
| MOVE_FORWARD | 0x01 | Move forward | Param1: Speed (0-100) |
| MOVE_BACKWARD | 0x02 | Move backward | Param1: Speed (0-100) |
| TURN_LEFT | 0x03 | Turn left | Param1: Speed (0-100) |
| TURN_RIGHT | 0x04 | Turn right | Param1: Speed (0-100) |
| SET_SPEED | 0x05 | Set default speed | Param1: Speed (0-100) |
| READ_SENSOR | 0x06 | Request sensor data | Param1: Sensor type |
| SET_LED | 0x07 | Control LED | Param1: On/Off, Param2: Brightness |
| PLAY_SOUND | 0x08 | Play sound | Param1-2: Frequency, Param3: Duration |
| STATUS_REQUEST | 0x0A | Request status | None |

## Files Updated

### New Files Created
- `arduino_tebot_code_8byte.ino`: Corrected Arduino implementation for 8-byte packets

### Files Modified
- `README.md`: Updated protocol documentation in WebSocket section

### Files Confirmed (No Changes Needed)
- `Form1.cs`: Already correctly splits 80-byte → 10×8-byte packets
- `BluetoothManager.cs`: Already sends 8-byte packets individually
- `WebSocketServer.cs`: Already handles 80-byte binary responses
- `scratch-tebot-extension.js`: Already creates 80-byte packets for WebSocket

## Testing Recommendations

1. **Replace Arduino Code**: Use `arduino_tebot_code_8byte.ino` instead of the original
2. **Verify Bluetooth Connection**: Ensure HC-05 is configured for 115200 baud
3. **Test Command Sequence**: Send multiple commands and verify individual responses
4. **Monitor Debug Output**: Arduino Serial Monitor shows command reception and processing
5. **Check Timing**: Verify 100ms intervals between commands in C# logs

## Performance Benefits

- **Reduced Latency**: No waiting for complete 80-byte packets
- **Better Responsiveness**: Commands processed as soon as received
- **Simplified Protocol**: No header parsing or checksum verification
- **Error Recovery**: Failed commands don't affect subsequent ones
- **Real-time Feedback**: Immediate status responses for each command

## Backward Compatibility

The original Arduino code (`arduino_tebot_code.ino`) will **not work** with the current C# implementation. The new `arduino_tebot_code_8byte.ino` is required for proper communication.

## Conclusion

The protocol mismatch has been identified and corrected. The C# system was already working correctly by sending 8-byte packets individually. The Arduino code has been updated to match this protocol, resulting in a simpler, more responsive, and more reliable communication system.
