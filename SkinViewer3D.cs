using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace MoonlightLauncher;

/// <summary>
/// Boneco 3D do Minecraft desenhado "à mão" (sem o Viewport3D do WPF, que variava de PC para PC).
/// Cada quadro é montado pixel a pixel num bitmap: a skin (64×64) é aplicada nas faces de cada peça,
/// a cabeça (e um pouco do corpo) acompanha o mouse, mesmo com o mouse fora do launcher,
/// e só desenha enquanto estiver visível.
/// </summary>
public sealed class SkinViewer3D : UserControl
{
    // Uma face (4 cantos) de uma peça do corpo, já com a região da skin que ela mostra.
    private sealed class Face
    {
        public readonly double[] X = new double[4];
        public readonly double[] Y = new double[4];
        public readonly double[] Z = new double[4];
        public double U0, V0, U1, V1;   // região da skin (em pixels)
        public double Shade;            // luz fixa por direção da face
        public int Kind;                // 0 cabeça, 1 corpo, 2 braço dir., 3 braço esq., 4 perna dir., 5 perna esq.
    }

    private readonly Image _image = new() { Stretch = Stretch.Fill, IsHitTestVisible = false };
    private readonly DispatcherTimer _timer = new(DispatcherPriority.Background);
    private readonly Stopwatch _clock = Stopwatch.StartNew();

    private WriteableBitmap? _bmp;
    private int[] _pix = Array.Empty<int>();
    private float[] _zbuf = Array.Empty<float>();
    private int _bw, _bh;

    private readonly List<Face> _faces = new();
    private byte[] _tex = SkinTools.PlaceholderPixels();
    private bool _paused;
    private bool _hasModel;
    private bool _slim;
    private bool _dirty = true;
    private bool _failed;
    private byte[]? _png;

    private double _nx, _ny;                    // posição suavizada do mouse (-1..1)
    private double _lastNx = 99, _lastNy = 99;
    private double _bodyYaw, _headYaw, _headPitch, _armR, _armL, _legR, _legL;
    private double _lastDrawAt = -10;       // segundo do último desenho (limita o balanço quando o mouse está parado)
    private double _popStart = -10;             // segundo em que a animação de "pulinho" começou

    // Vértices na tela do triângulo em desenho.
    private readonly double[] _sx = new double[4];
    private readonly double[] _sy = new double[4];
    private readonly double[] _sz = new double[4];
    private readonly double[] _su = new double[4];
    private readonly double[] _sv = new double[4];
    private double _umin, _umax, _vmin, _vmax, _shade;

    public SkinViewer3D()
    {
        Content = _image;
        Focusable = false;
        IsHitTestVisible = false;
        RenderOptions.SetBitmapScalingMode(_image, BitmapScalingMode.HighQuality);

        _timer.Tick += (_, _) =>
        {
            try { Tick(); }
            catch (Exception ex)
            {
                LauncherLog.Write("Boneco 3D falhou (movimento desligado)", ex);
                _failed = true;
                _timer.Stop();
            }
        };

        Loaded += (_, _) =>
        {
            Perf.Changed += OnPerfChanged;
            ApplyPerf();
            UpdateTimer();
        };
        Unloaded += (_, _) =>
        {
            Perf.Changed -= OnPerfChanged;
            _timer.Stop();
        };
        IsVisibleChanged += (_, _) => UpdateTimer();
        SizeChanged += (_, _) =>
        {
            EnsureBitmap();
            _dirty = true;
            SafeRender();
        };

        // Antes de receber uma skin, mostra o boneco "lunar" padrão.
        SetSkin(null, false);
    }

    /// <summary>A janela principal liga isso quando está minimizada/escondida: o 3D para de gastar CPU.</summary>
    public bool Paused
    {
        get => _paused;
        set
        {
            if (_paused == value) return;
            _paused = value;
            UpdateTimer();
        }
    }

    // ---------- Skin ----------

