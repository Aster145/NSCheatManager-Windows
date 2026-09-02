using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

if (args.Length != 1) throw new ArgumentException("Pass the destination .ico path.");
var destination = Path.GetFullPath(args[0]);
Directory.CreateDirectory(Path.GetDirectoryName(destination)!);

const int size = 256;
var visual = new DrawingVisual();
using (var drawing = visual.RenderOpen())
{
    drawing.DrawRoundedRectangle(new SolidColorBrush(Color.FromRgb(39, 100, 216)), null, new Rect(8, 8, 240, 240), 52, 52);
    drawing.PushTransform(new TranslateTransform(32, 32));
    drawing.PushTransform(new ScaleTransform(8, 8));
    var white = Brushes.White;
    var pen = new Pen(white, 2) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
    var body = Geometry.Parse("M17.32,5 H6.68 A4,4 0 0 0 2.702,8.59 C2.696,8.642 2.692,8.691 2.685,8.742 C2.604,9.416 2,14.456 2,16 A3,3 0 0 0 5,19 C6,19 6.5,18.5 7,18 L8.414,16.586 A2,2 0 0 1 9.828,16 H14.172 A2,2 0 0 1 15.586,16.586 L17,18 C17.5,18.5 18,19 19,19 A3,3 0 0 0 22,16 C22,14.455 21.396,9.416 21.315,8.742 C21.308,8.692 21.304,8.642 21.298,8.591 A4,4 0 0 0 17.32,5 Z");
    drawing.DrawGeometry(null, pen, body);
    drawing.DrawLine(pen, new Point(6, 11), new Point(10, 11));
    drawing.DrawLine(pen, new Point(8, 9), new Point(8, 13));
    drawing.DrawEllipse(white, null, new Point(15, 12), 1, 1);
    drawing.DrawEllipse(white, null, new Point(18, 10), 1, 1);
    drawing.Pop();
    drawing.Pop();
}

var bitmap = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
bitmap.Render(visual);
var encoder = new PngBitmapEncoder();
encoder.Frames.Add(BitmapFrame.Create(bitmap));
using var pngStream = new MemoryStream();
encoder.Save(pngStream);
var png = pngStream.ToArray();
File.WriteAllBytes(Path.ChangeExtension(destination, ".png"), png);

using var icoStream = File.Create(destination);
using var writer = new BinaryWriter(icoStream);
writer.Write((ushort)0);
writer.Write((ushort)1);
writer.Write((ushort)1);
writer.Write((byte)0);
writer.Write((byte)0);
writer.Write((byte)0);
writer.Write((byte)0);
writer.Write((ushort)1);
writer.Write((ushort)32);
writer.Write(png.Length);
writer.Write(22);
writer.Write(png);
Console.WriteLine(destination);
