using System.IO;
using NetShareDoctor.Models;
using NetShareDoctor.Services;

namespace NetShareDoctor.Tests;

public class RemoteTesterTests
{
    [Theory]
    [InlineData(@"\\pc\share", "pc", "share")]
    [InlineData(@"\\pc\share\sub\", "pc", @"share\sub")]
    [InlineData("smb://pc/share", "pc", "share")]
    [InlineData(@"\\192.168.1.20", "192.168.1.20", "")]
    [InlineData("  DESKTOP-1234 ", "DESKTOP-1234", "")]
    [InlineData("", "", "")]
    public void Split(string input, string host, string share) =>
        Assert.Equal((host, share), RemoteTester.Split(input));
}

public class ShellTests
{
    [Theory]
    [InlineData("WARNING: x\n{\"a\":1}\n", "{\"a\":1}")]
    [InlineData("[1,2]", "[1,2]")]
    [InlineData("noise [ {\"a\":[1]} ] trailing", "[ {\"a\":[1]} ]")]
    public void ExtractJson_FindsTheJson(string output, string expected) =>
        Assert.Equal(expected, Shell.ExtractJson(output));

    [Theory]
    [InlineData("no json here")]
    [InlineData("only { open")]
    public void ExtractJson_NoJson_ReturnsNull(string output) => Assert.Null(Shell.ExtractJson(output));

    [Theory]
    [InlineData("abc", "'abc'")]
    [InlineData("Bob's", "'Bob''s'")]
    [InlineData(null, "''")]
    public void Q_QuotesForPowerShell(string? value, string expected) => Assert.Equal(expected, Shell.Q(value));
}

public class NetInfoTests
{
    [Theory]
    [InlineData("System error 53 has occurred.", "Network path not found")]
    [InlineData("Systemfehler 53 aufgetreten.", "Network path not found")]
    [InlineData("Fehler 53", "Network path not found")]
    [InlineData("System error 1326 has occurred.", "Wrong username or password")]
    [InlineData("error 3227320323", "SMB signing mismatch")]
    public void Explain_ParsesErrorNumbersFromText(string text, string expectedStart) =>
        Assert.StartsWith(expectedStart, NetInfo.Explain(text));

    [Theory]
    [InlineData("The command completed successfully.")]
    [InlineData("System error 99999 has occurred.")]
    public void Explain_UnknownOrNoError_IsEmpty(string text) => Assert.Equal("", NetInfo.Explain(text));

    [Fact]
    public void Explain_Codes()
    {
        Assert.Equal(NetInfo.Explain(86), NetInfo.Explain(1326));
        Assert.StartsWith("You're already connected", NetInfo.Explain(1219));
        Assert.StartsWith("That drive letter", NetInfo.Explain(85));
        Assert.Equal("", NetInfo.Explain(0));
    }

    [Fact]
    public void Explain_NegativeCodeIsReadAsUnsigned() =>
        Assert.StartsWith("SMB signing mismatch", NetInfo.Explain(unchecked((int)0xC000006D)));
}

public class ConnectionStoreTests : IDisposable
{
    private readonly string _original = ConnectionStore.FilePath;
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "nsd-tests-" + Guid.NewGuid().ToString("N"));

    public ConnectionStoreTests() => ConnectionStore.FilePath = Path.Combine(_dir, "connections.json");

    public void Dispose()
    {
        ConnectionStore.FilePath = _original;
        if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
    }

    [Fact]
    public void Load_MissingFile_IsEmpty() => Assert.Empty(ConnectionStore.Load());

    [Fact]
    public void SaveThenLoad_RoundTrips_AndRaisesChanged()
    {
        var changed = 0;
        void OnChanged() => changed++;
        ConnectionStore.Changed += OnChanged;
        try
        {
            var c = new SavedConnection { Name = "Photos on nas", Host = "nas", Share = "Photos", UserName = @"nas\me", DriveLetter = "P:", Reconnect = false };
            ConnectionStore.Save([c]);

            var loaded = Assert.Single(ConnectionStore.Load());
            Assert.Equal(c.Id, loaded.Id);
            Assert.Equal(@"\\nas\Photos", loaded.UncPath);
            Assert.Equal(@"nas\me", loaded.UserName);
            Assert.Equal("P:", loaded.DriveLetter);
            Assert.False(loaded.Reconnect);
            Assert.Equal(1, changed);
            Assert.DoesNotContain("\"Password\"", File.ReadAllText(ConnectionStore.FilePath));
        }
        finally { ConnectionStore.Changed -= OnChanged; }
    }

    [Fact]
    public void Load_CorruptFile_IsEmpty()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(ConnectionStore.FilePath, "{ not json");
        Assert.Empty(ConnectionStore.Load());
    }
}