    /// <summary>png = arquivo da skin (64×64 ou 64×32); null mostra o boneco padrão.</summary>
    public void SetSkin(byte[]? png, bool slim)
    {
        if (_hasModel && _slim == slim && SameBytes(png, _png)) return;

        byte[] pixels;
        try
        {
            pixels = png == null ? SkinTools.PlaceholderPixels() : SkinTools.ToPixels(png);
        }
        catch (Exception ex)
        {
            LauncherLog.Write("Não consegui montar a skin em 3D", ex);
            pixels = SkinTools.PlaceholderPixels();
            png = null;
        }

        _tex = pixels;
        if (!_hasModel || _slim != slim) BuildFaces(slim);

        _hasModel = true;
        _slim = slim;
        _png = png;
        _failed = false;
        _dirty = true;

        if (!Perf.Light && IsLoaded) _popStart = _clock.Elapsed.TotalSeconds; // pulinho ao trocar de skin

        SafeRender();
        UpdateTimer();
    }

    private static bool SameBytes(byte[]? a, byte[]? b)
    {
        if (a == null || b == null) return a == null && b == null;
        return a.AsSpan().SequenceEqual(b);
    }

    // ---------- Montagem do boneco ----------

    private void BuildFaces(bool slim)
    {
        _faces.Clear();

        var aw = slim ? 3 : 4;
        var armX = 4 + aw / 2.0;

        // Camada de base primeiro, depois as de cima (chapéu, casaco...). Mesmas medidas do jogo.
        AddBox(0, 28, 8, 8, 8, 0, 0, 0, 0);
        AddBox(0, 18, 8, 12, 4, 0, 16, 16, 1);
        AddBox(-armX, 18, aw, 12, 4, 0, 40, 16, 2);
        AddBox(armX, 18, aw, 12, 4, 0, 32, 48, 3);
        AddBox(-2, 6, 4, 12, 4, 0, 0, 16, 4);
        AddBox(2, 6, 4, 12, 4, 0, 16, 48, 5);

        AddBox(0, 28, 8, 8, 8, 0.5, 32, 0, 0);
        AddBox(0, 18, 8, 12, 4, 0.25, 16, 32, 1);
        AddBox(-armX, 18, aw, 12, 4, 0.25, 40, 32, 2);
        AddBox(armX, 18, aw, 12, 4, 0.25, 48, 48, 3);
        AddBox(-2, 6, 4, 12, 4, 0.25, 0, 32, 4);
        AddBox(2, 6, 4, 12, 4, 0.25, 0, 48, 5);
    }

    // Caixa com o mapeamento da skin do Minecraft. (u, v) = canto da região na skin 64×64;
    // w, h, d = tamanho em pixels; infl = quanto a camada de cima "incha".
    // O personagem olha para +Z; o lado direito dele fica em -X (esquerda de quem vê).
    private void AddBox(double cx, double cy, double w, double h, double d, double infl, int u, int v, int kind)
    {
        double hx = w / 2 + infl, hy = h / 2 + infl, hz = d / 2 + infl;

        // Cantos vistos de fora: baixo-esquerda, baixo-direita, cima-direita, cima-esquerda.
        void Add(double[] xs, double[] ys, double[] zs, double u0, double v0, double u1, double v1, double shade)
        {
            var f = new Face { U0 = u0, V0 = v0, U1 = u1, V1 = v1, Shade = shade, Kind = kind };
            for (var i = 0; i < 4; i++)
            {
                f.X[i] = cx + xs[i];
                f.Y[i] = cy + ys[i];
                f.Z[i] = zs[i];
            }
            _faces.Add(f);
        }

        // frente (+Z)
        Add(new[] { -hx, hx, hx, -hx }, new[] { -hy, -hy, hy, hy }, new[] { hz, hz, hz, hz },
            u + d, v + d, u + d + w, v + d + h, 1.0);
        // costas (-Z)
        Add(new[] { hx, -hx, -hx, hx }, new[] { -hy, -hy, hy, hy }, new[] { -hz, -hz, -hz, -hz },
            u + 2 * d + w, v + d, u + 2 * d + 2 * w, v + d + h, 0.8);
        // lado direito do personagem (-X)
        Add(new[] { -hx, -hx, -hx, -hx }, new[] { -hy, -hy, hy, hy }, new[] { -hz, hz, hz, -hz },
            u, v + d, u + d, v + d + h, 0.72);
        // lado esquerdo do personagem (+X)
        Add(new[] { hx, hx, hx, hx }, new[] { -hy, -hy, hy, hy }, new[] { hz, -hz, -hz, hz },
            u + d + w, v + d, u + 2 * d + w, v + d + h, 0.86);
        // topo (+Y)
        Add(new[] { -hx, hx, hx, -hx }, new[] { hy, hy, hy, hy }, new[] { hz, hz, -hz, -hz },
            u + d, v, u + d + w, v + d, 1.0);
        // base (-Y)
        Add(new[] { -hx, hx, hx, -hx }, new[] { -hy, -hy, -hy, -hy }, new[] { -hz, -hz, hz, hz },
            u + d + w, v, u + d + 2 * w, v + d, 0.55);
    }

