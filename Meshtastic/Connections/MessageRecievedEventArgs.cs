using Meshtastic.Data;
using Meshtastic.Protobufs;

namespace Meshtastic.Connections;

public class MessageRecievedEventArgs : EventArgs
{
#nullable disable
    public DeviceStateContainer DeviceStateContainer { get; set; }
    public FromRadio Message;
#nullable enable

    public MessageRecievedEventArgs()
    {
    }
}
