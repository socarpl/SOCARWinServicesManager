using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace Socar.WinServicesManager;

public sealed class ServiceGraphControl : FrameworkElement
{
    private readonly Dictionary<string, GraphNode> _nodes = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<GraphEdge> _edges = [];
    private Point _pan = new(40, 40);
    private double _zoom = 1;
    private bool _isPanning;
    private Point _lastMouse;
    private GraphNode? _draggedNode;
    private string? _selectedServiceName;

    public ServiceGraphControl()
    {
        Focusable = true;
        ClipToBounds = true;
        MouseDown += OnGraphMouseDown;
        MouseMove += OnGraphMouseMove;
        MouseUp += OnGraphMouseUp;
        MouseWheel += OnGraphMouseWheel;
    }

    public event EventHandler<string>? ServiceSelected;

    public void SetServices(IReadOnlyList<ServiceSummary> services, string? selectedServiceName)
    {
        _selectedServiceName = selectedServiceName;
        _nodes.Clear();
        _edges.Clear();
        foreach (var service in services)
        {
            _nodes[service.Name] = new GraphNode(service);
        }
        foreach (var service in services)
        {
            foreach (var dependency in service.DependsOn)
            {
                if (_nodes.ContainsKey(dependency))
                {
                    _edges.Add(new GraphEdge(service.Name, dependency));
                }
            }
        }
        ApplyLayout();
        InvalidateVisual();
    }