    // ---------- Seguir o mouse ----------

    private void OnPerfChanged() => Dispatcher.Invoke(() =>
    {
        ApplyPerf();
        UpdateTimer();
    });

    private void ApplyPerf()
    {
        _timer.Interval = TimeSpan.FromMilliseconds(Perf.Light ? 50 : 33);
        EnsureBitmap();
        _dirty = true;
        SafeRender();
    }

    private void UpdateTimer()
    {
        var run = IsLoaded && IsVisible && !_paused && !_failed;
        if (run && !_timer.IsEnabled) _timer.Start();
        else if (!run && _timer.IsEnabled) _timer.Stop();
    }

    private void Tick()
    {
        if (ActualWidth < 4 || ActualHeight < 4) return;
        if (!CursorUtil.TryGetPosition(this, out var p)) return;

        // A cabeça do boneco fica mais ou menos a 1/4 da altura.
        var nx = Math.Clamp((p.X - ActualWidth / 2) / 240.0, -1, 1);
        var ny = Math.Clamp((p.Y - ActualHeight * 0.24) / 240.0, -1, 1);

        var k = Perf.Light ? 0.35 : 0.22;
        _nx += (nx - _nx) * k;
        _ny += (ny - _ny) * k;

        var sway = !Perf.Light;
        var popping = _clock.Elapsed.TotalSeconds - _popStart < 0.5;
        var moved = Math.Abs(_nx - _lastNx) > 0.0015 || Math.Abs(_ny - _lastNy) > 0.0015;
        if (!moved && !sway && !popping && !_dirty) return; // parado: não redesenha nada

        // Só o balancinho (mouse parado)? Redesenha uns 10x por segundo; o olho não nota e a CPU agradece.
        var now = _clock.Elapsed.TotalSeconds;
        if (!moved && !popping && !_dirty && now - _lastDrawAt < 0.1) return;
        _lastDrawAt = now;

        _lastNx = _nx;
        _lastNy = _ny;

        _bodyYaw = _nx * 20;
        _headYaw = _nx * 30;
        _headPitch = _ny * 24; // positivo = olha para baixo

        if (sway)
        {
            var t = _clock.Elapsed.TotalSeconds * 1.7;
            var swing = Math.Sin(t) * 3.0;
            _armR = swing;
            _armL = -swing;
            _legR = -swing * 0.5;
            _legL = swing * 0.5;
        }

        SafeRender();
    }

    // ---------- Desenho ----------

    private void EnsureBitmap()
    {
        if (ActualWidth < 4 || ActualHeight < 4) return;

        var ss = Perf.Light ? 1.0 : 1.5; // no modo completo desenha 1,5x e reduz (bordas suaves, bem mais leve que 2x)
        var w = Math.Clamp((int)Math.Round(ActualWidth * ss), 16, 1600);
        var h = Math.Clamp((int)Math.Round(ActualHeight * ss), 16, 1600);
        if (_bmp != null && w == _bw && h == _bh) return;

        _bw = w;
        _bh = h;
        _bmp = new WriteableBitmap(w, h, 96, 96, PixelFormats.Pbgra32, null);
        _pix = new int[w * h];
        _zbuf = new float[w * h];
        _image.Source = _bmp;
    }

