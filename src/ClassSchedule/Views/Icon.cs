using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Shapes;

namespace ClassSchedule.Views;

public sealed class Icon : Viewbox
{
    private readonly Path path = new()
    {
        StrokeThickness = 1.8,
        StrokeStartLineCap = PenLineCap.Round,
        StrokeEndLineCap = PenLineCap.Round,
        StrokeLineJoin = PenLineJoin.Round
    };

    public static readonly DependencyProperty KeyProperty = DependencyProperty.Register(
        nameof(Key), typeof(string), typeof(Icon), new PropertyMetadata(null, (d, _) => ((Icon)d).Refresh()));

    public static readonly DependencyProperty BrushKeyProperty = DependencyProperty.Register(
        nameof(BrushKey), typeof(string), typeof(Icon), new PropertyMetadata(null, (d, _) => ((Icon)d).Refresh()));

    public Icon()
    {
        Width = Height = 18;
        Stretch = Stretch.Uniform;
        IsHitTestVisible = false;
        Child = new Canvas { Width = 24, Height = 24, Children = { path } };
    }

    public Icon(string key, double size = 18, string? brushKey = null) : this()
    {
        Width = Height = size;
        BrushKey = brushKey;
        Key = key;
    }

    public string? Key { get => (string?)GetValue(KeyProperty); set => SetValue(KeyProperty, value); }

    /// <summary>Theme brush to stroke with; when empty the icon follows the text colour of its button.</summary>
    public string? BrushKey { get => (string?)GetValue(BrushKeyProperty); set => SetValue(BrushKeyProperty, value); }

    private void Refresh()
    {
        if (Key is not null && Application.Current?.TryFindResource(Key) is Geometry g) path.Data = g;

        if (BrushKey is not null) path.SetResourceReference(Shape.StrokeProperty, BrushKey);
        else
        {
            BindingOperations.SetBinding(path, Shape.StrokeProperty, new Binding
            {
                Path = new PropertyPath(TextElement.ForegroundProperty),
                RelativeSource = new RelativeSource(RelativeSourceMode.FindAncestor, typeof(ContentPresenter), 1)
            });
        }
    }
}
