namespace JeffRidder.NINA.Nightfront.Sequencer {

    /// <summary>
    /// How a NightFrontSeeingTrigger compares a sampled FWHM reading (arcsec) against its
    /// configured ThresholdArcsec.
    /// </summary>
    public enum SeeingComparator {
        LessThanOrEqual,
        GreaterThanOrEqual
    }
}
