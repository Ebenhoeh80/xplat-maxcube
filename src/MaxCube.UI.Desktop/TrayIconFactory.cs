using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Controls;

namespace MaxCube.UI.Desktop;

/// <summary>
/// Erzeugt das Tray-Symbol zur Laufzeit (orangefarbener Kreis mit weißem Punkt), damit keine
/// Binärdatei im Repository nötig ist und das Symbol auf Windows und Linux identisch aussieht.
/// </summary>
internal static class TrayIconFactory
{
    private const int Size = 32;

    public static WindowIcon Create()
    {
        var pixels = new byte[Size * Size * 4];
        var center = (Size - 1) / 2.0;
        for (var y = 0; y < Size; y++)
        {
            for (var x = 0; x < Size; x++)
            {
                var distance = Math.Sqrt(((x - center) * (x - center)) + ((y - center) * (y - center)));
                var index = ((y * Size) + x) * 4;
                if (distance <= 5)
                {
                    Write(pixels, index, 255, 255, 255, 255);
                }
                else if (distance <= 15)
                {
                    Write(pixels, index, 0xE0, 0x7B, 0x1F, 255);
                }
            }
        }

        var bitmap = new WriteableBitmap(
            new PixelSize(Size, Size), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Premul);
        using (var buffer = bitmap.Lock())
        {
            Marshal.Copy(pixels, 0, buffer.Address, pixels.Length);
        }

        return new WindowIcon(bitmap);
    }

    private static void Write(byte[] pixels, int index, byte red, byte green, byte blue, byte alpha)
    {
        pixels[index] = blue;
        pixels[index + 1] = green;
        pixels[index + 2] = red;
        pixels[index + 3] = alpha;
    }
}
