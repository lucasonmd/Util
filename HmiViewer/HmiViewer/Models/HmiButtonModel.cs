namespace HmiViewer.Models;

public class HmiButtonModel
{
    public string Label    { get; }
    public int    VirtualKey { get; }  // Win32 VK code

    public HmiButtonModel(string label, int virtualKey)
    {
        Label      = label;
        VirtualKey = virtualKey;
    }
}
