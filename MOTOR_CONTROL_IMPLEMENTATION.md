# TeBot Motor Control Implementation

I've implemented motor control functionality in the BluetoothManager class. Here's what I've added:

1. **Command Constants:**
```csharp
// Command constants for robot control
private const byte CMD_MOVE_FORWARD = 0x01;
private const byte CMD_MOVE_BACKWARD = 0x02;
private const byte CMD_TURN_LEFT = 0x03;
private const byte CMD_TURN_RIGHT = 0x04;
private const byte CMD_STOP = 0x05;
private const byte CMD_SET_SPEED = 0x06;
private const byte CMD_SET_LED = 0x07;
private const byte CMD_PLAY_TONE = 0x08;
private const byte CMD_SET_NEOMATRIX = 0x09;
```

2. **Command Packet Creation Method:**
```csharp
/// <summary>
/// Creates an 8-byte command packet for the robot based on JSON-RPC method and parameters
/// </summary>
private byte[] CreateCommandPacket(string method, Dictionary<string, object> parameters)
{
    byte[] commandPacket = new byte[8]; // All bytes initialized to 0 by default
    
    // Default speed to use if not specified in parameters
    byte speed = 50;
    
    // Extract speed parameter if present
    if (parameters != null && parameters.TryGetValue("speed", out object speedObj))
    {
        // Try to parse the speed parameter (handle both string and numeric formats)
        if (speedObj is long longSpeed)
        {
            speed = (byte)Math.Min(100, Math.Max(0, longSpeed));
        }
        else if (speedObj is int intSpeed)
        {
            speed = (byte)Math.Min(100, Math.Max(0, intSpeed));
        }
        else if (speedObj is double doubleSpeed)
        {
            speed = (byte)Math.Min(100, Math.Max(0, doubleSpeed));
        }
        else if (speedObj is string speedStr && byte.TryParse(speedStr, out byte parsedSpeed))
        {
            speed = Math.Min((byte)100, parsedSpeed);
        }
    }
    
    // Set command type based on method
    switch (method.ToLowerInvariant())
    {
        case "moveforward":
            commandPacket[0] = CMD_MOVE_FORWARD;
            commandPacket[1] = speed;
            break;
            
        case "movebackward":
            commandPacket[0] = CMD_MOVE_BACKWARD;
            commandPacket[1] = speed;
            break;
            
        case "turnleft":
            commandPacket[0] = CMD_TURN_LEFT;
            commandPacket[1] = speed;
            break;
            
        case "turnright":
            commandPacket[0] = CMD_TURN_RIGHT;
            commandPacket[1] = speed;
            break;
            
        case "stop":
            commandPacket[0] = CMD_STOP;
            break;
            
        case "setspeed":
            commandPacket[0] = CMD_SET_SPEED;
            commandPacket[1] = speed;
            break;
            
        case "setled":
            commandPacket[0] = CMD_SET_LED;
            if (parameters != null && parameters.TryGetValue("packed", out object packedObj))
            {
                // Try to parse the packed LED parameter
                if (packedObj is long longPacked)
                    commandPacket[1] = (byte)longPacked;
                else if (packedObj is int intPacked)
                    commandPacket[1] = (byte)intPacked;
                else if (packedObj is string packedStr && byte.TryParse(packedStr, out byte parsedPacked))
                    commandPacket[1] = parsedPacked;
            }
            break;
            
        case "playtone":
            commandPacket[0] = CMD_PLAY_TONE;
            if (parameters != null)
            {
                // Get b1, b2, b3 parameters if present
                if (parameters.TryGetValue("b1", out object b1Obj) && b1Obj is long b1Long)
                    commandPacket[1] = (byte)b1Long;
                if (parameters.TryGetValue("b2", out object b2Obj) && b2Obj is long b2Long)
                    commandPacket[2] = (byte)b2Long;
                if (parameters.TryGetValue("b3", out object b3Obj) && b3Obj is long b3Long)
                    commandPacket[3] = (byte)b3Long;
            }
            break;
            
        case "setneomatrix":
            commandPacket[0] = CMD_SET_NEOMATRIX;
            if (parameters != null)
            {
                // Get p1, p2, p3, p4 parameters if present
                if (parameters.TryGetValue("p1", out object p1Obj) && p1Obj is long p1Long)
                    commandPacket[1] = (byte)p1Long;
                if (parameters.TryGetValue("p2", out object p2Obj) && p2Obj is long p2Long)
                    commandPacket[2] = (byte)p2Long;
                if (parameters.TryGetValue("p3", out object p3Obj) && p3Obj is long p3Long)
                    commandPacket[3] = (byte)p3Long;
                if (parameters.TryGetValue("p4", out object p4Obj) && p4Obj is long p4Long)
                    commandPacket[4] = (byte)p4Long;
            }
            break;
            
        default:
            // Unknown method, use poll command as fallback
            commandPacket[0] = TEBOT_POLL_COMMAND;
            break;
    }
    
    return commandPacket;
}
```

