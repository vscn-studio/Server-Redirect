namespace ServerRedirect;

[ProtoBuf.ProtoContract]
public sealed class ServerRedirectListRequest
{
}

[ProtoBuf.ProtoContract]
public sealed class ServerRedirectSelectRequest
{
    [ProtoBuf.ProtoMember(1)]
    public string Name { get; set; } = string.Empty;
}

[ProtoBuf.ProtoContract]
public sealed class ServerRedirectExecutePacket
{
    [ProtoBuf.ProtoMember(1)]
    public string Host { get; set; } = string.Empty;

    [ProtoBuf.ProtoMember(2)]
    public string Name { get; set; } = string.Empty;
}

[ProtoBuf.ProtoContract]
public sealed class ServerRedirectListPacket
{
    [ProtoBuf.ProtoMember(1)]
    public ServerRedirectEntry[] Entries { get; set; } = [];
}

[ProtoBuf.ProtoContract]
public sealed class ServerRedirectEntry
{
    [ProtoBuf.ProtoMember(1)]
    public string Host { get; set; } = string.Empty;

    [ProtoBuf.ProtoMember(2)]
    public string Name { get; set; } = string.Empty;
}
