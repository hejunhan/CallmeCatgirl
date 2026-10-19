using System;

namespace CallmeCatgirl.UI.SystemMenu
{
    public static class StartupWindowPolicy
    {
        public const int DefaultWidth = 1920, DefaultHeight = 1080;
        public static DisplayResolution FitClient(int width, int height, int workWidth, int workHeight, int frameWidth, int frameHeight, int margin = 8)
        {
            int availableWidth = workWidth - frameWidth - margin * 2;
            int availableHeight = workHeight - frameHeight - margin * 2;
            if (width <= 0 || height <= 0 || availableWidth < 2 || availableHeight < 2) throw new ArgumentOutOfRangeException();
            if (width <= availableWidth && height <= availableHeight) return new DisplayResolution(width, height);
            double ratio = Math.Min(1, Math.Min((double)availableWidth / width, (double)availableHeight / height));
            return new DisplayResolution(Math.Max(2, (int)Math.Floor(width * ratio / 2) * 2), Math.Max(2, (int)Math.Floor(height * ratio / 2) * 2));
        }
        public static (int x, int y) Center(int left, int top, int workWidth, int workHeight, int outerWidth, int outerHeight)
            => (left + Math.Max(0, (workWidth - outerWidth) / 2), top + Math.Max(0, (workHeight - outerHeight) / 2));
    }
}
