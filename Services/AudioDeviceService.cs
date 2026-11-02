using FreeScreenRecorder.Models;
using NAudio.CoreAudioApi;

namespace FreeScreenRecorder.Services;

public sealed class AudioDeviceService
{
    public IReadOnlyList<AudioDeviceInfo> GetMicrophones()
    {
        var devices = new List<AudioDeviceInfo>();

        try
        {
            using var enumerator = new MMDeviceEnumerator();
            foreach (var device in enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active))
            {
                using (device)
                {
                    devices.Add(new AudioDeviceInfo(device.ID, device.FriendlyName));
                }
            }
        }
        catch
        {
            // No audio endpoint or the audio service is unavailable.
        }

        return devices
            .OrderBy(d => d.FriendlyName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    public AudioDeviceInfo? GetDefaultMicrophone()
    {
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            using var device = enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Communications);
            return new AudioDeviceInfo(device.ID, device.FriendlyName);
        }
        catch
        {
            return null;
        }
    }
}
