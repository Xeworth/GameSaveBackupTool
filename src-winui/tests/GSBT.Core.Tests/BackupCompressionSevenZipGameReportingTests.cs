using GSBT.Core.Services;

namespace GSBT.Core.Tests;

public sealed class BackupCompressionSevenZipGameReportingTests
{
    [Fact]
    public void ReportSevenZipEntryProgress_reports_every_folder_when_index_jumps()
    {
        var entries = new List<(string FullPath, string EntryName)>
        {
            ("", "Game A/save1.dat"),
            ("", "Game A/save2.dat"),
            ("", "Game B/save1.dat"),
            ("", "Game C/save1.dat"),
        };

        var reported = new List<string>();
        BackupCompressionService.ReportSevenZipEntryProgress(
            -1,
            2,
            entries,
            reported.Add,
            out var last);

        Assert.Equal(2, last);
        Assert.Equal(["Game A", "Game A", "Game B"], reported);
    }

    [Fact]
    public void ReportSevenZipEntryProgress_flushes_remaining_entries_at_end()
    {
        var entries = new List<(string FullPath, string EntryName)>
        {
            ("", "Alpha/x"),
            ("", "Beta/x"),
            ("", "Gamma/x"),
        };

        var reported = new List<string>();
        BackupCompressionService.ReportSevenZipEntryProgress(
            0,
            2,
            entries,
            reported.Add,
            out _);

        Assert.Equal(["Beta", "Gamma"], reported);
    }
}
