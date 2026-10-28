using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using Windows.Foundation;

namespace MyTelU_Launcher.Helpers;

internal static class PrintUIInterop
{
    // The SDK's generic async projection uses reflection here. Keep this COM boundary generated for Native AOT.
    internal static async Task<bool> ShowAsync(nint window)
    {
        nint operation = Start(window);
        var completed = new PrintUICompletedHandler();
        try
        {
            Subscribe(operation, completed);
            return await completed.Completion.Task;
        }
        finally
        {
            Marshal.Release(operation);
            GC.KeepAlive(completed);
        }
    }

    private static unsafe nint Start(nint window)
    {
        using var factory = WinRT.ActivationFactory.Get("Windows.Graphics.Printing.PrintManager",
            new Guid("C5435A42-8D43-4E7B-A68A-EF311E392087"));
        var operationId = new Guid("CDB5EFB3-5788-509D-9BE1-71CCB8A3362A");
        nint operation = 0;
        var show = (delegate* unmanaged[Stdcall]<nint, nint, Guid*, nint*, int>)(*(nint**)factory.ThisPtr)[7];
        int result = show(factory.ThisPtr, window, &operationId, &operation);
        if (result < 0)
        {
            if (operation != 0) Marshal.Release(operation);
            Marshal.ThrowExceptionForHR(result);
        }
        return operation;
    }

    private static unsafe void Subscribe(nint operation, IPrintUICompletedHandler completed)
    {
        void* callback = ComInterfaceMarshaller<IPrintUICompletedHandler>.ConvertToUnmanaged(completed);
        try
        {
            var setCompleted = (delegate* unmanaged[Stdcall]<nint, void*, int>)(*(nint**)operation)[6];
            Marshal.ThrowExceptionForHR(setCompleted(operation, callback));
        }
        finally { ComInterfaceMarshaller<IPrintUICompletedHandler>.Free(callback); }
    }

    internal static unsafe bool GetResult(nint operation)
    {
        byte shown = 0;
        var getResults = (delegate* unmanaged[Stdcall]<nint, byte*, int>)(*(nint**)operation)[8];
        Marshal.ThrowExceptionForHR(getResults(operation, &shown));
        return shown != 0;
    }
}

[GeneratedComInterface]
[Guid("C1D3D1A2-AE17-5A5F-B5A2-BDCC8844889A")]
internal partial interface IPrintUICompletedHandler
{
    void Invoke(nint operation, AsyncStatus status);
}

[GeneratedComClass]
internal sealed partial class PrintUICompletedHandler : IPrintUICompletedHandler
{
    internal TaskCompletionSource<bool> Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public void Invoke(nint operation, AsyncStatus status)
    {
        try { Completion.TrySetResult(status != AsyncStatus.Canceled && PrintUIInterop.GetResult(operation)); }
        catch (Exception ex) { Completion.TrySetException(ex); }
    }
}
