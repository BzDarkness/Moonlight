using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace MoonlightLauncher;

/// <summary>Um pixel da skin (x, y na textura 64×64) que está debaixo do mouse, com a face do corpo onde ele fica.</summary>
public readonly record struct PartHit(int X, int Y, bool Overlay, string Face, int FX0, int FY0, int FX1, int FY1);

public enum BodyPart { Head, Body, ArmR, ArmL, LegR, LegL }

/// <summary>
/// Pintura em 3D de UMA parte do corpo (cabeça, corpo, braço ou perna). A peça aparece grande, você gira com o
/// botão direito, dá zoom com a rodinha e pinta com o botão esquerdo direto em cima do 3D.
/// Desenhado à mão (sem Viewport3D): cada quadro monta um bitmap e guarda, para cada pixel da tela, qual pixel
/// da skin ele mostra. Assim o clique cai exatamente no pixel certo, em qualquer ângulo.
/// </summary>
public sealed class PartPainter3D : UserControl
{
    private sealed class Face
    {
        public readonly double[] X = new double[4];
        public readonly double[] Y = new double[4];
        public readonly double[] Z = new double[4];
        public double U0, V0, U1, V1;
        public double Shade;
        public bool Overlay;
        public string Name = "";
    }

    // ---------- Eventos para o editor ----------
    public event Action<PartHit>? PaintBegin;
    public event Action<PartHit>? PaintMove;
    public event Action? PaintEnd;
    public event Action<PartHit>? PickRequested;
    public event Action<PartHit?>? HoverChanged;

    // ---------- Estado ----------
    private readonly Image _image = new() { Stretch = Stretch.Fill, IsHitTestVisible = false };
    private readonly List<Face> _faces = new();
    private WriteableBitmap? _bmp;
    private int[] _pix = Array.Empty<int>();
    private int[] _id = Array.Empty<int>();
    private float[] _zbuf = Array.Empty<float>();
    private int _bw, _bh;

    private byte[] _tex = new byte[64 * 64 * 4];
    private BodyPart _part = BodyPart.Head;
    private bool _slim, _overlay, _showGrid = true;
    private double _yaw = -28, _pitch = 18, _zoom = 1.0;
    private bool _renderQueued;
    private bool _paused;

    private int _hoverId = -1;
    private bool _painting, _rotating, _moved;
    private Point _lastPos, _downPos;
    private int _lastPaintId = -1;
    private double _startYaw, _startPitch;

    // Vértices na tela do triângulo em desenho.
    private readonly double[] _sx = new double[4];
    private readonly double[] _sy = new double[4];
    private readonly double[] _sz = new double[4];
    private readonly double[] _su = new double[4];
    private readonly double[] _sv = new double[4];
    private double _umin, _umax, _vmin, _vmax, _shade;
    private bool _curOverlay, _curDim;

    public PartPainter3D()
    {
        var root = new Grid { Background = Brushes.Transparent, ClipToBounds = true };
        root.Children.Add(_image);
        Content = root;
        Cursor = Cursors.Cross;
        Focusable = false;
        RenderOptions.SetBitmapScalingMode(_image, BitmapScalingMode.NearestNeighbor);

        SizeChanged += (_, _) => { EnsureBitmap(); QueueRender(); };
        IsVisibleChanged += (_, _) => { if (IsVisible) QueueRender(); };

        BuildFaces();
    }

    // ---------- Propriedades ----------

    /// <summary>Textura da skin (64×64, BGRA). É a MESMA matriz do editor: o que o editor pinta aparece aqui.</summary>
    public byte[] Texture
    {
        get => _tex;
        set { _tex = value; QueueRender(); }
    }

    public BodyPart Part
    {
        get => _part;
        set { if (_part == value) return; _part = value; BuildFaces(); _hoverId = -1; QueueRender(); }
    }

    public bool Slim
    {
        get => _slim;
        set { if (_slim == value) return; _slim = value; BuildFaces(); QueueRender(); }
    }

    /// <summary>true = você está pintando a camada de fora (chapéu, casaco, mangas, calças).</summary>
    public bool OverlayLayer
    {
        get => _overlay;
        set { if (_overlay == value) return; _overlay = value; _hoverId = -1; QueueRender(); }
    }