    public void SelectService(string? serviceName)
    {
        _selectedServiceName = serviceName;
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(18, 22, 28)), null, new Rect(RenderSize));
        if (_nodes.Count == 0)
        {
            dc.DrawText(MakeText("Load services to render the dependency graph.", 13, Brushes.LightGray), new Point(16, 16));
            return;
        }

        dc.PushTransform(new TranslateTransform(_pan.X, _pan.Y));
        dc.PushTransform(new ScaleTransform(_zoom, _zoom));
        DrawEdges(dc);
        DrawNodes(dc);
        dc.Pop();
        dc.Pop();

        dc.DrawText(MakeText($"{_nodes.Count:N0} services, {_edges.Count:N0} dependencies", 13, Brushes.Gainsboro), new Point(12, 10));
        dc.DrawText(MakeText("Mouse wheel zooms. Drag empty space pans. Click a service to highlight dependencies.", 12, Brushes.SlateGray), new Point(12, 30));
    }

    private void DrawEdges(DrawingContext dc)
    {
        foreach (var edge in _edges)
        {
            if (!_nodes.TryGetValue(edge.From, out var from) || !_nodes.TryGetValue(edge.To, out var to))
            {
                continue;
            }
            var brush = IsEdgeHighlighted(edge) ? Brushes.Gold : new SolidColorBrush(Color.FromArgb(100, 122, 144, 166));
            var pen = new Pen(brush, IsEdgeHighlighted(edge) ? 2.2 / _zoom : 1.1 / _zoom);
            var start = PointOnCircle(from.Position, to.Position, 10);
            var end = PointOnCircle(to.Position, from.Position, 13);
            dc.DrawLine(pen, start, end);
            DrawArrow(dc, pen, start, end);
        }
    }

    private void DrawNodes(DrawingContext dc)
    {
        foreach (var node in _nodes.Values)
        {
            var selected = IsSelected(node.Service.Name);
            var related = IsRelatedToSelected(node.Service.Name);
            var radius = selected ? 12 : 8;
            var brush = NodeBrush(node.Service, selected, related);
            var borderPen = NodeBorderPen(node.Service, selected);
            dc.DrawEllipse(brush, borderPen, node.Position, radius, radius);
            if (_zoom >= 0.32 || selected || related)
            {
                var text = MakeText(ShortName(node.Service.Name), selected ? 10 : 9, related || selected ? Brushes.Gainsboro : Brushes.SlateGray);
                dc.DrawText(text, new Point(node.Position.X - text.Width / 2, node.Position.Y + radius + 2 / _zoom));
            }
        }
    }

    private void ApplyLayout()
    {
        var ordered = _nodes.Values.OrderByDescending(node => Degree(node.Service.Name)).ThenBy(node => node.Service.Name).ToList();
        var count = Math.Max(1, ordered.Count);
        var radius = Math.Max(260, count * 5.8);
        for (var i = 0; i < ordered.Count; i++)
        {
            var angle = i * Math.PI * (3 - Math.Sqrt(5));
            var ring = Math.Sqrt(i + 1) * (radius / Math.Sqrt(count));
            ordered[i].Position = new Point(Math.Cos(angle) * ring, Math.Sin(angle) * ring);
        }

        RelaxNodeCollisions(ordered);
    }

    private static void RelaxNodeCollisions(IReadOnlyList<GraphNode> nodes)
    {
        const double minDistance = 42;
        for (var pass = 0; pass < 120; pass++)
        {
            for (var i = 0; i < nodes.Count; i++)
            {
                for (var j = i + 1; j < nodes.Count; j++)
                {
                    var delta = nodes[j].Position - nodes[i].Position;
                    var distance = delta.Length;
                    if (distance >= minDistance)
                    {
                        continue;
                    }

                    if (distance < 0.001)
                    {
                        delta = new Vector(1, 0);
                        distance = 1;
                    }

                    delta.Normalize();
                    var push = (minDistance - distance) / 2;
                    nodes[i].Position -= delta * push;
                    nodes[j].Position += delta * push;
                }
            }
        }
    }

    private int Degree(string serviceName) => _edges.Count(edge => edge.From.Equals(serviceName, StringComparison.OrdinalIgnoreCase) || edge.To.Equals(serviceName, StringComparison.OrdinalIgnoreCase));

    private void OnGraphMouseDown(object sender, MouseButtonEventArgs e)
    {
        Focus();
        var world = ToWorld(e.GetPosition(this));
        var node = HitTest(world);
        if (node is not null)
        {
            _selectedServiceName = node.Service.Name;
            _draggedNode = node;
            _lastMouse = e.GetPosition(this);
            ServiceSelected?.Invoke(this, node.Service.Name);
            InvalidateVisual();
            CaptureMouse();
            return;
        }

        _isPanning = true;
        _lastMouse = e.GetPosition(this);
        CaptureMouse();
    }

    private void OnGraphMouseMove(object sender, MouseEventArgs e)
    {
        var current = e.GetPosition(this);
        if (_draggedNode is not null)
        {
            var previousWorld = ToWorld(_lastMouse);
            var currentWorld = ToWorld(current);
            var delta = currentWorld - previousWorld;
            _draggedNode.Position += delta;
            _lastMouse = current;
            InvalidateVisual();
            return;
        }

        if (!_isPanning)
        {
            return;
        }
        _pan = new Point(_pan.X + current.X - _lastMouse.X, _pan.Y + current.Y - _lastMouse.Y);
        _lastMouse = current;
        InvalidateVisual();
    }

    private void OnGraphMouseUp(object sender, MouseButtonEventArgs e)
    {
        _isPanning = false;
        _draggedNode = null;
        ReleaseMouseCapture();
    }

    private void OnGraphMouseWheel(object sender, MouseWheelEventArgs e)
    {
        var oldZoom = _zoom;
        _zoom = Math.Clamp(_zoom * (e.Delta > 0 ? 1.15 : 1 / 1.15), 0.12, 4);
        var mouse = e.GetPosition(this);
        var before = ToWorld(mouse, oldZoom);
        _pan = new Point(mouse.X - before.X * _zoom, mouse.Y - before.Y * _zoom);
        InvalidateVisual();
    }

    private GraphNode? HitTest(Point world)
    {
        return _nodes.Values.FirstOrDefault(node => (node.Position - world).Length <= (IsSelected(node.Service.Name) ? 14 : 10));
    }

    private Point ToWorld(Point screen) => ToWorld(screen, _zoom);
    private Point ToWorld(Point screen, double zoom) => new((screen.X - _pan.X) / zoom, (screen.Y - _pan.Y) / zoom);
    private bool IsSelected(string name) => _selectedServiceName is not null && name.Equals(_selectedServiceName, StringComparison.OrdinalIgnoreCase);
    private bool IsEdgeHighlighted(GraphEdge edge) => _selectedServiceName is not null && (edge.From.Equals(_selectedServiceName, StringComparison.OrdinalIgnoreCase) || edge.To.Equals(_selectedServiceName, StringComparison.OrdinalIgnoreCase));
    private bool IsRelatedToSelected(string name) => _selectedServiceName is not null && _edges.Any(edge => edge.From.Equals(_selectedServiceName, StringComparison.OrdinalIgnoreCase) && edge.To.Equals(name, StringComparison.OrdinalIgnoreCase) || edge.To.Equals(_selectedServiceName, StringComparison.OrdinalIgnoreCase) && edge.From.Equals(name, StringComparison.OrdinalIgnoreCase));
    private static string ShortName(string name) => name.Length <= 16 ? name : name[..16];
    private Pen NodeBorderPen(ServiceSummary service, bool selected)
    {
        if (selected)
        {
            return new Pen(Brushes.Gold, 2.4 / _zoom);
        }

        return service.State switch
        {
            ServiceRunState.Running => new Pen(Brushes.Black, 1.5 / _zoom),
            ServiceRunState.Stopped => new Pen(Brushes.White, 1.5 / _zoom),
            _ => new Pen(Brushes.LightGray, 1.2 / _zoom)
        };
    }

    private static Brush NodeBrush(ServiceSummary service, bool selected, bool related)
    {
        if (selected)
        {
            return Brushes.Gold;
        }

        if (related)
        {
            return Brushes.DeepSkyBlue;
        }

        return service.State switch
        {
            ServiceRunState.Running => Brushes.LimeGreen,
            ServiceRunState.Stopped => Brushes.Red,
            _ => Brushes.SteelBlue
        };
    }

    private static Point PointOnCircle(Point from, Point toward, double radius)
    {
        var vector = toward - from;
        vector.Normalize();
        return from + vector * radius;
    }

    private static void DrawArrow(DrawingContext dc, Pen pen, Point start, Point end)
    {
        var vector = start - end;
        vector.Normalize();
        var normal = new Vector(-vector.Y, vector.X);
        var p1 = end + vector * 10 + normal * 5;
        var p2 = end + vector * 10 - normal * 5;
        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            ctx.BeginFigure(end, true, true);
            ctx.LineTo(p1, true, false);
            ctx.LineTo(p2, true, false);
        }
        dc.DrawGeometry(pen.Brush, null, geometry);
    }

    private static FormattedText MakeText(string text, double size, Brush brush)
    {
        return new FormattedText(text, System.Globalization.CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface("Segoe UI"), size, brush, VisualTreeHelper.GetDpi(Application.Current.MainWindow).PixelsPerDip);
    }

    private sealed class GraphNode(ServiceSummary service)
    {
        public ServiceSummary Service { get; } = service;
        public Point Position { get; set; }
    }

    private sealed record GraphEdge(string From, string To);
}
