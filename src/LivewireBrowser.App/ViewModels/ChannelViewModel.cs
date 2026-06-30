using CommunityToolkit.Mvvm.ComponentModel;
using LivewireBrowser.App.Localization;
using LivewireBrowser.Core.Models;

namespace LivewireBrowser.App.ViewModels;

public partial class ChannelViewModel : ObservableObject
{
    public ChannelInfo Channel { get; }

    public ChannelViewModel(ChannelInfo channel)
    {
        Channel = channel;
    }

    public int ChannelNumber => Channel.ChannelNumber;
    public int LwNumber => Channel.LwNumber;
    public string Name => Channel.Name;
    public string MulticastAddress => Channel.MulticastAddress;
    public int Port => Channel.Port;
    public bool IsActive => Channel.IsActive;

    public string LwDisplay => LwNumber > 0 ? LwNumber.ToString() : "—";

    // Not live-refreshed on a language switch (unlike DeviceViewModel.CategoryDisplay) —
    // ChannelViewModel instances are numerous (one per channel) and short-lived (recreated
    // on every scan/rescan), so subscribing each one to the static Loc.LanguageChanged event
    // isn't worth the cleanup complexity for what's a rare fallback (an unnamed channel) that
    // naturally picks up the new language on the next scan anyway.
    public string NameDisplay => string.IsNullOrWhiteSpace(Name) ? Loc.Get("Str_NoChannelName") : Name;
}
