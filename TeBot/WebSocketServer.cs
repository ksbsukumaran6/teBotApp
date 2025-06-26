using System;
using System.Collections.Concurrent;
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
            Debug.WriteLine("WebSocket client connected");
        }

        protected override void OnClose(CloseEventArgs e)
        {
            Debug.WriteLine($"WebSocket client disconnected: {e.Reason}");
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
                
                // Subscribe to the global event
                DataReceiver.GlobalDataReceived += OnDataReceived;

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
            try
            {
                if (_server != null && _isRunning)
                {
                    DataReceiver.GlobalDataReceived -= OnDataReceived;
                    _server.Stop();
                    _isRunning = false;
                    Debug.WriteLine("WebSocket server stopped");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error stopping WebSocket server: {ex.Message}");
            }
        }

        private void OnDataReceived(byte[] data)
        {
            DataReceived?.Invoke(data);
        }

        public void Dispose()
        {
            Stop();
        }
    }
}
