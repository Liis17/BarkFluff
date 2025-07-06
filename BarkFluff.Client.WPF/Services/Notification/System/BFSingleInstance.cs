using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Text;

namespace BarkFluff.Client.WPF.Services.Notification.System
{
    public static class BFSingleInstance
    {
        private const string PipeName = "bf_pipe_instance";

        public static async Task ListenAsync(Action<string> onMessageReceived, CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                using (var server = new NamedPipeServerStream(PipeName, PipeDirection.In))
                {
                    try
                    {
                        await server.WaitForConnectionAsync(token);

                        using (var reader = new StreamReader(server))
                        {
                            var message = await reader.ReadLineAsync();
                            if (message != null)
                                onMessageReceived?.Invoke(message);
                        }
                    }
                    catch (OperationCanceledException) { break; }
                    catch (Exception) { /* log если нужно */ }
                }
            }
        }

        public static bool SendToExistingInstance(string message)
        {
            try
            {
                using (var client = new NamedPipeClientStream(".", PipeName, PipeDirection.Out))
                {
                    client.Connect(1000); // таймаут 1 секунда
                    using (var writer = new StreamWriter(client) { AutoFlush = true })
                    {
                        writer.WriteLine(message);
                        return true;
                    }
                }
            }
            catch
            {
                return false;
            }
        }
    }
}
