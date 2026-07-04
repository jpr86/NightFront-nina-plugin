using System;

namespace JeffRidder.NINA.Nightfront.Import {

    /// <summary>
    /// Thrown when a NightFront plan file is present but cannot be turned into live NINA sequence
    /// instructions (malformed JSON, an unsupported instruction type, or a referenced filter that
    /// doesn't exist in the current profile's filter wheel).
    /// </summary>
    public class NightFrontImportException : Exception {

        public NightFrontImportException(string message) : base(message) {
        }

        public NightFrontImportException(string message, Exception innerException) : base(message, innerException) {
        }
    }
}
