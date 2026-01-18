using CommunityToolkit.Mvvm.ComponentModel;

namespace VRCVideoCacher.Models;

public partial class EditableString : ObservableObject
{
    [ObservableProperty]
    private string _value = string.Empty;

    public EditableString(string value)
    {
        Value = value;
    }

    public static implicit operator string(EditableString es) => es.Value;
    public static implicit operator EditableString(string s) => new(s);
}