    public bool ShowGrid
    {
        get => _showGrid;
        set { _showGrid = value; QueueRender(); }
    }

    public bool Paused
    {
        get => _paused;
        set { _paused = value; if (!value) QueueRender(); }
    }

    public void SetView(double yaw, double pitch)
    {
        _yaw = yaw;
        _pitch = Math.Clamp(pitch, -89, 89);
        _hoverId = -1;
        QueueRender();
    }

    public void ResetView() { _zoom = 1.0; SetView(-28, 18); }

    /// <summary>Pede um novo desenho (juntando vários pedidos num só).</summary>
    public void Invalidate() => QueueRender();

    // ---------- Montagem da peça ----------

    private static (double W, double H, double D, int U, int V, int OU, int OV, double Infl) Spec(BodyPart p, bool slim) => p switch
    {
        BodyPart.Head => (8, 8, 8, 0, 0, 32, 0, 0.5),
        BodyPart.Body => (8, 12, 4, 16, 16, 16, 32, 0.25),
        BodyPart.ArmR => (slim ? 3 : 4, 12, 4, 40, 16, 40, 32, 0.25),
        BodyPart.ArmL => (slim ? 3 : 4, 12, 4, 32, 48, 48, 48, 0.25),
        BodyPart.LegR => (4, 12, 4, 0, 16, 0, 32, 0.25),
        _ => (4, 12, 4, 16, 48, 0, 48, 0.25)
    };

    private void BuildFaces()
    {
        _faces.Clear();
        var s = Spec(_part, _slim);
        AddBox(s.W, s.H, s.D, 0, s.U, s.V, false);
        AddBox(s.W, s.H, s.D, s.Infl, s.OU, s.OV, true);
    }

    // Mesmo mapeamento do boneco do launcher (cantos vistos de fora: baixo-esq., baixo-dir., cima-dir., cima-esq.).
    private void AddBox(double w, double h, double d, double infl, int u, int v, bool overlay)
    {
        double hx = w / 2 + infl, hy = h / 2 + infl, hz = d / 2 + infl;

        void Add(string name, double[] xs, double[] ys, double[] zs, double u0, double v0, double u1, double v1, double shade)
        {
            var f = new Face { Name = name, U0 = u0, V0 = v0, U1 = u1, V1 = v1, Shade = shade, Overlay = overlay };
            for (var i = 0; i < 4; i++) { f.X[i] = xs[i]; f.Y[i] = ys[i]; f.Z[i] = zs[i]; }
            _faces.Add(f);
        }

        Add("Frente", new[] { -hx, hx, hx, -hx }, new[] { -hy, -hy, hy, hy }, new[] { hz, hz, hz, hz },
            u + d, v + d, u + d + w, v + d + h, 1.0);
        Add("Costas", new[] { hx, -hx, -hx, hx }, new[] { -hy, -hy, hy, hy }, new[] { -hz, -hz, -hz, -hz },
            u + 2 * d + w, v + d, u + 2 * d + 2 * w, v + d + h, 0.8);
        Add("Lado direito", new[] { -hx, -hx, -hx, -hx }, new[] { -hy, -hy, hy, hy }, new[] { -hz, hz, hz, -hz },
            u, v + d, u + d, v + d + h, 0.72);
        Add("Lado esquerdo", new[] { hx, hx, hx, hx }, new[] { -hy, -hy, hy, hy }, new[] { hz, -hz, -hz, hz },
            u + d + w, v + d, u + 2 * d + w, v + d + h, 0.86);
        Add("Topo", new[] { -hx, hx, hx, -hx }, new[] { hy, hy, hy, hy }, new[] { hz, hz, -hz, -hz },
            u + d, v, u + d + w, v + d, 1.0);
        Add("Baixo", new[] { -hx, hx, hx, -hx }, new[] { -hy, -hy, -hy, -hy }, new[] { -hz, -hz, hz, hz },
            u + d + w, v, u + d + 2 * w, v + d, 0.55);
    }

    // ---------- Desenho ----------

