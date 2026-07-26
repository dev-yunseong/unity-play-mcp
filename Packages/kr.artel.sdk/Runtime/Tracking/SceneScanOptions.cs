namespace Artel.Tracking
{
    /// <summary>
    /// How much of a scene a scan is allowed to see.
    /// </summary>
    /// <remarks>
    /// <see cref="Default"/> is what <c>GAME_STATE</c> and the poller run: opted-in state only,
    /// active objects only. <see cref="Full"/> is the discovery mode behind
    /// <c>scan_all_scenes ["full"]</c> — every serialized field of the game's own behaviours, and
    /// inactive objects too. Nothing on the live-play path uses it.
    /// </remarks>
    internal readonly struct SceneScanOptions
    {
        public static readonly SceneScanOptions Default = new SceneScanOptions(false, false);
        public static readonly SceneScanOptions Full = new SceneScanOptions(true, true);

        /// <summary>
        /// Include every MonoBehaviour the game itself wrote, reading the fields Unity would
        /// serialize rather than only the ones marked with <see cref="ArtelStateAttribute"/>.
        /// </summary>
        public bool IncludeAllSerializedFields { get; }

        /// <summary>
        /// Walk into objects that are inactive. Their blocks carry <c>active: false</c>.
        /// </summary>
        public bool IncludeInactive { get; }

        private SceneScanOptions(bool includeAllSerializedFields, bool includeInactive)
        {
            IncludeAllSerializedFields = includeAllSerializedFields;
            IncludeInactive = includeInactive;
        }
    }
}
