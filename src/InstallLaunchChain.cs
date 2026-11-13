using System;

namespace TitanGameHub
{
    /// <summary>
    /// Steam-style "one press" continuity: a single click on the hero button may start a
    /// download/install, and the game must start automatically as soon as the install is done.
    /// This is the intent token for that chain - it is consumed exactly once per request.
    /// </summary>
    public static class InstallLaunchChain
    {
        private static readonly object _lock = new object();
        private static string _pendingId = null;
        private static string _pendingName = null;
        private static DateTime _requestedAt = DateTime.MinValue;

        /// <summary>Marks that the game must be launched automatically after this install finishes.</summary>
        public static void RequestAutoLaunch(GameProfile gp)
        {
            if (gp == null) return;
            lock (_lock)
            {
                _pendingId = gp.Id ?? "";
                _pendingName = gp.Name ?? "";
                _requestedAt = DateTime.UtcNow;
            }
        }

        public static void CancelPending()
        {
            lock (_lock)
            {
                _pendingId = null;
                _pendingName = null;
                _requestedAt = DateTime.MinValue;
            }
        }

        public static bool HasPending
        {
            get
            {
                lock (_lock)
                {
                    return !string.IsNullOrEmpty(_pendingId) || !string.IsNullOrEmpty(_pendingName);
                }
            }
        }

        /// <summary>
        /// Returns true at most once for the matching profile, and clears the token.
        /// Requests older than 6 hours are treated as stale and dropped.
        /// </summary>
        public static bool ConsumeIfPending(GameProfile gp)
        {
            if (gp == null) return false;
            lock (_lock)
            {
                if (string.IsNullOrEmpty(_pendingId) && string.IsNullOrEmpty(_pendingName))
                    return false;

                if ((DateTime.UtcNow - _requestedAt).TotalHours > 6.0)
                {
                    _pendingId = null;
                    _pendingName = null;
                    return false;
                }

                bool idMatch = !string.IsNullOrEmpty(_pendingId) &&
                               string.Equals(_pendingId, gp.Id, StringComparison.OrdinalIgnoreCase);
                bool nameMatch = !string.IsNullOrEmpty(_pendingName) &&
                                 GameDeduplicationEngine.NormalizeGameTitle(_pendingName) ==
                                 GameDeduplicationEngine.NormalizeGameTitle(gp.Name);
                if (!idMatch && !nameMatch) return false;

                _pendingId = null;
                _pendingName = null;
                _requestedAt = DateTime.MinValue;
                return true;
            }
        }
    }
}
