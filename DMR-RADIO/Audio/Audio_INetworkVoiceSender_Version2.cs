namespace DmrRadio.Audio
{
    /// <summary>
    /// Abstraction you must implement to send fully formed DMR voice bursts over existing network code.
    /// The agent will later wire this to the repo's DMR network sender.
    /// </summary>
    public interface INetworkVoiceSender
    {
        /// <summary>
        /// Send a full DMR voice burst payload (constructed from 3x 9-byte AMBE frames + headers as needed by your network stack).
        /// Implement this method to call into the project's existing network code.
        /// </summary>
        void SendVoiceBurst(byte[] dmrVoiceBurst);
    }
}