using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using WebSocketSharp;
using WebSocketSharp.Server;

namespace TeBot
{
    public class DataReceiver : WebSocketBehavior
    {
        public static event Action<byte[]> GlobalDataReceived;
        public static event Action<string> SessionConnected;
        public static event Action<string> SessionDisconnected;

        protected override void OnMessage(MessageEventArgs e)
        {
            try
            {
                if (e.RawData != null && e.RawData.Length > 0)
                {
                    Debug.WriteLine($"Received {e.RawData.Length} bytes from WebSocket client");
                    
                    // Fire and forget to prevent blocking WebSocket thread
                    Task.Run(() => GlobalDataReceived?.Invoke(e.RawData));
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error processing WebSocket message: {ex.Message}");
            }
        }

        protected override void OnOpen()
        {
            Debug.WriteLine($"WebSocket client connected: {ID}");
            SessionConnected?.Invoke(ID);
        }

        protected override void OnClose(CloseEventArgs e)
        {
            Debug.WriteLine($"WebSocket client disconnected: {ID}, Reason: {e.Reason}");
            SessionDisconnected?.Invoke(ID);
        }

        protected override void OnError(ErrorEventArgs e)
        {
            Debug.WriteLine($"WebSocket error: {e.Message}");
        }
    }

    public class WebSocketDataServer
    {
        private WebSocketServer _server;
        private bool _isRunning;
        private readonly ConcurrentDictionary<string, IWebSocketSession> _sessions = new ConcurrentDictionary<string, IWebSocketSession>();

        public event Action<byte[]> DataReceived;

        public bool IsRunning => _isRunning;

        public bool Start(int port = 5000)
        {
            try
            {
                if (_isRunning)
                {
                    Debug.WriteLine("Server is already running");
                    return true;
                }

                _server = new WebSocketServer($"ws://localhost:{port}");
                
                // Configure for better performance
                _server.WaitTime = TimeSpan.FromSeconds(2);
                _server.KeepClean = false; // Reduce overhead
                
                _server.AddWebSocketService<DataReceiver>("/");
                
                // Subscribe to the global events
                DataReceiver.GlobalDataReceived += OnDataReceived;
                DataReceiver.SessionConnected += OnSessionConnected;
                DataReceiver.SessionDisconnected += OnSessionDisconnected;

                _server.Start();
                _isRunning = true;
                Debug.WriteLine($"WebSocket server started on port {port}");
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to start WebSocket server: {ex.Message}");
                return false;
            }
        }

        public void Stop()
        {
            StopAsync().Wait(TimeSpan.FromSeconds(5)); // 5 second timeout
        }

        public async Task StopAsync()
        {
            try
            {
                if (_server != null && _isRunning)
                {
                    Debug.WriteLine("Stopping WebSocket server...");
                    
                    // Set flag first to prevent new operations
                    _isRunning = false;
                    
                    // Unsubscribe from events first
                    DataReceiver.GlobalDataReceived -= OnDataReceived;
                    DataReceiver.SessionConnected -= OnSessionConnected;
                    DataReceiver.SessionDisconnected -= OnSessionDisconnected;
                    
                    // Clear sessions
                    _sessions.Clear();
                    
                    // Stop server in background task with timeout
                    var stopTask = Task.Run(() => 
                    {
                        try
                        {
                            _server.Stop();
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"Exception during server stop: {ex.Message}");
                        }
                    });
                    
                    // Wait for stop with timeout
                    if (await Task.WhenAny(stopTask, Task.Delay(3000)) == stopTask)
                    {
                        Debug.WriteLine("WebSocket server stopped gracefully");
                    }
                    else
                    {
                        Debug.WriteLine("WebSocket server stop timed out - forcing shutdown");
                    }
                    
                    _server = null;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error stopping WebSocket server: {ex.Message}");
                _isRunning = false;
                _server = null;
            }
        }

        private void OnDataReceived(byte[] data)
        {
            DataReceived?.Invoke(data);
        }

        private void OnSessionConnected(string sessionId)
        {
            Debug.WriteLine($"Session connected: {sessionId}");
        }

        private void OnSessionDisconnected(string sessionId)
        {
            Debug.WriteLine($"Session disconnected: {sessionId}");
            _sessions.TryRemove(sessionId, out _);
        }

        /// <summary>
        /// Send binary data to all connected WebSocket clients
        /// </summary>
        /// <param name="data">The binary data to send</param>
        /// <returns>Task representing the async operation</returns>
        public Task SendToAllClientsAsync(byte[] data)
        {
            if (!_isRunning || _server == null)
            {
                Debug.WriteLine("Cannot send data: server is not running");
                return Task.CompletedTask;
            }

            try
            {
                var service = _server.WebSocketServices["/"];
                if (service?.Sessions?.Count > 0)
                {
                    Debug.WriteLine($"Attempting to send {data.Length} bytes to {service.Sessions.Count} connected clients");
                    
                    // Send as binary data only (removed confusing text message)
                    service.Sessions.Broadcast(data);
                    
                    var hexString = BitConverter.ToString(data).Replace("-", " ");
                    Debug.WriteLine($"✅ Sent {data.Length} bytes as binary to {service.Sessions.Count} clients: {hexString}");
                }
                else
                {
                    Debug.WriteLine("❌ No connected clients to send data to");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"❌ Error sending data to clients: {ex.Message}");
            }
            
            return Task.CompletedTask;
        }
        
        /// <summary>
        /// Send text data to all connected WebSocket clients
        /// This method specifically sends data as WebSocket text frames (not binary frames)
        /// which is important for JSON-RPC messages that Scratch expects as text
        /// </summary>
        /// <param name="textData">The text data to send</param>
        /// <returns>Task representing the async operation</returns>
        public Task SendTextToAllClientsAsync(string textData)
        {
            if (!_isRunning || _server == null)
            {
                Debug.WriteLine("Cannot send text data: server is not running");
                return Task.CompletedTask;
            }

            try
            {
                var service = _server.WebSocketServices["/"];
                if (service?.Sessions?.Count > 0)
                {
                    Debug.WriteLine($"Attempting to send {textData.Length} chars as TEXT to {service.Sessions.Count} connected clients");
                    
                    // Send as text frames instead of binary frames
                    service.Sessions.Broadcast(textData);
                    
                    // Only log the first 100 characters to avoid cluttering logs
                    string previewText = textData.Length > 100 ? textData.Substring(0, 100) + "..." : textData;
                    Debug.WriteLine($"✅ Sent text message: {previewText}");
                }
                else
                {
                    Debug.WriteLine("❌ No connected clients to send text to");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"❌ Error sending text to clients: {ex.Message}");
            }
            
            return Task.CompletedTask;
        }

        public void Dispose()
        {
            try
            {
                Stop();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error during dispose: {ex.Message}");
            }
        }
    }
}
