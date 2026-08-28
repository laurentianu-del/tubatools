using TubaWinUi3.Services;

namespace TubaWinUi3.Tests;

public class MemoryManagerServiceTests
{
    [Fact]
    public void ParsePageFileEntry_SystemManaged()
    {
        var entry = MemoryManagerService.ParsePageFileEntry(@"C:\pagefile.sys 0 0");
        Assert.True(entry.SystemManaged);
        Assert.False(entry.Disabled);
        Assert.Equal("C", entry.DriveLetter);
    }

    [Fact]
    public void ParsePageFileEntry_Disabled()
    {
        var entry = MemoryManagerService.ParsePageFileEntry(@"D:\pagefile.sys 0");
        Assert.False(entry.SystemManaged);
        Assert.True(entry.Disabled);
        Assert.Equal("D", entry.DriveLetter);
    }

    [Fact]
    public void ParsePageFileEntry_Custom()
    {
        var entry = MemoryManagerService.ParsePageFileEntry(@"E:\pagefile.sys 4096 8192");
        Assert.False(entry.SystemManaged);
        Assert.False(entry.Disabled);
        Assert.Equal(4096, entry.InitialMB);
        Assert.Equal(8192, entry.MaximumMB);
    }

    [Fact]
    public void ParsePageFileEntry_EmptyLine_DefaultsToSystemManaged()
    {
        var entry = MemoryManagerService.ParsePageFileEntry("");
        Assert.True(entry.SystemManaged);
        Assert.False(entry.Disabled);
    }

    [Fact]
    public void ParsePageFileEntry_InvalidNumbers_TreatsAsDisabled()
    {
        var entry = MemoryManagerService.ParsePageFileEntry(@"C:\pagefile.sys abc def");
        Assert.True(entry.Disabled);
        Assert.False(entry.SystemManaged);
    }

    [Theory]
    [InlineData(0, "0 B")]
    [InlineData(1024, "1 KB")]
    [InlineData(5L * 1024 * 1024, "5.0 MB")]
    [InlineData(16L * 1024 * 1024 * 1024, "16.0 GB")]
    public void FormatBytes_FormatsCorrectly(long bytes, string expected)
    {
        Assert.Equal(expected, MemoryManagerService.FormatBytes(bytes));
    }

    [Fact]
    public void BytesToGb_ConvertsCorrectly()
    {
        Assert.Equal(1.0, MemoryManagerService.BytesToGb(1024L * 1024 * 1024), 3);
    }
}
