using System.Globalization;

namespace LocalDub.Models;

/// <summary>Combines a video's base narration speed with the usual fitting allowance.</summary>
internal readonly record struct NarrationPace(double BaseRatio, double AdditionalPreferredRatio, double AbsoluteMaximum)
{
    /// <summary>Highest total speed allowed before trying to shorten the text.</summary>
    public double PreferredRatio => Math.Min(BaseRatio * AdditionalPreferredRatio, AbsoluteMaximum);

    /// <summary>Returns the total playback speed, including the base speed even for short clips.</summary>
    public double RequiredRatio(double spokenSeconds, double slotSeconds) =>
        Math.Max(BaseRatio, spokenSeconds / slotSeconds);

    /// <summary>Reads a per-video multiplier, accepting both decimal dots and commas.</summary>
    public static double ParseBaseRatio(string? value, double maximum)
    {
        if (value is null)
        {
            return 1;
        }
        if (double.TryParse(value.Trim().Replace(',', '.'), NumberStyles.AllowDecimalPoint,
                CultureInfo.InvariantCulture, out var ratio) && ratio >= 1 && ratio <= maximum)
        {
            return ratio;
        }
        throw new ArgumentException($"Base narration speed must be between 1.00 and {maximum.ToString("0.##", CultureInfo.InvariantCulture)}.");
    }
}
