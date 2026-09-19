using System.Diagnostics;
using Talvora.Shared;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Windows.Forms;

namespace Talvora.Tray;

internal static class NativeDpapi
{
    [StructLayout(LayoutKind.Sequential)]
    private struct DataBlob
    {
        public int Length;
        public IntPtr Data;
    }

    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptUnprotectData(
        ref DataBlob dataIn,
        IntPtr description,
        IntPtr optionalEntropy,
        IntPtr reserved,
        IntPtr promptStruct,
        int flags,
        out DataBlob dataOut);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr LocalFree(IntPtr memory);

    public static byte[] Unprotect(byte[] encrypted)
    {
        var inputPointer = Marshal.AllocHGlobal(encrypted.Length);
        try
        {
            Marshal.Copy(encrypted, 0, inputPointer, encrypted.Length);
            var input = new DataBlob
            {
                Length = encrypted.Length,
                Data = inputPointer,
            };

            if (!CryptUnprotectData(
                    ref input,
                    IntPtr.Zero,
                    IntPtr.Zero,
                    IntPtr.Zero,
                    IntPtr.Zero,
                    0,
                    out var output))
            {
                throw new InvalidOperationException(
                    $"DPAPI çözme başarısız. Win32={Marshal.GetLastWin32Error()}");
            }

            try
            {
                var result = new byte[output.Length];
                Marshal.Copy(output.Data, result, 0, output.Length);
                return result;
            }
            finally
            {
                if (output.Data != IntPtr.Zero)
                {
                    _ = LocalFree(output.Data);
                }
            }
        }
        finally
        {
            Marshal.FreeHGlobal(inputPointer);
        }
    }
}
