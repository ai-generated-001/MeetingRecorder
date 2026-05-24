using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Windows.Forms;

namespace MeetingRecorder.Services;

public sealed class ScreenCaptureService : IScreenCaptureService
{
    public byte[] CapturePrimaryScreenPng()
    {
        var bounds = Screen.PrimaryScreen?.Bounds ?? throw new InvalidOperationException("Primary screen is not available.");

        using var bitmap = new Bitmap(bounds.Width, bounds.Height);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.CopyFromScreen(bounds.Location, Point.Empty, bounds.Size);
        }

        using var stream = new MemoryStream();
        bitmap.Save(stream, ImageFormat.Png);
        return stream.ToArray();
    }
}
