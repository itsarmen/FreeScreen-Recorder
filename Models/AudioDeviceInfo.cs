namespace FreeScreenRecorder.Models;

public sealed record AudioDeviceInfo(string Id, string FriendlyName)
{
    public override string ToString() => FriendlyName;
}
