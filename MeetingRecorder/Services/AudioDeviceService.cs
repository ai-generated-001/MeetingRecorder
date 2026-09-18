using System;
using System.Collections.Generic;
using System.Diagnostics;
using MeetingRecorder.Models;
using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;

namespace MeetingRecorder.Services;

public class AudioDeviceService : IAudioDeviceService, IMMNotificationClient
{
    private readonly MMDeviceEnumerator _enumerator;
    private bool _isDisposed;

    public event EventHandler? DevicesChanged;
    public event EventHandler? DefaultDeviceChanged;

    public AudioDeviceService()
    {
        _enumerator = new MMDeviceEnumerator();
        try
        {
            _enumerator.RegisterEndpointNotificationCallback(this);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[AudioDeviceService] Failed to register notification callback: {ex.Message}");
        }
    }

    public string GetDefaultMicrophoneName()
    {
        try
        {
            var device = GetDefaultDeviceInternal();
            return device?.FriendlyName ?? "";
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[AudioDeviceService] Error querying default mic name: {ex.Message}");
            return "";
        }
    }

    public string? GetDefaultMicrophoneId()
    {
        try
        {
            var device = GetDefaultDeviceInternal();
            return device?.ID;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[AudioDeviceService] Error querying default mic ID: {ex.Message}");
            return null;
        }
    }

    public IReadOnlyList<AudioDeviceItem> GetAvailableMicrophones()
    {
        var defaultName = GetDefaultMicrophoneName();
        string defaultLabel = !string.IsNullOrWhiteSpace(defaultName)
            ? string.Format(Resources.MicrophoneDefaultWithDevice, defaultName)
            : Resources.MicrophoneDefault;

        var list = new List<AudioDeviceItem>
        {
            new AudioDeviceItem(defaultLabel, "")
        };

        try
        {
            var endpoints = _enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active);
            foreach (var device in endpoints)
            {
                list.Add(new AudioDeviceItem(device.FriendlyName, device.ID));
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[AudioDeviceService] Failed to enumerate capture devices: {ex.Message}");
        }

        return list;
    }

    private MMDevice? GetDefaultDeviceInternal()
    {
        try
        {
            return _enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Communications);
        }
        catch
        {
            try
            {
                return _enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Console);
            }
            catch
            {
                return null;
            }
        }
    }

    public void OnDefaultDeviceChanged(DataFlow flow, Role role, string defaultDeviceId)
    {
        if (flow == DataFlow.Capture && (role == Role.Communications || role == Role.Console || role == Role.Multimedia))
        {
            DefaultDeviceChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public void OnDeviceStateChanged(string deviceId, DeviceState newState)
    {
        DevicesChanged?.Invoke(this, EventArgs.Empty);
    }

    public void OnDeviceAdded(string pwstrDeviceId)
    {
        DevicesChanged?.Invoke(this, EventArgs.Empty);
    }

    public void OnDeviceRemoved(string pwstrDeviceId)
    {
        DevicesChanged?.Invoke(this, EventArgs.Empty);
    }

    public void OnPropertyValueChanged(string pwstrDeviceId, PropertyKey key)
    {
        // Only refresh if the friendly name changed (PKEY_Device_FriendlyName: {a45c254e-df1c-4efd-8020-67d146a850e0}, 14)
        // Ignore volume/mute/session property changes
        if (key.formatId == new Guid("a45c254e-df1c-4efd-8020-67d146a850e0") && key.propertyId == 14)
        {
            DevicesChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public void Dispose()
    {
        if (!_isDisposed)
        {
            try
            {
                _enumerator.UnregisterEndpointNotificationCallback(this);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[AudioDeviceService] Failed to unregister notification callback: {ex.Message}");
            }
            _enumerator.Dispose();
            _isDisposed = true;
        }
    }
}
