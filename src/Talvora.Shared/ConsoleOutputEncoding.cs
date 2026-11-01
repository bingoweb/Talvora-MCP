using System.Runtime.InteropServices;
using System.Text;

namespace Talvora.Shared;

// W1: console programs write the OEM code page (857 on Turkish Windows) unless they choose UTF-8 themselves.
// Valid UTF-8 stays UTF-8; every byte that is not valid UTF-8 is read through the OEM code page.
public static class ConsoleOutputEncoding
{
    public static Encoding Instance { get; } = Create((int)GetOEMCP());

    public static Encoding Create(int oemCodePage)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        return Encoding.GetEncoding(
            65001,
            EncoderFallback.ReplacementFallback,
            new OemDecoderFallback(Encoding.GetEncoding(oemCodePage)));
    }

    [DllImport("kernel32.dll")]
    private static extern uint GetOEMCP();

    private sealed class OemDecoderFallback(Encoding oem) : DecoderFallback
    {
        public override int MaxCharCount => 4;

        public override DecoderFallbackBuffer CreateFallbackBuffer() => new OemDecoderFallbackBuffer(oem);
    }

    private sealed class OemDecoderFallbackBuffer(Encoding oem) : DecoderFallbackBuffer
    {
        private string _chars = string.Empty;
        private int _index;

        public override int Remaining => _chars.Length - _index;

        public override bool Fallback(byte[] bytesUnknown, int index)
        {
            _chars = oem.GetString(bytesUnknown);
            _index = 0;
            return _chars.Length > 0;
        }

        public override char GetNextChar() => _index < _chars.Length ? _chars[_index++] : '\0';

        public override bool MovePrevious()
        {
            if (_index == 0)
            {
                return false;
            }

            _index--;
            return true;
        }

        public override void Reset()
        {
            _chars = string.Empty;
            _index = 0;
        }
    }
}
