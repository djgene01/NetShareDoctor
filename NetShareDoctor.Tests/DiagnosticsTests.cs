using System.Net;
using NetShareDoctor.Services;

namespace NetShareDoctor.Tests;

public class DiagnosticsTests
{
    [Theory]
    [InlineData("445", "TCP", true)]
    [InlineData("", "", true)]
    [InlineData("Any", "Any", true)]
    [InlineData("135, 445", "TCP", true)]
    [InlineData("400-500", "TCP", true)]
    [InlineData("446-500", "TCP", false)]
    [InlineData("139", "TCP", false)]
    [InlineData("445", "UDP", false)]
    public void PortCovers445(string ports, string protocol, bool expected) =>
        Assert.Equal(expected, Diagnostics.PortCovers445(ports, protocol));

    [Theory]
    [InlineData("192.168.1.10", "192.168.1.200", 24, true)]
    [InlineData("192.168.1.10", "192.168.2.10", 24, false)]
    [InlineData("10.0.5.1", "10.0.9.1", 16, true)]
    [InlineData("10.0.5.1", "10.0.9.1", 21, false)]
    [InlineData("10.0.5.1", "10.0.7.1", 22, true)]
    [InlineData("1.2.3.4", "9.9.9.9", 0, true)]
    public void SameNetwork(string a, string b, int prefix, bool expected) =>
        Assert.Equal(expected, Diagnostics.SameNetwork(IPAddress.Parse(a), IPAddress.Parse(b), prefix));

    [Fact]
    public void SameNetwork_DifferentFamilies_IsFalse() =>
        Assert.False(Diagnostics.SameNetwork(IPAddress.Parse("10.0.0.1"), IPAddress.IPv6Loopback, 8));

    [Theory]
    [InlineData("Any", "Private", true)]
    [InlineData("Domain, Private", "Private", true)]
    [InlineData("Public", "Private", false)]
    public void ProfileMatches(string profile, string category, bool expected) =>
        Assert.Equal(expected, Diagnostics.ProfileMatches(profile, category));

    [Theory]
    [InlineData("Any", true)]
    [InlineData("LocalSubnet", true)]
    [InlineData("LocalSubnet4", true)]
    [InlineData("192.168.1.1-192.168.1.100", true)]
    [InlineData("192.168.1.60-192.168.1.100", false)]
    [InlineData("192.168.1.0/24", true)]
    [InlineData("192.168.0.0/255.255.0.0", true)]
    [InlineData("10.0.0.0/255.0.0.0", false)]
    [InlineData("192.168.1.50", true)]
    [InlineData("192.168.1.51", false)]
    [InlineData("8.8.8.8, 192.168.1.50", true)]
    [InlineData("", false)]
    public void RemoteCovers(string remote, bool expected) =>
        Assert.Equal(expected, Diagnostics.RemoteCovers(remote, IPAddress.Parse("192.168.1.50")));

    [Fact]
    public void RemoteCovers_WithoutPeer_OnlyMatchesKeywords()
    {
        Assert.True(Diagnostics.RemoteCovers("Intranet", null));
        Assert.False(Diagnostics.RemoteCovers("192.168.1.0/24", null));
    }

    [Fact]
    public void MissingNtfs_EveryoneOnFolder_CoversAll() =>
        Assert.Empty(Diagnostics.MissingNtfsAccounts([("PC\\bob", "Full"), ("Everyone", "Read")], ["Everyone", "NT AUTHORITY\\SYSTEM"]));

    [Theory]
    [InlineData("BUILTIN\\Users")]
    [InlineData("NT AUTHORITY\\Authenticated Users")]
    public void MissingNtfs_WideGroupsCoverEveryone(string group) =>
        Assert.Empty(Diagnostics.MissingNtfsAccounts([("Everyone", "Read")], [group]));

    [Fact]
    public void MissingNtfs_MatchesWithoutDomain() =>
        Assert.Empty(Diagnostics.MissingNtfsAccounts([("BUILTIN\\Administrators", "Full"), ("PC\\bob", "Change")],
            ["BUILTIN\\Administrators", "PC\\Bob", "NT AUTHORITY\\SYSTEM"]));

    [Fact]
    public void MissingNtfs_ReportsAccountsWithoutAnEntry()
    {
        var missing = Diagnostics.MissingNtfsAccounts(
            [("Everyone", "Read"), ("PC\\bob", "Change"), ("PC\\bob", "Change"), ("BUILTIN\\Administrators", "Full")],
            ["BUILTIN\\Administrators", "NT AUTHORITY\\SYSTEM", "PC\\alice"]);
        Assert.Equal(new[] { ("Everyone", "Read"), ("PC\\bob", "Change") }, missing);
    }

    [Fact]
    public void NtfsGrantScript_UsesReadForReadShares_ModifyOtherwise()
    {
        Assert.Contains("(OI)(CI)RX'", Diagnostics.NtfsGrantScript(@"D:\Share", "Everyone", "Read"));
        Assert.Contains("(OI)(CI)M'", Diagnostics.NtfsGrantScript(@"D:\Share", "PC\\bob", "Full"));
        Assert.Contains("icacls.exe 'D:\\Bob''s files' /grant $g", Diagnostics.NtfsGrantScript(@"D:\Bob's files", "Everyone", "Change"));
    }
}
