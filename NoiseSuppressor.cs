namespace MoonlightLauncher;

// ====================================================================================
//  Supressão de ruído do microfone (ventilador, teclado ao fundo, chiado…), em tempo real.
//
//  Como funciona (sem nenhuma biblioteca extra):
//   • A voz de 16 kHz é cortada em janelas de 32 ms que se sobrepõem pela metade (FFT de 512 pontos).
//   • Para cada faixa de frequência o programa vai aprendendo o "nível do ruído de fundo"
//     (ele acompanha o que fica tocando quando ninguém fala).
//   • Cada faixa recebe um ganho (filtro de Wiener com "decision-directed"): onde só tem ruído o som
//     é abaixado ~22 dB; onde tem voz, o som passa quase intacto. Os ganhos são suavizados para não
//     soar como "robô"/"gotinhas".
//   • Graves abaixo de ~90 Hz (vento, batida na mesa) são cortados.
//  Atraso total: cerca de 16 ms.
// ====================================================================================
internal sealed class NoiseSuppressor
{
    private const int N = 512;                 // tamanho da janela (32 ms a 16 kHz)
    private const int H = N / 2;               // passo entre janelas
    private const int Bins = N / 2 + 1;
    private const int LogN = 9;                // 2^9 = 512

    private const double MinGain = 0.08;       // quanto o ruído pode ser abaixado (0,08 ≈ -22 dB)
    private const double NoiseBias = 1.5;      // margem em cima do ruído estimado (maior = corta mais)
    private const double Alpha = 0.96;         // peso da "memória" do quadro anterior (decision-directed)
    private const double Eps = 1e-9;
    private const int InitFrames = 24;         // ~0,4 s para aprender o ruído logo que o microfone abre

    private static readonly double[] Win = MakeWindow();
    private static readonly double[] Cos = MakeTrig(true);
    private static readonly double[] Sin = MakeTrig(false);
    private static readonly int[] Rev = MakeRev();

    private readonly double[] _re = new double[N], _im = new double[N];
    private readonly double[] _pow = new double[Bins];
    private readonly double[] _noise = new double[Bins];
    private readonly double[] _prevClean = new double[Bins];
    private readonly double[] _g = new double[Bins], _gs = new double[Bins];
    private readonly double[] _ola = new double[N];
    private double[] _acc = new double[N * 4];
    private int _accLen;
    private int _frame;

    /// <summary>Começa do zero (use ao ligar a supressão de novo).</summary>
    public void Reset()
    {
        Array.Clear(_noise); Array.Clear(_prevClean); Array.Clear(_ola);
        _accLen = 0; _frame = 0;
    }

    /// <summary>
    /// Limpa um trecho de áudio (PCM 16 bits, 16 kHz, mono). Devolve o áudio já limpo; no começo, e de vez em quando,
    /// pode vir um pouco menos ou mais amostras do que entraram (o resto fica guardado para a próxima chamada).
    /// </summary>
    public short[] Process(short[] input)
    {
        var n = input.Length;
        if (_accLen + n > _acc.Length) Array.Resize(ref _acc, _accLen + n + N);
        for (var i = 0; i < n; i++) _acc[_accLen + i] = input[i] / 32768.0;
        _accLen += n;

        var frames = _accLen >= N ? (_accLen - N) / H + 1 : 0;
        if (frames == 0) return Array.Empty<short>();

        var output = new short[frames * H];
        for (var f = 0; f < frames; f++) ProcessFrame(f * H, output, f * H);

        var used = frames * H;
        Array.Copy(_acc, used, _acc, 0, _accLen - used);
        _accLen -= used;
        return output;
    }

