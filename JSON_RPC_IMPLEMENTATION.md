# JSON-RPC Protocol Implementation

This document describes the implementation of the JSON-RPC protocol in the TeBot application for communication between Scratch and the robot.

## JSON-RPC Protocol Overview

The TeBot application implements the [JSON-RPC 2.0 specification](https://www.jsonrpc.org/specification) for communication between Scratch and the robot. All messages sent between Scratch and the robot are formatted as JSON-RPC requests and responses, newline-terminated.

## Message Flow

The communication flow is as follows:

1. Scratch sends a JSON-RPC request to the TeBot application via WebSocket
2. The TeBot application processes the request using the `HandleJsonRpcRequest` method in `BluetoothManager.cs`
3. The TeBot application sends a JSON-RPC response back to Scratch via WebSocket
4. Additionally, the TeBot application automatically sends sensor data from the robot to Scratch as JSON-RPC notifications

## Request Types

### Status Request

Scratch can request the current status of the robot using the `status` method:

```json
{
  "jsonrpc": "2.0",
  "method": "status",
  "id": 1
}
```

The TeBot application will respond with a JSON-RPC success response containing the sensor data:

```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "result": {
    "raw": "00 01 64 64 32 00 00 00 00 00 00 00 00 00 00 97",
    "sensors": {
      "statusCode": 0,
      "direction": "forward",
      "speed": 100,
      "batteryLevel": 100,
      "ultrasonicDistance": 50,
      "irValue": 0,
      "lightValue": 0,
      "buttonA": false,
      "buttonB": false,
      "ledOn": false,
      "isMoving": false,
      "commandCount": 0,
      "checksumValid": true,
      "isValid": true
    },
    "ageMs": 120
  }
}
```

### Robot Commands

Scratch can send commands to the robot using various method names:

- `moveforward`
- `movebackward`
- `turnleft`
- `turnright`
- `stop`
- `setspeed`
- `setled`
- `playtone`
- `setneomatrix`

Example request:

```json
{
  "jsonrpc": "2.0",
  "method": "moveforward",
  "params": {
    "speed": 100
  },
  "id": 2
}
```

The TeBot application will respond with a JSON-RPC success response:

```json
{
  "jsonrpc": "2.0",
  "id": 2,
  "result": {}
}
```

## Implementation Details

### BluetoothManager.cs

The `BluetoothManager` class implements the `HandleJsonRpcRequest` method that processes JSON-RPC requests from Scratch. It validates the request, parses the method and parameters, and returns the appropriate response.

- `HandleJsonRpcRequest`: Processes JSON-RPC requests and returns JSON-RPC responses
- `CreateJsonRpcSuccessResponse`: Creates a JSON-RPC success response
- `CreateJsonRpcErrorResponse`: Creates a JSON-RPC error response
- `GetStatusJson`: Creates a JSON-RPC response with the current sensor data

### Form1.cs

The `Form1` class handles WebSocket communication and routes JSON-RPC requests to the `BluetoothManager`:

- `OnDataReceived`: Parses incoming WebSocket messages and routes JSON-RPC requests to `HandleJsonRpcRequest`
- `OnStatusJsonPushed`: Forwards automatic sensor data updates to Scratch

## Protocol Versioning

The current protocol implementation is version 1.0. All messages must include the `jsonrpc` field with the value `"2.0"` to be properly processed.

## Error Handling

The TeBot application returns JSON-RPC error responses for invalid requests:

- `-32600`: Invalid Request - The JSON sent is not a valid JSON-RPC request
- `-32601`: Method not found - The method does not exist / is not available
- `-32700`: Parse error - Invalid JSON was received
- `-32603`: Internal error - Internal error during processing

Example error response:

```json
{
  "jsonrpc": "2.0",
  "error": {
    "code": -32601,
    "message": "Method not found"
  },
  "id": 3
}
```

## Automatic Status Updates

The TeBot application automatically sends sensor data updates to Scratch at regular intervals (when polling the robot). These updates are sent as JSON-RPC notifications (no `id` field) and include the latest sensor data.

## Integration

The JSON-RPC protocol is fully integrated with the WebSocket server and the Bluetooth communication with the robot. All WebSocket messages are processed as JSON-RPC requests, and all responses follow the JSON-RPC format.
