using System.Drawing.Drawing2D;

namespace Socar.WinServicesManager;

public sealed class ServiceGraphView : Control
{
    private const float NodeRadius = 8f;
    private const float SelectedNodeRadius = 12f;
    private const float MinZoom = 0.12f;
    private const float MaxZoom = 4f;

    private readonly Dictionary<string, GraphNode> _nodes = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<GraphEdge> _edges = [];
    private PointF _pan = new(40, 40);
    private float _zoom = 1f;
    private bool _isPanning;
    private Point _lastMousePosition;
    private string? _selectedServiceName;

    public event EventHandler<string>? ServiceSelected;

    public ServiceGraphView()
    {
        DoubleBuffered = true;
        BackColor = Color.FromArgb(18, 22, 28);
        ForeColor = Color.FromArgb(218, 226, 236);
        SetStyle(ControlStyles.ResizeRedraw, true);
    }

    public void SetServices(IReadOnlyList<ServiceSummary> services, string? selectedServiceName)
    {
        _selectedServiceName = selectedServiceName;
        BuildGraph(services);
        Invalidate();
    }

    public void SelectService(string? serviceName)
    {
        _selectedServiceName = serviceName;
        Invalidate();
    }

    public void ResetView()
    {
        _zoom = 1f;
        _pan = new PointF(40, 40);
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);

        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        e.Graphics.Clear(BackColor);

        if (_nodes.Count == 0)
        {
            DrawEmptyState(e.Graphics);
            return;
        }

        using var transform = new Matrix();
        transform.Translate(_pan.X, _pan.Y);
        transform.Scale(_zoom, _zoom);
        e.Graphics.Transform = transform;

        DrawEdges(e.Graphics);
        DrawNodes(e.Graphics);

        e.Graphics.ResetTransform();
        DrawOverlay(e.Graphics);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        Focus();

        if (e.Button == MouseButtons.Left)
        {
            var node = HitTest(ToWorld(e.Location));
            if (node is not null)
            {
                _selectedServiceName = node.Service.Name;
                Invalidate();
                ServiceSelected?.Invoke(this, node.Service.Name);
                return;
            }
        }

        if (e.Button is MouseButtons.Left or MouseButtons.Middle)
        {
            _isPanning = true;
            _lastMousePosition = e.Location;
            Cursor = Cursors.SizeAll;
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);

        if (_isPanning)
        {
            _pan = new PointF(_pan.X + e.X - _lastMousePosition.X, _pan.Y + e.Y - _lastMousePosition.Y);
            _lastMousePosition = e.Location;
            Invalidate();
            return;
        }

