using NUnit.Framework;
using CallmeCatgirl.UI.SystemMenu;

public sealed class StartupWindowPolicyTests
{
    [Test] public void FullHdFitsLargerWorkAreaWithoutUpscaling()
    { var size = StartupWindowPolicy.FitClient(1920,1080,2560,1400,16,39); Assert.That(size.Width,Is.EqualTo(1920)); Assert.That(size.Height,Is.EqualTo(1080)); }
    [Test] public void FullHdDisplayReservesTitleBarAndTaskbar()
    { var size = StartupWindowPolicy.FitClient(1920,1080,1920,1040,16,39); Assert.That(size.Width+16,Is.LessThanOrEqualTo(1904)); Assert.That(size.Height+39,Is.LessThanOrEqualTo(1024)); Assert.That(size.Height,Is.LessThan(1080)); Assert.That((double)size.Width/size.Height,Is.EqualTo(16d/9).Within(.01)); }
    [Test] public void SavedSmallerWindowIsNotUpscaledToDefault()
    { var size = StartupWindowPolicy.FitClient(1280,720,2560,1400,16,39); Assert.That(size.Width,Is.EqualTo(1280)); Assert.That(size.Height,Is.EqualTo(720)); }
    [Test] public void SavedOddDimensionsAreNotRoundedWhenTheyAlreadyFit()
    { var size = StartupWindowPolicy.FitClient(1377,791,2560,1400,16,39); Assert.That(size.Width,Is.EqualTo(1377)); Assert.That(size.Height,Is.EqualTo(791)); }
    [Test] public void CenterUsesTheTargetMonitorsWorkAreaOrigin()
    { var point = StartupWindowPolicy.Center(-1920,40,1920,1040,1766,1023); Assert.That(point.x,Is.EqualTo(-1843)); Assert.That(point.y,Is.EqualTo(48)); }
}
