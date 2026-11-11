using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using Windows.ApplicationModel.DataTransfer;

namespace MyTelU_Launcher.Helpers;

internal static class ShareUIInterop
{
    internal static DataTransferManager GetForWindow(nint window)
    {
        var iid = new Guid("A5CAEE9B-8708-49D1-8D36-67D25A8DA00C");
        nint result = GetInterop().GetForWindow(window, in iid);
        try { return WinRT.MarshalInterface<DataTransferManager>.FromAbi(result); }
        finally { Marshal.Release(result); }
    }

    internal static void ShowForWindow(nint window) => GetInterop().ShowShareUIForWindow(window);

    private static unsafe IDataTransferManagerInterop GetInterop()
    {
        using var factory = WinRT.ActivationFactory.Get("Windows.ApplicationModel.DataTransfer.DataTransferManager",
            typeof(IDataTransferManagerInterop).GUID);
        return ComInterfaceMarshaller<IDataTransferManagerInterop>.ConvertToManaged((void*)factory.ThisPtr)!;
    }
}

[GeneratedComInterface]
[Guid("3A3DCD6C-3EAB-43DC-BCDE-45671CE800C8")]
internal partial interface IDataTransferManagerInterop
{
    nint GetForWindow(nint window, in Guid iid);
    void ShowShareUIForWindow(nint window);
}
