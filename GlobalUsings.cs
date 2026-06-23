// Résolution des conflits de noms WPF ↔ WinForms/Drawing
// WPF prend la priorité partout ; WinForms accessible via WinForms.*
global using Application        = System.Windows.Application;
global using Brush              = System.Windows.Media.Brush;
global using Brushes            = System.Windows.Media.Brushes;
global using Color              = System.Windows.Media.Color;
global using ColorConverter     = System.Windows.Media.ColorConverter;
global using Panel              = System.Windows.Controls.Panel;
global using Button             = System.Windows.Controls.Button;
global using Orientation        = System.Windows.Controls.Orientation;
global using HorizontalAlignment = System.Windows.HorizontalAlignment;
global using VerticalAlignment  = System.Windows.VerticalAlignment;
global using Cursors            = System.Windows.Input.Cursors;
global using Point              = System.Windows.Point;
global using Size               = System.Windows.Size;
global using WinForms           = System.Windows.Forms;
global using Drawing            = System.Drawing;
