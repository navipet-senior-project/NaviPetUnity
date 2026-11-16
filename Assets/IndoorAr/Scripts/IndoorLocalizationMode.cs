namespace IndoorAr
{
    public enum IndoorLocalizationMode
    {
        /// <summary>The original MultiSet single-frame VPS flow.</summary>
        MultiSet = 1,
        /// <summary>Room signs first (SignFirstLocalizer); MultiSet only when no calibrated sign pose arrives in time.</summary>
        SignFirst = 2
    }
}
