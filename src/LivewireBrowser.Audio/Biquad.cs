namespace LivewireBrowser.Audio;

/// <summary>Direct Form I biquad IIR filter, one instance per audio channel.</summary>
internal class Biquad
{
    private readonly float _b0, _b1, _b2, _a1, _a2;
    private float _x1, _x2, _y1, _y2;

    public Biquad(float b0, float b1, float b2, float a1, float a2)
    {
        _b0 = b0; _b1 = b1; _b2 = b2; _a1 = a1; _a2 = a2;
    }

    public float Process(float x)
    {
        var y = _b0 * x + _b1 * _x1 + _b2 * _x2 - _a1 * _y1 - _a2 * _y2;
        _x2 = _x1;
        _x1 = x;
        _y2 = _y1;
        _y1 = y;
        return y;
    }
}
