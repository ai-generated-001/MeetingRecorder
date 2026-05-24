using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace MeetingRecorder.Services;

public sealed class GlobalHotkeyService : IGlobalHotkeyService
{
    private readonly IntPtr _windowHandle;
    private readonly HashSet<int> _registeredIds = new();

    public event EventHandler<int>? HotkeyPressed;

    public GlobalHotkeyService(IntPtr windowHandle)
    {
        _windowHandle = windowHandle;
    }

    public bool Register(int id, uint modifiers, uint virtualKey)
    {
        var result = RegisterHotKey(_windowHandle, id, modifiers, virtualKey);
        if (result)
        {
            _registeredIds.Add(id);
        }

        return result;
    }

    public void Unregister(int id)
    {
        if (_registeredIds.Remove(id))
        {
            UnregisterHotKey(_windowHandle, id);
        }
    }

    public void NotifyHotkeyPressed(int hotkeyId)
    {
        HotkeyPressed?.Invoke(this, hotkeyId);
    }

    public void Dispose()
    {
        foreach (var id in _registeredIds)
        {
            UnregisterHotKey(_windowHandle, id);
        }

        _registeredIds.Clear();
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
}