    private void EnsureBitmap()
    {
        var w = (int)Math.Round(ActualWidth);
        var h = (int)Math.Round(ActualHeight);
        if (w < 16 || h < 16) return;
        w = Math.Min(w, 1400);
        h = Math.Min(h, 1400);
        if (_bmp != null && w == _bw && h == _bh) return;

        _bw = w;
        _bh = h;
        _bmp = new WriteableBitmap(w, h, 96, 96, PixelFormats.Pbgra32, null);
        _pix = new int[w * h];
        _id = new int[w * h];
        _zbuf = new float[w * h];
        _image.Source = _bmp;
    }

    private void QueueRender()
    {
        if (_renderQueued || _paused) return;
        _renderQueued = true;
        Dispatcher.BeginInvoke(DispatcherPriority.Render, new Action(() =>
        {
            _renderQueued = false;
            try { Render(); }
            catch (Exception ex) { LauncherLog.Write("Pintura 3D por parte: falha ao desenhar", ex); }
        }));
    }

    private static void RotX(ref double y, ref double z, double deg)
    {
        if (deg == 0) return;
        var a = deg * Math.PI / 180;
        double c = Math.Cos(a), s = Math.Sin(a);
        double ny = y * c - z * s, nz = y * s + z * c;
        y = ny;
        z = nz;
    }

    private static void RotY(ref double x, ref double z, double deg)
    {
        if (deg == 0) return;
        var a = deg * Math.PI / 180;
        double c = Math.Cos(a), s = Math.Sin(a);
        double nx = x * c + z * s, nz = -x * s + z * c;
        x = nx;
        z = nz;
    }

    private void Render()
    {
        EnsureBitmap();
        if (_bmp == null || _bw < 16 || _bh < 16 || !IsVisible) return;

        Array.Clear(_pix, 0, _pix.Length);
        Array.Fill(_id, -1);
        Array.Fill(_zbuf, float.MaxValue);

        var s = Spec(_part, _slim);
        var diag = Math.Sqrt(s.W * s.W + s.H * s.H + s.D * s.D) + 1.5;
        const double dist = 300;
        var ppu = Math.Min(_bw, _bh) / diag * _zoom;
        var focal = ppu * dist;

        foreach (var f in _faces)
        {
            // Editando a camada de base: a de fora fica escondida. Editando a de fora: a base aparece mais escura.
            if (f.Overlay && !_overlay) continue;
            _curOverlay = f.Overlay;
            _curDim = _overlay && !f.Overlay;

            for (var i = 0; i < 4; i++)
            {
                double x = f.X[i], y = f.Y[i], z = f.Z[i];
                RotY(ref x, ref z, _yaw);
                RotX(ref y, ref z, _pitch);

                var zc = dist - z;
                _sx[i] = _bw / 2.0 + x / zc * focal;
                _sy[i] = _bh / 2.0 - y / zc * focal;
                _sz[i] = zc;
            }

            _su[0] = f.U0; _sv[0] = f.V1;
            _su[1] = f.U1; _sv[1] = f.V1;
            _su[2] = f.U1; _sv[2] = f.V0;
            _su[3] = f.U0; _sv[3] = f.V0;
            _umin = f.U0; _umax = f.U1 - 0.001;
            _vmin = f.V0; _vmax = f.V1 - 0.001;
            _shade = f.Shade;

            Tri(0, 1, 2);
            Tri(0, 2, 3);
        }

        _bmp.WritePixels(new Int32Rect(0, 0, _bw, _bh), _pix, _bw * 4, 0);
    }

