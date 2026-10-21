using System;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Bimwright.Dwg.Tests
{
    /// <summary>
    /// A real NamedPipeServerStream that accepts connections and replies to
    /// each NDJSON line with a caller-supplied canned response. Mirrors the
    /// real plugin's PipeTransportServer closely enough to exercise
    /// PluginClient.SendPipeAsync end to end, including stream disposal.
    /// </summary>
    public class FakeNamedPipeServer : IDisposable
    {
        private readonly Func<string, string> _responder;
        private readonly CancellationTokenSource _cts = new CancellationTokenSource();
        private readonly TaskCompletionSource<bool> _listening =
            new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly Task _serverTask;
        private NamedPipeServerStream _listeningPipe;
        public string PipeName { get; }
        public Task Completion => _serverTask;

        public FakeNamedPipeServer(Func<string, string> responder)
        {
            _responder = responder;
            PipeName = "bimwright-dwg-test-" + Guid.NewGuid().ToString("N");
            _serverTask = Task.Run(async () =>
            {
                try { await AcceptLoop(_cts.Token); }
                catch (Exception ex)
                {
                    _listening.TrySetException(ex);
                    throw;
                }
            });
            if (!_listening.Task.Wait(TimeSpan.FromSeconds(5)))
                throw new TimeoutException("FakeNamedPipeServer did not start listening.");
        }

        private async Task AcceptLoop(CancellationToken ct)
        {
            var signaled = false;
            while (!ct.IsCancellationRequested)
            {
                NamedPipeServerStream pipe;
                try
                {
                    pipe = await CreateServerPipeAsync(ct);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                _listeningPipe = pipe;
                if (!signaled)
                {
                    signaled = true;
                    _listening.TrySetResult(true);
                }

                StreamReader reader = null;
                StreamWriter writer = null;
                try
                {
                    try { await pipe.WaitForConnectionAsync(ct); }
                    catch (OperationCanceledException) when (ct.IsCancellationRequested) { return; }
                    catch (ObjectDisposedException) when (ct.IsCancellationRequested) { return; }
                    catch (IOException) when (ct.IsCancellationRequested) { return; }

                    reader = new StreamReader(pipe, new UTF8Encoding(false), true, -1, leaveOpen: true);
                    writer = new StreamWriter(pipe, new UTF8Encoding(false), -1, leaveOpen: true) { AutoFlush = true };
                    while (!ct.IsCancellationRequested)
                    {
                        string line;
                        try { line = await reader.ReadLineAsync(ct); }
                        catch (IOException) { break; }
                        catch (ObjectDisposedException) { break; }
                        catch (OperationCanceledException) when (ct.IsCancellationRequested) { break; }
                        if (line == null) break;

                        // Keep responder assertions outside transport catches: a
                        // fixture failure must fail the test, not look like EOF.
                        var reply = _responder(line);
                        try { await writer.WriteLineAsync(reply); }
                        catch (IOException) { break; }
                        catch (ObjectDisposedException) { break; }
                    }
                }
                finally
                {
                    // EOF marks a Windows pipe Broken. StreamWriter.Dispose
                    // still flushes with leaveOpen=true, so it may throw here.
                    // That disconnect must not terminate the accept loop.
                    DisposeDisconnected(writer);
                    DisposeDisconnected(reader);
                    DisposeDisconnected(pipe);
                    _listeningPipe = null;
                }
            }
        }

        private async Task<NamedPipeServerStream> CreateServerPipeAsync(CancellationToken ct)
        {
            // Linux keeps the pipe name busy briefly after the previous instance
            // is disposed; aborting the loop there made sequential PluginClient
            // calls fail on CI while Windows usually reused the name immediately.
            for (var attempt = 0; attempt < 50; attempt++)
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    return new NamedPipeServerStream(
                        PipeName,
                        PipeDirection.InOut,
                        1,
                        PipeTransmissionMode.Byte,
                        PipeOptions.Asynchronous);
                }
                catch (IOException) when (attempt < 49)
                {
                    await Task.Delay(20, ct);
                }
            }

            throw new IOException("Could not recreate named pipe server '" + PipeName + "'.");
        }

        public void Dispose()
        {
            _cts.Cancel();
            DisposeDisconnected(_listeningPipe);
            try { _serverTask.WaitAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult(); }
            finally { _cts.Dispose(); }
        }

        private static void DisposeDisconnected(IDisposable stream)
        {
            try { stream?.Dispose(); }
            catch (IOException) { }
            catch (ObjectDisposedException) { }
        }
    }
}
