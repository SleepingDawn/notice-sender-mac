using System.Runtime.InteropServices;
using System.Text;
using Accessibility;

namespace NoticeSender.Automation;

// Read-only discovery of virtual native accessibility children; never invokes a default action.
internal static class MsaaInspection
{
    internal static IReadOnlyList<string> Read(nint root, int pid)
    {
        var lines = new List<string>();
        var seen = new HashSet<nint>();
        bool Owned(nint window) => window != 0 && Pid(window) == pid && (window == root || IsChild(root, window));
        if (!Owned(root)) throw new InvalidOperationException("MSAA root ownership changed.");
        var iid = new Guid("618736E0-3C3D-11CF-810C-00AA00389B71");
        int result = AccessibleObjectFromWindow(root, unchecked((uint)-4), ref iid, out var accessible);
        if (result < 0 || accessible == null) return new[] { $"  MSAA unavailable HRESULT 0x{result:X8}" };
        void Visit(IAccessible parent, object child, int depth, nint scope, bool transcriptProperties)
        {
            if (depth > 12 || lines.Count >= 512) return;
            if (!Owned(root)) throw new InvalidOperationException("MSAA root ownership changed.");
            WindowFromAccessibleObject(parent, out nint native);
            if (!Owned(native) || native != scope && !IsChild(scope, native)) { lines.Add("  MSAA object outside exact native root; skipped."); return; }
            try
            {
                string Property(Func<object?> read)
                {
                    try { return read()?.ToString() ?? ""; }
                    catch (COMException ex) { return $"<HRESULT 0x{ex.HResult:X8}>"; }
                }
                string optional = "";
                if (transcriptProperties)
                {
                    string ReadOptional(bool description)
                    {
                        try
                        {
                            var raw = (IAccessibleStringProperties)parent;
                            string? value;
                            int hr = description ? raw.GetDescription(child, out value) : raw.GetValue(child, out value);
                            return $"HRESULT 0x{hr:X8}; " + (value == null ? "<null>" : $"[{value}]");
                        }
                        catch (COMException ex) { return $"HRESULT 0x{ex.HResult:X8}"; }
                        catch (InvalidCastException ex) { return $"HRESULT 0x{ex.HResult:X8}; native property interface unavailable"; }
                    }
                    optional = $", value [{ReadOptional(false)}], description [{ReadOptional(true)}]";
                }
                lines.Add($"  MSAA depth {depth}, HWND {native}, child {child}, role {Property(() => parent.get_accRole(child))}, state {Property(() => parent.get_accState(child))}, name [{Property(() => parent.get_accName(child))}], default action [{Property(() => parent.get_accDefaultAction(child))}]{optional}");
                if (child is not int id || id != 0) return;
                nint identity = Marshal.GetIUnknownForObject(parent);
                bool fresh;
                try { fresh = seen.Add(identity); } finally { Marshal.Release(identity); }
                if (!fresh) return;
                int count = Math.Clamp(parent.accChildCount, 0, 256);
                if (count == 0) return;
                var children = new object[count];
                int hr = AccessibleChildren(parent, 0, count, children, out int obtained);
                if (hr < 0) { lines.Add($"  MSAA children HRESULT 0x{hr:X8}"); return; }
                for (int i = 0; i < Math.Min(obtained, count); i++)
                {
                    if (children[i] is IAccessible nested) Visit(nested, 0, depth + 1, scope, transcriptProperties);
                    else if (children[i] is int childId) Visit(parent, childId, depth + 1, scope, transcriptProperties);
                }
            }
            catch (COMException ex) { lines.Add($"  MSAA read HRESULT 0x{ex.HResult:X8}"); }
        }
        Visit(accessible, 0, 0, root, false);
        // The room's generic MSAA proxy need not enumerate the transcript's native client object.
        // Probe only the observed owned transcript profile; never guess virtual child IDs or inspect a composer.
        bool Transcript(nint window)
        {
            if (!Owned(window) || GetDlgCtrlID(window) != 100 || window != root && GetParent(window) != root) return false;
            var name = new StringBuilder(256);
            GetClassName(window, name, name.Capacity);
            return name.ToString().Equals("EVA_VH_ListControl_Dblclk", StringComparison.Ordinal);
        }
        var transcripts = new HashSet<nint>();
        if (Transcript(root)) transcripts.Add(root);
        EnumChildWindows(root, (window, _) => { if (Transcript(window)) transcripts.Add(window); return true; }, 0);
        if (transcripts.Count != 1) lines.Add($"  MSAA exact transcript Value/Description not read: native candidates {transcripts.Count}.");
        else
        {
            nint transcript = transcripts.Single();
            if (!Owned(root) || !Transcript(transcript)) throw new InvalidOperationException("MSAA transcript ownership changed.");
            int hr = AccessibleObjectFromWindow(transcript, unchecked((uint)-4), ref iid, out var client);
            lines.Add($"  MSAA exact transcript HWND {transcript}, OBJID_CLIENT HRESULT 0x{hr:X8}; Value/Description are read-only and are not delivery receipt.");
            if (hr >= 0 && client != null)
            {
                seen.Clear();
                if (lines.Count >= 512) lines.Add("  MSAA transcript property probe truncated by diagnostic limit.");
                else Visit(client, 0, 0, transcript, true);
            }
        }
        return lines;
    }
    // IAccessible's dual-interface prefix, in its native vtable order. PreserveSig retains
    // S_FALSE as well as failures; the usual Accessibility RCW hides success HRESULTs.
    // Only the final two read-only methods are called here.
    [ComImport, Guid("618736E0-3C3D-11CF-810C-00AA00389B71"), InterfaceType(ComInterfaceType.InterfaceIsDual)]
    private interface IAccessibleStringProperties
    {
        [PreserveSig, DispId(-5000)] int GetParent([MarshalAs(UnmanagedType.IDispatch)] out object? parent);
        [PreserveSig, DispId(-5001)] int GetChildCount(out int count);
        [PreserveSig, DispId(-5002)] int GetChild([MarshalAs(UnmanagedType.Struct)] object child, [MarshalAs(UnmanagedType.IDispatch)] out object? result);
        [PreserveSig, DispId(-5003)] int GetName([MarshalAs(UnmanagedType.Struct)] object child, [MarshalAs(UnmanagedType.BStr)] out string? value);
        [PreserveSig, DispId(-5004)] int GetValue([MarshalAs(UnmanagedType.Struct)] object child, [MarshalAs(UnmanagedType.BStr)] out string? value);
        [PreserveSig, DispId(-5005)] int GetDescription([MarshalAs(UnmanagedType.Struct)] object child, [MarshalAs(UnmanagedType.BStr)] out string? value);
    }
    private static int Pid(nint window) { GetWindowThreadProcessId(window, out int pid); return pid; }
    private delegate bool EnumChildCallback(nint window, nint parameter);
    [DllImport("user32.dll")] private static extern bool EnumChildWindows(nint parent, EnumChildCallback callback, nint parameter);
    [DllImport("user32.dll")] private static extern int GetDlgCtrlID(nint window);
    [DllImport("user32.dll")] private static extern nint GetParent(nint window);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetClassNameW")] private static extern int GetClassName(nint window, StringBuilder name, int capacity);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint window, out int pid);
    [DllImport("user32.dll")] private static extern bool IsChild(nint parent, nint child);
    [DllImport("oleacc.dll")] private static extern int AccessibleObjectFromWindow(nint window, uint objectId, ref Guid iid, [MarshalAs(UnmanagedType.Interface)] out IAccessible? accessible);
    [DllImport("oleacc.dll")] private static extern int WindowFromAccessibleObject(IAccessible accessible, out nint window);
    [DllImport("oleacc.dll")] private static extern int AccessibleChildren(IAccessible parent, int start, int count,
        [Out, MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 2)] object[] children, out int obtained);
}
