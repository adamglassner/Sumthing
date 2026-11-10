using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Sumthing.Views;

/// <summary>A calculator key. Its look comes from the theme (Themes/Calc.xaml); Tag holds the key name the engine understands.</summary>
public class CalcKey : Button
{
    public static readonly DependencyProperty HoverBackgroundProperty =
        DependencyProperty.Register(nameof(HoverBackground), typeof(Brush), typeof(CalcKey));
    public static readonly DependencyProperty PressedBackgroundProperty =
        DependencyProperty.Register(nameof(PressedBackground), typeof(Brush), typeof(CalcKey));
    public static readonly DependencyProperty CornerRadiusProperty =
        DependencyProperty.Register(nameof(CornerRadius), typeof(CornerRadius), typeof(CalcKey));

    public Brush HoverBackground { get => (Brush)GetValue(HoverBackgroundProperty); set => SetValue(HoverBackgroundProperty, value); }
    public Brush PressedBackground { get => (Brush)GetValue(PressedBackgroundProperty); set => SetValue(PressedBackgroundProperty, value); }
    public CornerRadius CornerRadius { get => (CornerRadius)GetValue(CornerRadiusProperty); set => SetValue(CornerRadiusProperty, value); }

    /// <summary>Flash the key as if clicked (keyboard input), so the user sees which key was used.</summary>
    public void Flash()
    {
        SetIsPressed(true);
        var t = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(110) };
        t.Tick += (_, _) => { t.Stop(); SetIsPressed(false); };
        t.Start();
    }
}
