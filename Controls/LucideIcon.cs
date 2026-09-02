using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace NSCheatManager.Windows.Controls;

public enum LucideIconKind
{
    Gamepad,
    Code,
    Files,
    Database,
    Camera,
    Settings
}

public sealed class LucideIcon : Control
{
    public static readonly DependencyProperty KindProperty = DependencyProperty.Register(
        nameof(Kind), typeof(LucideIconKind), typeof(LucideIcon),
        new FrameworkPropertyMetadata(LucideIconKind.Gamepad, FrameworkPropertyMetadataOptions.AffectsRender));

    public LucideIconKind Kind
    {
        get => (LucideIconKind)GetValue(KindProperty);
        set => SetValue(KindProperty, value);
    }

    public LucideIcon()
    {
        IsHitTestVisible = false;
        SnapsToDevicePixels = true;
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);
        if (ActualWidth <= 0 || ActualHeight <= 0) return;

        var scale = Math.Min(ActualWidth / 24d, ActualHeight / 24d);
        var offsetX = (ActualWidth - 24d * scale) / 2d;
        var offsetY = (ActualHeight - 24d * scale) / 2d;
        drawingContext.PushTransform(new TranslateTransform(offsetX, offsetY));
        drawingContext.PushTransform(new ScaleTransform(scale, scale));

        var pen = new Pen(Foreground, 2)
        {
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round,
            LineJoin = PenLineJoin.Round
        };

        switch (Kind)
        {
            case LucideIconKind.Gamepad:
                Draw(drawingContext, pen, "M17.32,5 H6.68 A4,4 0 0 0 2.702,8.59 C2.696,8.642 2.692,8.691 2.685,8.742 C2.604,9.416 2,14.456 2,16 A3,3 0 0 0 5,19 C6,19 6.5,18.5 7,18 L8.414,16.586 A2,2 0 0 1 9.828,16 H14.172 A2,2 0 0 1 15.586,16.586 L17,18 C17.5,18.5 18,19 19,19 A3,3 0 0 0 22,16 C22,14.455 21.396,9.416 21.315,8.742 C21.308,8.692 21.304,8.642 21.298,8.591 A4,4 0 0 0 17.32,5 Z");
                drawingContext.DrawLine(pen, new Point(6, 11), new Point(10, 11));
                drawingContext.DrawLine(pen, new Point(8, 9), new Point(8, 13));
                drawingContext.DrawEllipse(Foreground, null, new Point(15, 12), 1, 1);
                drawingContext.DrawEllipse(Foreground, null, new Point(18, 10), 1, 1);
                break;
            case LucideIconKind.Code:
                drawingContext.DrawLine(pen, new Point(8, 9), new Point(3, 12));
                drawingContext.DrawLine(pen, new Point(3, 12), new Point(8, 15));
                drawingContext.DrawLine(pen, new Point(16, 9), new Point(21, 12));
                drawingContext.DrawLine(pen, new Point(21, 12), new Point(16, 15));
                drawingContext.DrawLine(pen, new Point(14, 5), new Point(10, 19));
                break;
            case LucideIconKind.Files:
                Draw(drawingContext, pen, "M15,2 H6 A2,2 0 0 0 4,4 V16 A2,2 0 0 0 6,18");
                Draw(drawingContext, pen, "M18,7 H11 A2,2 0 0 0 9,9 V20 A2,2 0 0 0 11,22 H20 A2,2 0 0 0 22,20 V11 Z");
                Draw(drawingContext, pen, "M18,7 V11 H22");
                break;
            case LucideIconKind.Database:
                drawingContext.DrawEllipse(null, pen, new Point(12, 5), 9, 3);
                Draw(drawingContext, pen, "M3,5 V19 A9,3 0 0 0 21,19 V5");
                Draw(drawingContext, pen, "M3,12 A9,3 0 0 0 21,12");
                break;
            case LucideIconKind.Camera:
                Draw(drawingContext, pen, "M14.5,4 H9.5 L7,7 H4 A2,2 0 0 0 2,9 V19 A2,2 0 0 0 4,21 H20 A2,2 0 0 0 22,19 V9 A2,2 0 0 0 20,7 H17 Z");
                drawingContext.DrawEllipse(null, pen, new Point(12, 14), 3, 3);
                break;
            case LucideIconKind.Settings:
                Draw(drawingContext, pen, "M12.22,2 H11.78 A2,2 0 0 0 9.78,4 V4.18 A2,2 0 0 1 8.78,5.91 L8.35,6.16 A2,2 0 0 1 6.35,6.16 L6.2,6.08 A2,2 0 0 0 3.47,6.81 L3.25,7.19 A2,2 0 0 0 3.98,9.92 L4.13,10 A2,2 0 0 1 5.13,11.73 V12.27 A2,2 0 0 1 4.13,14 L3.98,14.08 A2,2 0 0 0 3.25,16.81 L3.47,17.19 A2,2 0 0 0 6.2,17.92 L6.35,17.84 A2,2 0 0 1 8.35,17.84 L8.78,18.09 A2,2 0 0 1 9.78,19.82 V20 A2,2 0 0 0 11.78,22 H12.22 A2,2 0 0 0 14.22,20 V19.82 A2,2 0 0 1 15.22,18.09 L15.65,17.84 A2,2 0 0 1 17.65,17.84 L17.8,17.92 A2,2 0 0 0 20.53,17.19 L20.75,16.81 A2,2 0 0 0 20.02,14.08 L19.87,14 A2,2 0 0 1 18.87,12.27 V11.73 A2,2 0 0 1 19.87,10 L20.02,9.92 A2,2 0 0 0 20.75,7.19 L20.53,6.81 A2,2 0 0 0 17.8,6.08 L17.65,6.16 A2,2 0 0 1 15.65,6.16 L15.22,5.91 A2,2 0 0 1 14.22,4.18 V4 A2,2 0 0 0 12.22,2 Z");
                drawingContext.DrawEllipse(null, pen, new Point(12, 12), 3, 3);
                break;
        }

        drawingContext.Pop();
        drawingContext.Pop();
    }

    private static void Draw(DrawingContext context, Pen pen, string data) =>
        context.DrawGeometry(null, pen, Geometry.Parse(data));
}
