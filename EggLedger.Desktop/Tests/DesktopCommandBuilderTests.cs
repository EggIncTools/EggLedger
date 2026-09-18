using System.Runtime.InteropServices;
using EggLedger.Desktop.Platform;

namespace EggLedger.Desktop.Tests;

public sealed class DesktopCommandBuilderTests {
    [Fact]
    public void BuildOpenCommand_Windows_UsesExplorerWithPath() {
        var (exe, args) = DesktopCommandBuilder.BuildOpenCommand(OSPlatform.Windows, @"C:\data\file.json");
        Assert.Equal("explorer.exe", exe);
        Assert.Equal([@"C:\data\file.json"], args);
    }

    [Fact]
    public void BuildOpenCommand_Mac_UsesOpenWithPath() {
        var (exe, args) = DesktopCommandBuilder.BuildOpenCommand(OSPlatform.OSX, "/Users/me/file.json");
        Assert.Equal("open", exe);
        Assert.Equal(["/Users/me/file.json"], args);
    }

    [Fact]
    public void BuildOpenCommand_Linux_UsesXdgOpenWithPath() {
        var (exe, args) = DesktopCommandBuilder.BuildOpenCommand(OSPlatform.Linux, "/home/me/file.json");
        Assert.Equal("xdg-open", exe);
        Assert.Equal(["/home/me/file.json"], args);
    }

    [Fact]
    public void BuildOpenInFolderCommand_Windows_SelectsFileInExplorer() {
        var (exe, args) = DesktopCommandBuilder.BuildOpenInFolderCommand(OSPlatform.Windows, @"C:\data\file.json");
        Assert.Equal("explorer.exe", exe);
        Assert.Equal([@"/select,C:\data\file.json"], args);
    }

    [Fact]
    public void BuildOpenInFolderCommand_Mac_RevealsWithOpenDashR() {
        var (exe, args) = DesktopCommandBuilder.BuildOpenInFolderCommand(OSPlatform.OSX, "/Users/me/file.json");
        Assert.Equal("open", exe);
        Assert.Equal(["-R", "/Users/me/file.json"], args);
    }

    [Fact]
    public void BuildOpenInFolderCommand_Linux_OpensContainingDirectory() {
        var (exe, args) = DesktopCommandBuilder.BuildOpenInFolderCommand(OSPlatform.Linux, "/home/me/sub/file.json");
        Assert.Equal("xdg-open", exe);

        Assert.Single(args);
        Assert.EndsWith("file.json", "/home/me/sub/file.json");
        Assert.DoesNotContain("file.json", args[0]);
    }

    [Fact]
    public void BuildRestartCommand_RelaunchesExeWithNoArgs() {
        var (exe, args) = DesktopCommandBuilder.BuildRestartCommand(@"C:\app\EggLedger.exe");
        Assert.Equal(@"C:\app\EggLedger.exe", exe);
        Assert.Empty(args);
    }
}