    private void Tri(int i0, int i1, int i2)
    {
        double ax = _sx[i0], ay = _sy[i0], bx = _sx[i1], by = _sy[i1], cx = _sx[i2], cy = _sy[i2];
        var area = (bx - ax) * (cy - ay) - (by - ay) * (cx - ax);
        if (area >= 0) return; // virado para trás

        var x0 = Math.Max(0, (int)Math.Floor(Math.Min(ax, Math.Min(bx, cx))));
        var x1 = Math.Min(_bw - 1, (int)Math.Ceiling(Math.Max(ax, Math.Max(bx, cx))));
        var y0 = Math.Max(0, (int)Math.Floor(Math.Min(ay, Math.Min(by, cy))));
        var y1 = Math.Min(_bh - 1, (int)Math.Ceiling(Math.Max(ay, Math.Max(by, cy))));
        var inv = 1.0 / area;

        for (var y = y0; y <= y1; y++)
        {
            var py = y + 0.5;
            for (var x = x0; x <= x1; x++)
            {
                var px = x + 0.5;
                var w0 = ((bx - px) * (cy - py) - (by - py) * (cx - px)) * inv;
                var w1 = ((cx - px) * (ay - py) - (cy - py) * (ax - px)) * inv;
                var w2 = 1 - w0 - w1;
                if (w0 < -1e-6 || w1 < -1e-6 || w2 < -1e-6) continue;

                var idx = y * _bw + x;
                var z = (float)(w0 * _sz[i0] + w1 * _sz[i1] + w2 * _sz[i2]);
                if (z >= _zbuf[idx]) continue;

                var u = w0 * _su[i0] + w1 * _su[i1] + w2 * _su[i2];
                var v = w0 * _sv[i0] + w1 * _sv[i1] + w2 * _sv[i2];
                var fu = u - Math.Floor(u);
                var fv = v - Math.Floor(v);
                if (u < _umin) u = _umin; else if (u > _umax) u = _umax;
                if (v < _vmin) v = _vmin; else if (v > _vmax) v = _vmax;

                var tx = Math.Clamp((int)u, 0, 63);
                var ty = Math.Clamp((int)v, 0, 63);
                var ti = (ty * 64 + tx) * 4;
                var alpha = _tex[ti + 3];
                var texId = (_curOverlay ? 4096 : 0) + ty * 64 + tx;

                int r, g, b;
                if (alpha >= 128)
                {
                    r = _tex[ti + 2]; g = _tex[ti + 1]; b = _tex[ti];
                }
                else if (_curOverlay)
                {
                    // Camada de fora vazia: um vidrinho claro mostra onde dá para pintar.
                    var below = _pix[idx];
                    if (below == 0) { r = g = b = 235; }
                    else
                    {
                        r = Math.Min(255, ((below >> 16) & 255) + 38);
                        g = Math.Min(255, ((below >> 8) & 255) + 38);
                        b = Math.Min(255, (below & 255) + 38);
                    }
                    var ghost = (((tx + ty) & 1) == 0) ? 0 : 14;
                    r = Math.Min(255, r + ghost); g = Math.Min(255, g + ghost); b = Math.Min(255, b + ghost);
                    // vidro: aplica a luz da face mas sem escurecer demais
                    r = (int)(r * (0.6 + 0.4 * _shade)); g = (int)(g * (0.6 + 0.4 * _shade)); b = (int)(b * (0.6 + 0.4 * _shade));
                    _zbuf[idx] = z;
                    _id[idx] = texId;
                    if (texId == _hoverId) { r = Math.Min(255, r + 70); g = Math.Min(255, g + 70); b = Math.Min(255, b + 70); }
                    _pix[idx] = (255 << 24) | (r << 16) | (g << 8) | b;
                    continue;
                }
                else
                {
                    // Base transparente (raro): mostra um quadriculado cinza para ainda dar para pintar.
                    r = g = b = ((tx + ty) & 1) == 0 ? 0xC8 : 0xA6;
                }

                var shade = _shade * (_curDim ? 0.72 : 1.0);
                r = Math.Min(255, (int)(r * shade));
                g = Math.Min(255, (int)(g * shade));
                b = Math.Min(255, (int)(b * shade));

                if (_showGrid && (fu < 0.07 || fv < 0.07))
                {
                    r = (int)(r * 0.82); g = (int)(g * 0.82); b = (int)(b * 0.82);
                }

                if (texId == _hoverId)
                {
                    r = Math.Min(255, r + 70); g = Math.Min(255, g + 70); b = Math.Min(255, b + 70);
                }

                _zbuf[idx] = z;
                _id[idx] = texId;
                _pix[idx] = (255 << 24) | (r << 16) | (g << 8) | b;
            }
        }
    }