        Cursor = HitTest(ToWorld(e.Location)) is null ? Cursors.Default : Cursors.Hand;
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        _isPanning = false;
        Cursor = Cursors.Default;
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);

        var oldZoom = _zoom;
        var factor = e.Delta > 0 ? 1.15f : 1 / 1.15f;
        _zoom = Math.Clamp(_zoom * factor, MinZoom, MaxZoom);

        var mouseWorldBefore = ToWorld(e.Location, oldZoom);
        _pan = new PointF(e.X - mouseWorldBefore.X * _zoom, e.Y - mouseWorldBefore.Y * _zoom);
        Invalidate();
    }

    private void BuildGraph(IReadOnlyList<ServiceSummary> services)
    {
        _nodes.Clear();
        _edges.Clear();

        foreach (var service in services)
        {
            _nodes[service.Name] = new GraphNode(service);
        }

        foreach (var service in services)
        {
            foreach (var dependencyName in service.DependsOn)
            {
                if (_nodes.ContainsKey(dependencyName))
                {
                    _edges.Add(new GraphEdge(service.Name, dependencyName));
                }
            }
        }

        ApplyLayout();
    }

    private void ApplyLayout()
    {
        if (_nodes.Count == 0)
        {
            return;
        }

        var orderedNodes = _nodes.Values
            .OrderByDescending(node => Degree(node.Service.Name))
            .ThenBy(node => node.Service.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        var layerCounts = new Dictionary<int, int>();
        var layerIndexes = new Dictionary<int, int>();
        var layers = orderedNodes.ToDictionary(node => node.Service.Name, node => DependencyDepth(node.Service.Name, []), StringComparer.OrdinalIgnoreCase);

        foreach (var layer in layers.Values)
        {
            layerCounts[layer] = layerCounts.GetValueOrDefault(layer) + 1;
        }

        var maxLayer = Math.Max(1, layers.Values.DefaultIfEmpty(0).Max());
        var layerGap = Math.Max(180f, Math.Min(340f, 2400f / (maxLayer + 1)));

        foreach (var node in orderedNodes)
        {
            var layer = layers[node.Service.Name];
            var index = layerIndexes.GetValueOrDefault(layer);
            layerIndexes[layer] = index + 1;

            var count = layerCounts[layer];
            var angle = count <= 1 ? 0 : MathF.PI * 2f * index / count;
            var radius = 120f + layer * layerGap;
            var jitter = StableJitter(node.Service.Name);

            if (layer == 0)
            {
                radius = 80f + jitter.X * 20f;
            }

            node.Position = new PointF(
                MathF.Cos(angle) * radius + jitter.X * 35f,
                MathF.Sin(angle) * radius + jitter.Y * 35f);
        }
    }

    private int Degree(string serviceName)
    {
        return _edges.Count(edge =>
            edge.From.Equals(serviceName, StringComparison.OrdinalIgnoreCase) ||
            edge.To.Equals(serviceName, StringComparison.OrdinalIgnoreCase));
    }

    private int DependencyDepth(string serviceName, HashSet<string> visited)
    {
        if (!visited.Add(serviceName) || !_nodes.TryGetValue(serviceName, out var node))
        {
            return 0;
        }

        if (node.Service.DependsOn.Count == 0)
        {
            return 0;
        }

        return 1 + node.Service.DependsOn
            .Where(_nodes.ContainsKey)
            .Select(dependency => DependencyDepth(dependency, new HashSet<string>(visited, StringComparer.OrdinalIgnoreCase)))
            .DefaultIfEmpty(0)
            .Max();
    }

    private void DrawEdges(Graphics graphics)
    {
        using var edgePen = new Pen(Color.FromArgb(72, 122, 144, 166), 1.1f / _zoom)
        {
            CustomEndCap = new AdjustableArrowCap(4f / _zoom, 5f / _zoom)
        };
        using var highlightedPen = new Pen(Color.FromArgb(210, 245, 184, 82), 2.2f / _zoom)
        {
            CustomEndCap = new AdjustableArrowCap(5f / _zoom, 6f / _zoom)
        };

        foreach (var edge in _edges)
        {
            if (!_nodes.TryGetValue(edge.From, out var from) || !_nodes.TryGetValue(edge.To, out var to))
            {
                continue;
            }

            var pen = IsEdgeHighlighted(edge) ? highlightedPen : edgePen;
            var start = PointOnCircle(from.Position, to.Position, NodeRadius + 2);
            var end = PointOnCircle(to.Position, from.Position, NodeRadius + 5);
            graphics.DrawLine(pen, start, end);
        }
    }

    private void DrawNodes(Graphics graphics)
    {
        using var labelFont = new Font(Font.FontFamily, Math.Max(7f, 8.5f / _zoom), FontStyle.Regular);
        using var selectedLabelFont = new Font(Font.FontFamily, Math.Max(7f, 9.5f / _zoom), FontStyle.Bold);
        using var labelBrush = new SolidBrush(Color.FromArgb(218, 226, 236));
        using var mutedLabelBrush = new SolidBrush(Color.FromArgb(150, 166, 182));
        using var selectedPen = new Pen(Color.FromArgb(255, 245, 184, 82), 2f / _zoom);

        foreach (var node in _nodes.Values)
        {
            var isSelected = IsSelected(node.Service.Name);
            var isRelated = IsRelatedToSelected(node.Service.Name);
            var radius = isSelected ? SelectedNodeRadius : NodeRadius;
            var bounds = new RectangleF(node.Position.X - radius, node.Position.Y - radius, radius * 2, radius * 2);

            using var nodeBrush = new SolidBrush(NodeColor(node.Service, isSelected, isRelated));
            graphics.FillEllipse(nodeBrush, bounds);

            if (isSelected)
            {
                graphics.DrawEllipse(selectedPen, bounds);
            }

            if (_zoom >= 0.32f || isSelected || isRelated)
            {
                var text = ShortName(node.Service.Name);
                var font = isSelected ? selectedLabelFont : labelFont;
                var brush = isRelated || isSelected ? labelBrush : mutedLabelBrush;
                var textSize = graphics.MeasureString(text, font);
                graphics.DrawString(text, font, brush, node.Position.X - textSize.Width / 2f, node.Position.Y + radius + 2f / _zoom);
            }
        }
    }

    private void DrawOverlay(Graphics graphics)
    {
        using var overlayBrush = new SolidBrush(Color.FromArgb(220, 218, 226, 236));
        using var mutedBrush = new SolidBrush(Color.FromArgb(170, 150, 166, 182));
        using var font = new Font(Font.FontFamily, 9f);
        using var boldFont = new Font(Font.FontFamily, 9f, FontStyle.Bold);

        graphics.DrawString($"{_nodes.Count:N0} services, {_edges.Count:N0} dependencies", boldFont, overlayBrush, 12, 10);
        graphics.DrawString("Mouse wheel zooms. Drag empty space pans. Click a service to highlight dependencies.", font, mutedBrush, 12, 30);
    }

    private void DrawEmptyState(Graphics graphics)
    {
        using var brush = new SolidBrush(Color.FromArgb(180, 218, 226, 236));
        using var font = new Font(Font.FontFamily, 10f);
        graphics.DrawString("Load services to render the dependency graph.", font, brush, 16, 16);
    }

    private GraphNode? HitTest(PointF worldPoint)
    {
        foreach (var node in _nodes.Values)
        {
            var radius = IsSelected(node.Service.Name) ? SelectedNodeRadius : NodeRadius;
            var dx = worldPoint.X - node.Position.X;
            var dy = worldPoint.Y - node.Position.Y;
            if (dx * dx + dy * dy <= radius * radius * 2.2f)
            {
                return node;
            }
        }

        return null;
    }

    private PointF ToWorld(Point point)
    {
        return ToWorld(point, _zoom);
    }

    private PointF ToWorld(Point point, float zoom)
    {
        return new PointF((point.X - _pan.X) / zoom, (point.Y - _pan.Y) / zoom);
    }

    private bool IsEdgeHighlighted(GraphEdge edge)
    {
        return _selectedServiceName is not null &&
            (edge.From.Equals(_selectedServiceName, StringComparison.OrdinalIgnoreCase) ||
             edge.To.Equals(_selectedServiceName, StringComparison.OrdinalIgnoreCase));
    }

    private bool IsSelected(string serviceName)
    {
        return _selectedServiceName is not null && serviceName.Equals(_selectedServiceName, StringComparison.OrdinalIgnoreCase);
    }

    private bool IsRelatedToSelected(string serviceName)
    {
        if (_selectedServiceName is null)
        {
            return false;
        }

        return _edges.Any(edge =>
            edge.From.Equals(_selectedServiceName, StringComparison.OrdinalIgnoreCase) &&
            edge.To.Equals(serviceName, StringComparison.OrdinalIgnoreCase) ||
            edge.To.Equals(_selectedServiceName, StringComparison.OrdinalIgnoreCase) &&
            edge.From.Equals(serviceName, StringComparison.OrdinalIgnoreCase));
    }

    private static Color NodeColor(ServiceSummary service, bool isSelected, bool isRelated)
    {
        if (isSelected)
        {
            return Color.FromArgb(255, 245, 184, 82);
        }

        if (isRelated)
        {
            return Color.FromArgb(255, 94, 178, 210);
        }

        return service.State switch
        {
            ServiceRunState.Running => Color.FromArgb(255, 79, 190, 145),
            ServiceRunState.Stopped => Color.FromArgb(255, 107, 123, 142),
            ServiceRunState.Paused => Color.FromArgb(255, 190, 152, 78),
            _ => Color.FromArgb(255, 132, 156, 190)
        };
    }

    private static PointF PointOnCircle(PointF from, PointF toward, float radius)
    {
        var dx = toward.X - from.X;
        var dy = toward.Y - from.Y;
        var length = MathF.Sqrt(dx * dx + dy * dy);
        if (length <= 0.001f)
        {
            return from;
        }

        return new PointF(from.X + dx / length * radius, from.Y + dy / length * radius);
    }

    private static string ShortName(string serviceName)
    {
        const int maxLength = 16;
        return serviceName.Length <= maxLength ? serviceName : serviceName[..maxLength];
    }

    private static PointF StableJitter(string value)
    {
        var hash = value.Aggregate(17, (current, character) => current * 31 + character);
        var x = ((hash & 0xff) / 255f) * 2f - 1f;
        var y = (((hash >> 8) & 0xff) / 255f) * 2f - 1f;
        return new PointF(x, y);
    }

    private sealed class GraphNode(ServiceSummary service)
    {
        public ServiceSummary Service { get; } = service;
        public PointF Position { get; set; }
    }

    private sealed record GraphEdge(string From, string To);
}
