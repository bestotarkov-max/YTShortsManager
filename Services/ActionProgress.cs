using System;

namespace YTShortsManager.Services
{
    /// <summary>IProgress, который не привязывается к SynchronizationContext.</summary>
    public sealed class ActionProgress : IProgress<double>
    {
        private readonly Action<double> _handler;
        public ActionProgress(Action<double> handler) => _handler = handler;
        public void Report(double value) => _handler(value);
    }
}
