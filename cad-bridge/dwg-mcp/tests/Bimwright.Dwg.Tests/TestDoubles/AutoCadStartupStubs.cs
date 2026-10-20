// Peripheral host surfaces only. App and DocumentInvoker are the real linked
// production implementations, not reproduced state machines.
using System;

namespace Autodesk.AutoCAD.Runtime
{
    public interface IExtensionApplication { void Initialize(); void Terminate(); }
    [AttributeUsage(AttributeTargets.Assembly)]
    public sealed class ExtensionApplicationAttribute : Attribute { public ExtensionApplicationAttribute(Type value) { } }
    [AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]
    public sealed class CommandClassAttribute : Attribute { public CommandClassAttribute(Type value) { } }
    public enum CommandFlags { Session }
    [AttributeUsage(AttributeTargets.Method)]
    public sealed class CommandMethodAttribute : Attribute { public CommandMethodAttribute(string name, CommandFlags flags) { } }
}

namespace System.Windows.Threading
{
    public sealed class Dispatcher { public static Dispatcher CurrentDispatcher { get; } = new Dispatcher(); }
}
namespace System.Windows
{
    public sealed class Application
    {
        public static Application Current => null;
        public Threading.Dispatcher Dispatcher => Threading.Dispatcher.CurrentDispatcher;
    }
}

namespace Bimwright.Dwg.Plugin
{
    internal static class StartupHostFake
    {
        internal static int Constructed, Started, Stopped, ToastShutdowns;
        internal static bool FailNextStart;
        internal static Exception ToastFlushError;
        internal static void Reset()
        {
            Constructed = Started = Stopped = ToastShutdowns = 0;
            FailNextStart = false;
            ToastFlushError = null;
        }
    }
    public sealed class PipeTransportServer : ITransportServer
    {
        public PipeTransportServer(string year) { StartupHostFake.Constructed++; }
        public int? Port => null;
        public string PipeName => "test-only-pipe";
        public string AuthToken => "";
        public bool IsRunning { get; private set; }
        public bool IsClientConnected => false;
        public DateTime? LastCommandTime => null;
        public TransportKind Kind => TransportKind.Pipe;
        public string ConnectionInfo => "test-only";
        public void Start()
        {
            StartupHostFake.Started++;
            IsRunning = true;
            if (StartupHostFake.FailNextStart)
            {
                StartupHostFake.FailNextStart = false;
                throw new InvalidOperationException("listener failed after partial startup");
            }
        }
        public void Stop() { StartupHostFake.Stopped++; IsRunning = false; }
    }
    internal static class CommandDispatcher { internal static void SetSendCodeEnabled(bool enabled) { } }
}

namespace Bimwright.Dwg.Plugin.Views.Settings
{
    public sealed class SettingsWindow
    {
        public event EventHandler Closed;
        public void Activate() { }
        public void Close() => Closed?.Invoke(this, EventArgs.Empty);
    }
}

namespace Bimwright.Dwg.Plugin.Views.Toast
{
    public sealed class McpToastHost { }
    public sealed class McpToastNotifier
    {
        public McpToastNotifier(McpToastHost host, Func<bool> enabled) { }
        public void SetInstanceInfo(string value) { }
        public void SetOwnerHandle(IntPtr value) { }
        public void SetHostDispatcher(System.Windows.Threading.Dispatcher value) { }
        public void OnToastEnabledChanged(bool enabled, bool saved) { }
        public void OnClientConnected(string info) { }
        public void DismissAll() { }
        public void FlushPendingIfUsable()
        {
            if (StartupHostFake.ToastFlushError != null) throw StartupHostFake.ToastFlushError;
        }
        public void Shutdown() => StartupHostFake.ToastShutdowns++;
    }
}
