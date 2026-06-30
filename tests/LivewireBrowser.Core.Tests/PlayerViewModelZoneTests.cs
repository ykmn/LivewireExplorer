using LivewireBrowser.App.ViewModels;
using Xunit;

namespace LivewireBrowser.Core.Tests;

public class PlayerViewModelZoneTests
{
    // Regression coverage for "only the exceeded segment should change color, not the whole
    // bar": each zone's Lit/Unlit fractions must sum to that zone's fixed span, and only the
    // zone(s) actually reached by the current reading should have a non-zero Lit fraction.

    [Fact]
    public void TruePeakZones_SilentLevel_AllZonesFullyUnlit()
    {
        var player = new PlayerViewModel();
        player.TruePeakCurrent = double.NegativeInfinity;

        var zones = player.TruePeakZones;

        Assert.Equal(0, zones.GreenLit, precision: 3);
        Assert.Equal(0, zones.YellowLit, precision: 3);
        Assert.Equal(0, zones.RedLit, precision: 3);
        AssertSumsToOne(zones);
    }

    [Fact]
    public void TruePeakZones_LevelInGreenZone_OnlyGreenIsLit()
    {
        var player = new PlayerViewModel();
        player.TruePeakCurrent = -40; // well below the -18 yellow threshold

        var zones = player.TruePeakZones;

        Assert.True(zones.GreenLit > 0);
        Assert.Equal(0, zones.YellowLit, precision: 3);
        Assert.Equal(0, zones.RedLit, precision: 3);
        AssertSumsToOne(zones);
    }

    [Fact]
    public void TruePeakZones_LevelInYellowZone_GreenFullyLitAndYellowPartiallyLit()
    {
        var player = new PlayerViewModel();
        player.TruePeakCurrent = -10; // between -18 (yellow start) and -6 (red start)

        var zones = player.TruePeakZones;

        Assert.Equal(0, zones.GreenUnlit, precision: 3); // green zone fully lit (level passed it)
        Assert.True(zones.YellowLit > 0);
        Assert.Equal(0, zones.RedLit, precision: 3); // hasn't reached red yet
        AssertSumsToOne(zones);
    }

    [Fact]
    public void TruePeakZones_LevelAtFullScale_AllZonesFullyLit()
    {
        var player = new PlayerViewModel();
        player.TruePeakCurrent = 0;

        var zones = player.TruePeakZones;

        Assert.Equal(0, zones.GreenUnlit, precision: 3);
        Assert.Equal(0, zones.YellowUnlit, precision: 3);
        Assert.Equal(0, zones.RedUnlit, precision: 3);
        AssertSumsToOne(zones);
    }

    [Fact]
    public void MomentaryZones_UsesLoudnessThresholds_NotTruePeakThresholds()
    {
        var player = new PlayerViewModel();
        // -10 LUFS is in the red zone for loudness (>= -12) but would only be yellow for True Peak.
        player.MomentaryCurrent = -10;

        var zones = player.MomentaryZones;

        Assert.True(zones.RedLit > 0);
        AssertSumsToOne(zones);
    }

    private static void AssertSumsToOne(MeterZoneFractions zones)
    {
        var sum = zones.RedUnlit + zones.RedLit + zones.YellowUnlit + zones.YellowLit + zones.GreenUnlit + zones.GreenLit;
        Assert.Equal(1.0, sum, precision: 3);
    }
}