    private void SafeRender()
    {
        try { Render(); }
        catch (Exception ex)
        {
            if (!_failed) LauncherLog.Write("Boneco 3D: falha ao desenhar", ex);
            _failed = true;
            _timer.Stop();
        }
    }

    private static void RotX(ref double y, ref double z, double cy, double cz, double deg)
    {
        if (deg == 0) return;
        var a = deg * Math.PI / 180;
        double c = Math.Cos(a), s = Math.Sin(a);
        double dy = y - cy, dz = z - cz;
        y = cy + dy * c - dz * s;
        z = cz + dy * s + dz * c;
    }

    private static void RotY(ref double x, ref double z, double cx, double cz, double deg)
    {
        if (deg == 0) return;
        var a = deg * Math.PI / 180;
        double c = Math.Cos(a), s = Math.Sin(a);
        double dx = x - cx, dz = z - cz;
        x = cx + dx * c + dz * s;
        z = cz - dx * s + dz * c;
    }

    private void Render()
    {
        if (!_hasModel || _bmp == null || _bw < 16 || _bh < 16) return;

        Array.Clear(_pix, 0, _pix.Length);
        Array.Fill(_zbuf, float.MaxValue);

        // Pulinho (a skin cresce de leve ao trocar), só no modo completo.
        var scale = 1.0;
        var pt = (_clock.Elapsed.TotalSeconds - _popStart) / 0.42;
        if (!Perf.Light && pt >= 0 && pt < 1)
        {
            var e = pt - 1;
            scale = 0.86 + 0.14 * (1 + 2.0 * e * e * e + 1.0 * e * e);
        }

        // Câmera: olha para -Z, de longe, e o boneco sempre cabe na tela.
        const double dist = 80;
        var ppu = Math.Min(_bh / 38.0, _bw / 22.0);   // pixels por unidade do jogo
        var focal = ppu * dist;

        foreach (var f in _faces)
        {
            for (var i = 0; i < 4; i++)
            {
                double x = f.X[i], y = f.Y[i], z = f.Z[i];

                switch (f.Kind)
                {
                    case 0: // cabeça: inclina e gira em volta do pescoço
                        RotX(ref y, ref z, 24, 0, _headPitch);
                        RotY(ref x, ref z, 0, 0, _headYaw);
                        break;
                    case 2: RotX(ref y, ref z, 24, 0, _armR); break;
                    case 3: RotX(ref y, ref z, 24, 0, _armL); break;
                    case 4: RotX(ref y, ref z, 12, 0, _legR); break;
                    case 5: RotX(ref y, ref z, 12, 0, _legL); break;
                }

                RotY(ref x, ref z, 0, 0, _bodyYaw);      // o corpo todo gira um pouco
                x *= scale;
                y = 16 + (y - 16) * scale;
                z *= scale;

                var zc = dist - z;
                _sx[i] = _bw / 2.0 + x / zc * focal;
                _sy[i] = _bh / 2.0 - (y - 16) / zc * focal;
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
        _dirty = false;
    }

    // Desenha um triângulo com teste de profundidade; ignora as costas e os pixels transparentes.
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
                if (u < _umin) u = _umin; else if (u > _umax) u = _umax;
                if (v < _vmin) v = _vmin; else if (v > _vmax) v = _vmax;

                var ti = (Math.Clamp((int)v, 0, 63) * 64 + Math.Clamp((int)u, 0, 63)) * 4;
                if (_tex[ti + 3] < 128) continue; // pixel transparente da skin

                var r = Math.Min(255, (int)(_tex[ti + 2] * _shade));
                var g = Math.Min(255, (int)(_tex[ti + 1] * _shade));
                var b = Math.Min(255, (int)(_tex[ti] * _shade));

                _zbuf[idx] = z;
                _pix[idx] = (255 << 24) | (r << 16) | (g << 8) | b;
            }
        }
    }
}
