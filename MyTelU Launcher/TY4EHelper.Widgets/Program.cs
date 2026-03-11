using System.Runtime.InteropServices;
using Microsoft.Windows.Widgets.Providers;
using WinRT;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace TY4EHelper.Widgets
{
    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("00000001-0000-0000-C000-000000000046")]
    internal interface IClassFactory
    {
        [PreserveSig]
        int CreateInstance(IntPtr pUnkOuter, ref Guid riid, out IntPtr ppvObject);
        [PreserveSig]
        int LockServer(bool fLock);
    }

    [ComVisible(true)]
    class WidgetProviderFactory<T> : IClassFactory
    where T : IWidgetProvider, new()
    {
        public int CreateInstance(IntPtr pUnkOuter, ref Guid riid, out IntPtr ppvObject)
        {
            ppvObject = IntPtr.Zero;

            WidgetFileLogger.Write("Factory", $"CreateInstance requested riid={riid}, outerIsNull={pUnkOuter == IntPtr.Zero}");

            if (pUnkOuter != IntPtr.Zero)
            {
                WidgetFileLogger.Write("Factory", "Rejecting aggregation request");
                Marshal.ThrowExceptionForHR(CLASS_E_NOAGGREGATION);
            }

            if (riid == typeof(T).GUID ||
                riid == typeof(IWidgetProvider).GUID ||
                riid == Guid.Parse(Guids.IUnknown) ||
                riid == Guid.Parse(Guids.IInspectable))
            {
                // Create the instance of the .NET object
                ppvObject = MarshalInspectable<IWidgetProvider>.FromManaged(new T());
                WidgetFileLogger.Write("Factory", $"CreateInstance succeeded for riid={riid}");
            }
            else
            {
                WidgetFileLogger.Write("Factory", $"CreateInstance rejected unsupported riid={riid}");
                // The object that ppvObject points to does not support the
                // interface identified by riid.
                Marshal.ThrowExceptionForHR(E_NOINTERFACE);
            }

            return 0;
        }

        int IClassFactory.LockServer(bool fLock)
        {
            return 0;
        }

        private const int CLASS_E_NOAGGREGATION = -2147221232;
        private const int E_NOINTERFACE = -2147467262;
    }

    static class Guids
    {
        public const string IClassFactory = "00000001-0000-0000-C000-000000000046";
        public const string IUnknown = "00000000-0000-0000-C000-000000000046";
        public const string IInspectable = "AF86E2E0-B12D-4C6A-9C5A-D7AA65101E90";
    }

    class Program
    {
        [DllImport("ole32.dll")]
        static extern int CoRegisterClassObject(
            [MarshalAs(UnmanagedType.LPStruct)] Guid rclsid,
            [MarshalAs(UnmanagedType.IUnknown)] object pUnk,
            uint dwClsContext,
            uint flags,
            out uint lpdwRegister);

        [DllImport("kernel32.dll")]
        static extern IntPtr GetConsoleWindow();

        [DllImport("user32.dll")]
        static extern bool ShowWindow(IntPtr hWnd, int cmdShow);

        [DllImport("ole32.dll")] static extern int CoRevokeClassObject(uint dwRegister);

        static void Main(string[] args)
        {
            AppDomain.CurrentDomain.UnhandledException += (_, eventArgs) =>
            {
                if (eventArgs.ExceptionObject is Exception ex)
                {
                    WidgetFileLogger.WriteException("Program", ex, "Unhandled exception");
                }
                else
                {
                    WidgetFileLogger.Write("Program", $"Unhandled non-exception object: {eventArgs.ExceptionObject}");
                }
            };

            TaskScheduler.UnobservedTaskException += (_, eventArgs) =>
            {
                WidgetFileLogger.WriteException("Program", eventArgs.Exception, "Unobserved task exception");
                eventArgs.SetObserved();
            };

            WidgetFileLogger.Write("Program", $"Widget provider process starting. PID={Environment.ProcessId}, LogPath={WidgetFileLogger.LogPath}");

            // Hide the console window
            var handle = GetConsoleWindow();
            if (handle != IntPtr.Zero)
            {
                ShowWindow(handle, 0); // SW_HIDE
            }

            // Initialize CsWinRT
            ComWrappersSupport.InitializeComWrappers();
            WidgetFileLogger.Write("Program", "CsWinRT COM wrappers initialized");

            var factory = new WidgetProviderFactory<WidgetProvider>();
            // This CLSID must match the one in Package.appxmanifest
            Guid clsid = Guid.Parse("94819777-622C-4BA7-8A7C-0C023EFB31B1");

            uint cookie;
            // CLSCTX_LOCAL_SERVER = 4, REGCLS_MULTIPLEUSE = 1
            int result = CoRegisterClassObject(clsid, factory, 4, 1, out cookie);
            WidgetFileLogger.Write("Program", $"CoRegisterClassObject result={result}, cookie={cookie}, clsid={clsid}");

            if (result < 0)
            {
                // Failed
                WidgetFileLogger.Write("Program", "COM registration failed; exiting process");
                return;
            }

            WidgetFileLogger.Write("Program", "COM registration succeeded; waiting for widget host callbacks");
            
            // Wait indefinitely
            using (var eventHandle = new ManualResetEvent(false))
            {
                eventHandle.WaitOne();
            }
            
            CoRevokeClassObject(cookie);
            WidgetFileLogger.Write("Program", "COM registration revoked; process exiting");
        }
    }
}
