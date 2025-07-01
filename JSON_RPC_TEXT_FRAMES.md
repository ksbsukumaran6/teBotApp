# JSON-RPC WebSocket Text Frame Optimization

## Issue Identified
The TeBot server was sending properly formatted JSON-RPC messages to Scratch, but they were being transmitted as WebSocket binary frames (ArrayBuffer) rather than as WebSocket text frames. While the content was correct, this could cause issues with how Scratch interprets the messages.

## Changes Made

1. **Added a Text-Specific WebSocket Method**:
   - Created a new `SendTextToAllClientsAsync(string textData)` method in `WebSocketServer.cs`
   - This method ensures JSON-RPC messages are sent as WebSocket text frames, not binary frames

2. **Updated JSON-RPC Communication Points**:
   - Modified `OnStatusJsonPushed` in Form1.cs to use the new text-specific method
   - Updated the JSON-RPC request handler to send responses as text frames
   - Ensured all JSON communications with Scratch use the proper frame type

3. **Preserved Binary Support**:
   - Kept the original `SendToAllClientsAsync(byte[] data)` method for any legitimate binary data
   - Ensured backward compatibility while improving protocol compliance

## Benefits

1. **Protocol Compliance**: WebSocket protocol specifies text frames for text data (like JSON) and binary frames for binary data
2. **Better Scratch Integration**: Scratch WebSocket implementations may expect JSON-RPC messages as text frames
3. **Easier Debugging**: Text frames can be inspected and logged more easily than binary frames
4. **Clearer Intent**: Code now clearly distinguishes between text and binary data transmission

## How It Works

When the JSON-RPC handler in `BluetoothManager` creates a JSON response:
1. The response is passed as a string to Form1.cs
2. Form1.cs calls `SendTextToAllClientsAsync` to send the JSON as a text frame
3. WebSocketServer transmits this as a WebSocket text frame
4. Scratch receives proper text data instead of binary data that contains text

This optimization should resolve any issues related to WebSocket frame type mismatch between the TeBot server and Scratch client.