    private void ProcessFrame(int off, short[] output, int outOff)
    {
        for (var i = 0; i < N; i++) { _re[i] = _acc[off + i] * Win[i]; _im[i] = 0; }
        Fft(false);
        for (var k = 0; k < Bins; k++) _pow[k] = _re[k] * _re[k] + _im[k] * _im[k];

        // 1) Aprende o nível do ruído de fundo em cada faixa.
        if (_frame < InitFrames)
        {
            // Primeiros instantes: média simples (assume que o começo é só ruído; se não for, o passo 2 corrige depois).
            for (var k = 0; k < Bins; k++)
                _noise[k] = Math.Max(Eps, _frame == 0 ? _pow[k] : _noise[k] + (_pow[k] - _noise[k]) / (_frame + 1));
        }
        else
        {
            for (var k = 0; k < Bins; k++)
            {
                var p = _pow[k];
                var nz = _noise[k];
                // Perto do nível do ruído: segue a média. Muito acima (voz ou ruído que aumentou): sobe devagar,
                // e assim um ruído novo (ex.: ligou o ventilador) é aprendido em ~3 s sem "comer" a voz.
                nz = p < 4.0 * nz ? nz * 0.97 + p * 0.03 : nz * 1.005;
                _noise[k] = nz < Eps ? Eps : nz;
            }
        }
        _frame++;

        // 2) Ganho de cada faixa (Wiener + decision-directed).
        for (var k = 0; k < Bins; k++)
        {
            var nz = _noise[k] * NoiseBias;
            var post = _pow[k] / nz;
            var prior = Alpha * _prevClean[k] / nz + (1.0 - Alpha) * Math.Max(post - 1.0, 0.0);
            _g[k] = Math.Max(prior / (1.0 + prior), MinGain);
        }

        // 3) Suaviza entre faixas vizinhas e corta os graves.
        for (var k = 0; k < Bins; k++)
        {
            var a = _g[k == 0 ? 0 : k - 1];
            var c = _g[k == Bins - 1 ? Bins - 1 : k + 1];
            _gs[k] = (a + 2.0 * _g[k] + c) * 0.25;
        }
        _gs[0] = 0; _gs[1] = 0; _gs[2] = 0; // abaixo de ~90 Hz
        for (var k = 0; k < Bins; k++) _prevClean[k] = _gs[k] * _gs[k] * _pow[k];

        // 4) Aplica os ganhos (nas duas metades do espectro) e volta para o som.
        for (var k = 0; k < Bins; k++)
        {
            var g = _gs[k];
            _re[k] *= g; _im[k] *= g;
            if (k > 0 && k < N / 2) { _re[N - k] *= g; _im[N - k] *= g; }
        }
        Fft(true);

        for (var i = 0; i < N; i++) _ola[i] += _re[i] * Win[i];
        for (var i = 0; i < H; i++)
        {
            var v = _ola[i] * 32768.0;
            output[outOff + i] = (short)Math.Clamp(v, short.MinValue, short.MaxValue);
        }
        Array.Copy(_ola, H, _ola, 0, N - H);
        Array.Clear(_ola, N - H, H);
    }

    // ---- FFT (radix-2, no próprio lugar) ----

    private void Fft(bool inverse)
    {
        for (var i = 0; i < N; i++)
        {
            var j = Rev[i];
            if (j > i)
            {
                (_re[i], _re[j]) = (_re[j], _re[i]);
                (_im[i], _im[j]) = (_im[j], _im[i]);
            }
        }
        for (var len = 2; len <= N; len <<= 1)
        {
            var half = len >> 1;
            var step = N / len;
            for (var i = 0; i < N; i += len)
            {
                for (var k = 0; k < half; k++)
                {
                    var wr = Cos[k * step];
                    var wi = inverse ? Sin[k * step] : -Sin[k * step];
                    var a = i + k;
                    var b = a + half;
                    var xr = _re[b] * wr - _im[b] * wi;
                    var xi = _re[b] * wi + _im[b] * wr;
                    _re[b] = _re[a] - xr; _im[b] = _im[a] - xi;
                    _re[a] += xr; _im[a] += xi;
                }
            }
        }
        if (inverse)
        {
            for (var i = 0; i < N; i++) { _re[i] /= N; _im[i] /= N; }
        }
    }

    // sqrt(Hann): usada na entrada e na saída, a soma das duas janelas sobrepostas dá exatamente 1.
    private static double[] MakeWindow()
    {
        var w = new double[N];
        for (var i = 0; i < N; i++) w[i] = Math.Sin(Math.PI * i / N);
        return w;
    }

    private static double[] MakeTrig(bool cos)
    {
        var t = new double[N / 2];
        for (var k = 0; k < t.Length; k++)
        {
            var a = 2.0 * Math.PI * k / N;
            t[k] = cos ? Math.Cos(a) : Math.Sin(a);
        }
        return t;
    }

    private static int[] MakeRev()
    {
        var r = new int[N];
        for (var i = 0; i < N; i++)
        {
            int x = i, y = 0;
            for (var b = 0; b < LogN; b++) { y = (y << 1) | (x & 1); x >>= 1; }
            r[i] = y;
        }
        return r;
    }
}