3. **Thread-safe Command Sending Method:**
```csharp
/// <summary>
/// Sends a command packet to the robot in a way that won't interfere with polling
/// This method is thread-safe and ensures commands and polls don't overlap
/// </summary>
private void SendCommandPacketSafe(byte[] commandPacket)
{
    if (commandPacket == null || commandPacket.Length == 0)
        return;
        
    // Ensure we have an 8-byte packet (required by the protocol)
    byte[] paddedPacket = new byte[8];
    Array.Copy(commandPacket, paddedPacket, Math.Min(commandPacket.Length, 8));
    
    // Use a lock to ensure commands don't interfere with polling
    lock (_robotDataLock)
    {
        if (_bluetoothStream != null && _bluetoothStream.CanWrite)
        {
            try
            {
                // Send the command
                _bluetoothStream.Write(paddedPacket, 0, paddedPacket.Length);
                _bluetoothStream.Flush();
                
                // Release the receive semaphore to allow the receiver to process any response
                // This is crucial as the robot may send back a status update after a command
                _receiveSemaphore.Release();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[COMMAND] Error sending command: {ex.Message}");
                throw; // Re-throw to notify caller
            }
        }
        else
        {
            throw new InvalidOperationException("Bluetooth stream not available for writing");
        }
    }
}
```

4. **Updated SendDataImmediately Methods:**
```csharp
/// <summary>
/// Send data immediately - optimized bridge from Scratch to robot
/// Accepts either a byte array or a hex string (for minimal data traffic)
/// POLICY: Only send commands when Scratch is connected
/// </summary>
public Task<bool> SendDataImmediately(string hex)
{
    if (string.IsNullOrWhiteSpace(hex))
        return Task.FromResult(false);

    // Convert hex string to byte array
    byte[] data = null;
    try
    {
        data = HexUtils.HexStringToBytes(hex);
    }
    catch (Exception ex)
    {
        StatusChanged?.Invoke($"❌ Invalid hex string: {ex.Message}");
        return Task.FromResult(false);
    }

    // Call the byte array version
    return SendDataImmediately(data);
}

/// <summary>
/// Overload: Send data immediately using a byte array (for compatibility)
/// </summary>
public Task<bool> SendDataImmediately(byte[] data)
{
    if (data == null || data.Length == 0)
    {
        return Task.FromResult(false);
    }

    if (!_isConnected)
    {
        StatusChanged?.Invoke("❌ Not connected to any Bluetooth device");
        return Task.FromResult(false);
    }

    // Only send commands if Scratch is connected
    if (!_isScratchConnected)
    {
        StatusChanged?.Invoke("❌ Scratch is disconnected, command ignored for safety");
        return Task.FromResult(false);
    }

    try
    {
        // Convert to hex string for logging
        var hex = HexUtils.BytesToHexString(data);
        
        // Send the command packet to the robot using the thread-safe method
        SendCommandPacketSafe(data);
        
        StatusChanged?.Invoke($"✅ Command sent: {data.Length} bytes → {hex}");
        return Task.FromResult(true);
    }
    catch (Exception e)
    {
        StatusChanged?.Invoke($"❌ Error sending command: {e.Message}");
        return Task.FromResult(false);
    }
}
```

5. **Updated JSON-RPC Request Handler:**
```csharp
// Handle different method types according to the protocol table
switch (method.ToLowerInvariant())
{
    case "status":
        // For status requests, return the latest sensor data
        return Task.FromResult(GetStatusJson(requestId) + "\n");

    case "moveforward":
    case "movebackward":
    case "turnleft":
    case "turnright":
    case "stop":
    case "setspeed":
    case "setled":
    case "playtone":
    case "setneomatrix":
        try
        {
            // Log the request
            StatusChanged?.Invoke($"Received JSON-RPC '{method}' command with params: {JsonConvert.SerializeObject(parameters)}");
            
            // Create the command packet for the robot
            byte[] commandPacket = CreateCommandPacket(method, parameters);
            
            // Send the command to the robot
            bool success = SendDataImmediately(commandPacket).Result;
            
            if (success)
            {
                // Return success response
                var responseResult = new Dictionary<string, object>
                {
                    ["success"] = true,
                    ["method"] = method
                };
                
                if (parameters != null)
                    responseResult["params"] = parameters;
                    
                return Task.FromResult(CreateJsonRpcSuccessResponse(requestId, responseResult) + "\n");
            }
            else
            {
                // Return error response for command failure
                return Task.FromResult(CreateJsonRpcErrorResponse("Failed to send command to robot", -32000, requestId) + "\n");
            }
        }
        catch (Exception ex)
        {
            // Return error for any exceptions during command execution
            return Task.FromResult(CreateJsonRpcErrorResponse($"Command error: {ex.Message}", -32002, requestId) + "\n");
        }

    default:
        return Task.FromResult(CreateJsonRpcErrorResponse($"Unknown method: {method}", -32601, requestId) + "\n");
}
```

## Implementation Details

1. **Command Constants**: Added byte constants for each robot command type according to the protocol.

2. **Command Packet Creation**: Created a method that converts JSON-RPC methods and parameters into the appropriate 8-byte command packets for the robot.

3. **Thread-safe Sending**: Implemented a thread-safe method for sending commands that prevents conflicts with the polling mechanism.

4. **Re-enabled Command Sending**: Updated the SendDataImmediately methods to actually send commands to the robot instead of ignoring them.

5. **JSON-RPC Integration**: Updated the JSON-RPC request handler to use the new command handling functionality.

## How It Prevents Conflicts with Polling

1. **Lock-based Synchronization**: Uses the existing _robotDataLock to ensure that commands don't interfere with polling operations.

2. **Semaphore Signaling**: After sending a command, it releases the _receiveSemaphore to allow the receiver thread to process any response from the robot.

3. **Packet Validation**: Both commands and responses maintain the protocol structure (8-byte commands, 16-byte responses).

These changes should allow the TeBot application to properly handle both motor control commands from Scratch and continuous polling for sensor data.