    // ---------- Descobrir o pixel da skin debaixo do mouse ----------

    private int IdAt(Point p)
    {
        if (_bmp == null || ActualWidth < 1 || ActualHeight < 1) return -1;
        var x = (int)(p.X * _bw / ActualWidth);
        var y = (int)(p.Y * _bh / ActualHeight);
        if (x < 0 || y < 0 || x >= _bw || y >= _bh) return -1;
        return _id[y * _bw + x];
    }

    private PartHit? MakeHit(int id)
    {
        if (id < 0) return null;
        var overlay = id >= 4096;
        var idx = id & 4095;
        int x = idx & 63, y = idx >> 6;

        foreach (var f in _faces)
        {
            if (f.Overlay != overlay) continue;
            if (x >= f.U0 && x < f.U1 && y >= f.V0 && y < f.V1)
                return new PartHit(x, y, overlay, f.Name, (int)f.U0, (int)f.V0, (int)f.U1, (int)f.V1);
        }
        return new PartHit(x, y, overlay, "", 0, 0, 64, 64);
    }

    // ---------- Mouse ----------

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        var p = e.GetPosition(this);
        var id = IdAt(p);
        if (id < 0) return;

        CaptureMouse();
        _painting = true;
        _lastPos = p;
        _lastPaintId = id;
        if (MakeHit(id) is { } hit) PaintBegin?.Invoke(hit);
        e.Handled = true;
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        if (!_painting) return;
        _painting = false;
        ReleaseMouseCapture();
        PaintEnd?.Invoke();
        e.Handled = true;
    }

    protected override void OnMouseRightButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseRightButtonDown(e);
        CaptureMouse();
        _rotating = true;
        _moved = false;
        _downPos = _lastPos = e.GetPosition(this);
        _startYaw = _yaw;
        _startPitch = _pitch;
        e.Handled = true;
    }

    protected override void OnMouseRightButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseRightButtonUp(e);
        if (!_rotating) return;
        _rotating = false;
        ReleaseMouseCapture();

        // Clique rápido com o botão direito (sem arrastar) = conta-gotas.
        if (!_moved && MakeHit(IdAt(e.GetPosition(this))) is { } hit) PickRequested?.Invoke(hit);
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var p = e.GetPosition(this);

        if (_rotating)
        {
            if (Math.Abs(p.X - _downPos.X) + Math.Abs(p.Y - _downPos.Y) > 4) _moved = true;
            if (_moved)
            {
                _yaw = _startYaw + (p.X - _downPos.X) * 0.7;
                _pitch = Math.Clamp(_startPitch + (p.Y - _downPos.Y) * 0.7, -89, 89);
                _hoverId = -1;
                QueueRender();
            }
            return;
        }

        if (_painting)
        {
            // Anda do ponto anterior até o atual de pixel em pixel da tela, para o traço não ter buracos.
            var dx = p.X - _lastPos.X;
            var dy = p.Y - _lastPos.Y;
            var steps = Math.Clamp((int)Math.Ceiling(Math.Max(Math.Abs(dx), Math.Abs(dy))), 1, 800);
            for (var i = 1; i <= steps; i++)
            {
                var q = new Point(_lastPos.X + dx * i / steps, _lastPos.Y + dy * i / steps);
                var id = IdAt(q);
                if (id < 0 || id == _lastPaintId) continue;
                _lastPaintId = id;
                if (MakeHit(id) is { } hit) PaintMove?.Invoke(hit);
            }
            _lastPos = p;
            return;
        }

        var hov = IdAt(p);
        if (hov != _hoverId)
        {
            _hoverId = hov;
            HoverChanged?.Invoke(MakeHit(hov));
            QueueRender();
        }
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        base.OnMouseLeave(e);
        if (_painting || _rotating || _hoverId < 0) return;
        _hoverId = -1;
        HoverChanged?.Invoke(null);
        QueueRender();
    }

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        base.OnMouseWheel(e);
        _zoom = Math.Clamp(_zoom * Math.Pow(1.0012, e.Delta), 0.5, 3.0);
        _hoverId = -1;
        QueueRender();
        e.Handled = true;
    }
}
