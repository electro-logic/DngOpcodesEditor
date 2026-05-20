using System;
using System.Threading.Tasks;

namespace DngOpcodesEditor;

// DNG ProfileHueSatMap (tags 50937 dims + 50938 data1 / 50939 data2 / 51107
// data3). A 3D look-up table indexed by HSV that gives a per-hue
// (hueShift°, satScale, valScale) tweak — the manufacturer's recipe for
// "punchy" rendering of saturated colours. DJI Phantom 4 ships a 6×6×3
// table; Mavic 3 Pro ships an 18×6×1 table.
//
// The DNG spec says to apply this in linear ProPhoto RGB / HSV between the
// camera-to-XYZ stage and the tone curve. We approximate by working in
// linear sRGB / HSV — close enough for a preview, much cheaper than a
// proper ProPhoto round-trip.
public class ProfileHueSatMap
{
    readonly int _hDiv, _sDiv, _vDiv;
    // Flat array indexed as [(v * _sDiv + s) * _hDiv + h) * 3 + c].
    readonly float[] _data;

    ProfileHueSatMap(int h, int s, int v, float[] data)
    {
        _hDiv = h; _sDiv = s; _vDiv = v; _data = data;
    }

    public static ProfileHueSatMap TryBuild(uint[] dims, double[] data)
    {
        if (dims == null || dims.Length < 3 || data == null) return null;
        int h = (int)dims[0], s = (int)dims[1], v = (int)dims[2];
        if (h <= 0 || s <= 0 || v <= 0) return null;
        if (data.Length != h * s * v * 3) return null;
        var flat = new float[data.Length];
        for (int i = 0; i < data.Length; i++) flat[i] = (float)data[i];
        return new ProfileHueSatMap(h, s, v, flat);
    }

    // Apply in place, in linear-sRGB HSV space.
    public void Apply(PixelBuffer img)
    {
        int W = img.Width, H = img.Height;
        Parallel.For(0, H, y =>
        {
            for (int x = 0; x < W; x++)
            {
                var px = img.GetRgb16Pixel(x, y);
                float r = px[0] / 65535f, g = px[1] / 65535f, b = px[2] / 65535f;
                RgbToHsv(r, g, b, out float h, out float s, out float v);
                Lookup(h, s, v, out float dh, out float satScale, out float valScale);
                h += dh;
                // Wrap hue back into [0, 360).
                h -= MathF.Floor(h / 360f) * 360f;
                s = Math.Clamp(s * satScale, 0f, 1f);
                v = Math.Clamp(v * valScale, 0f, 1f);
                HsvToRgb(h, s, v, out r, out g, out b);
                px[0] = (ushort)Math.Clamp(MathF.Round(r * 65535f), 0f, 65535f);
                px[1] = (ushort)Math.Clamp(MathF.Round(g * 65535f), 0f, 65535f);
                px[2] = (ushort)Math.Clamp(MathF.Round(b * 65535f), 0f, 65535f);
            }
        });
    }

    // Trilinear interpolation in the (hue, sat, value) cube. Hue wraps; sat
    // and value clamp at the edges.
    void Lookup(float hueDeg, float sat, float val, out float dh, out float ss, out float vs)
    {
        // h in [0, 360) -> [0, _hDiv) with circular wrap on the upper bound.
        float hf = hueDeg / 360f * _hDiv;
        if (hf < 0) hf += _hDiv;
        int hi0 = ((int)MathF.Floor(hf)) % _hDiv;
        if (hi0 < 0) hi0 += _hDiv;
        int hi1 = (hi0 + 1) % _hDiv;
        float hFrac = hf - MathF.Floor(hf);

        float sf = Math.Clamp(sat, 0f, 1f) * (_sDiv - 1);
        int si0 = (int)MathF.Floor(sf);
        int si1 = Math.Min(si0 + 1, _sDiv - 1);
        float sFrac = sf - si0;

        float vf = Math.Clamp(val, 0f, 1f) * (_vDiv - 1);
        int vi0 = (int)MathF.Floor(vf);
        int vi1 = Math.Min(vi0 + 1, _vDiv - 1);
        float vFrac = vf - vi0;

        float[] r = new float[3];
        for (int c = 0; c < 3; c++)
        {
            float c000 = Get(hi0, si0, vi0, c);
            float c001 = Get(hi1, si0, vi0, c);
            float c010 = Get(hi0, si1, vi0, c);
            float c011 = Get(hi1, si1, vi0, c);
            float c100 = Get(hi0, si0, vi1, c);
            float c101 = Get(hi1, si0, vi1, c);
            float c110 = Get(hi0, si1, vi1, c);
            float c111 = Get(hi1, si1, vi1, c);
            float hh0 = c000 + (c001 - c000) * hFrac;
            float hh1 = c010 + (c011 - c010) * hFrac;
            float hh2 = c100 + (c101 - c100) * hFrac;
            float hh3 = c110 + (c111 - c110) * hFrac;
            float ss0 = hh0 + (hh1 - hh0) * sFrac;
            float ss1 = hh2 + (hh3 - hh2) * sFrac;
            r[c] = ss0 + (ss1 - ss0) * vFrac;
        }
        dh = r[0]; ss = r[1]; vs = r[2];
    }

    float Get(int h, int s, int v, int c) =>
        _data[((v * _sDiv + s) * _hDiv + h) * 3 + c];

    static void RgbToHsv(float r, float g, float b, out float h, out float s, out float v)
    {
        float max = Math.Max(r, Math.Max(g, b));
        float min = Math.Min(r, Math.Min(g, b));
        v = max;
        float delta = max - min;
        s = max > 0f ? delta / max : 0f;
        if (delta <= 0f) { h = 0f; return; }
        if (max == r) h = (g - b) / delta;
        else if (max == g) h = 2f + (b - r) / delta;
        else h = 4f + (r - g) / delta;
        h *= 60f;
        if (h < 0f) h += 360f;
    }

    static void HsvToRgb(float h, float s, float v, out float r, out float g, out float b)
    {
        if (s <= 0f) { r = g = b = v; return; }
        float hh = h / 60f;
        int sextant = ((int)MathF.Floor(hh)) % 6;
        if (sextant < 0) sextant += 6;
        float f = hh - MathF.Floor(hh);
        float p = v * (1f - s);
        float q = v * (1f - s * f);
        float t = v * (1f - s * (1f - f));
        switch (sextant)
        {
            case 0: r = v; g = t; b = p; return;
            case 1: r = q; g = v; b = p; return;
            case 2: r = p; g = v; b = t; return;
            case 3: r = p; g = q; b = v; return;
            case 4: r = t; g = p; b = v; return;
            default: r = v; g = p; b = q; return;
        }
    }
}
