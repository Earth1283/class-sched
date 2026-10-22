using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Printing;
using System.Windows;
using System.Windows.Controls;
using ClassSchedule.Core;
using Microsoft.Win32;

namespace ClassSchedule.Views;

public partial class MainWindow
{
    private const double PdfDpi = 240;
    private const double DefaultPageWidth = 1056;
    private const double DefaultPageHeight = 816;

    /// <summary>The week on screen, or the week around the day in Day view.</summary>
    private IReadOnlyList<DateTime> PrintDates()
    {
        var start = mode == ViewMode.Day ? ScheduleQueries.WeekStart(dayDate) : WeekStart;
        return ScheduleQueries.VisibleDays(data).Select(d => start.AddDays(DayParser.Order(d))).ToList();
    }

    private void OnPrint(object sender, RoutedEventArgs e)
    {
        MorePopup.IsOpen = false;
        PrintWeek();
    }

    private void OnSavePdf(object sender, RoutedEventArgs e)
    {
        MorePopup.IsOpen = false;
        SaveWeekAsPdf();
    }

    private bool HasSomethingToPrint()
    {
        if (data.Sessions.Count > 0) return true;
        ShowToast("Nothing to print yet", "Add a class first.");
        return false;
    }

    private void PrintWeek()
    {
        if (!HasSomethingToPrint()) return;

        var dates = PrintDates();
        try
        {
            var dialog = new PrintDialog();
            dialog.PrintTicket.PageOrientation = PageOrientation.Landscape;
            if (dialog.ShowDialog() != true) return;

            var page = dialog.PrintableAreaWidth > 0 && dialog.PrintableAreaHeight > 0
                ? new Size(dialog.PrintableAreaWidth, dialog.PrintableAreaHeight)
                : new Size(DefaultPageWidth, DefaultPageHeight);
            dialog.PrintVisual(PrintSheet.Build(data, dates, page), $"{data.Settings.TermName}, {PrintSheet.RangeText(dates)}");
            ShowToast("Sent to the printer", PrintSheet.RangeText(dates));
        }
        catch (Exception ex) when (ex is PrintSystemException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            ShowToast("The week could not be printed", $"{ex.Message} “Save this week as PDF” works without a printer.");
        }
    }

    private void SaveWeekAsPdf()
    {
        if (!HasSomethingToPrint()) return;

        var dates = PrintDates();
        var dialog = new SaveFileDialog
        {
            Title = "Save this week as PDF",
            Filter = "PDF (*.pdf)|*.pdf",
            FileName = $"{data.Settings.TermName} {dates[0]:yyyy-MM-dd}.pdf",
            AddExtension = true,
            DefaultExt = "pdf",
            OverwritePrompt = true
        };
        if (dialog.ShowDialog(this) != true) return;

        try
        {
            var (widthPt, heightPt) = PdfWriter.LandscapePage(!PdfWriter.UsesLetter(RegionInfo.CurrentRegion.TwoLetterISORegionName));
            var page = new Size(widthPt * 96 / 72, heightPt * 96 / 72);
            var (pixelsWide, pixelsHigh, rgb) = PrintSheet.Rasterize(PrintSheet.Build(data, dates, page), page, PdfDpi);

            using (var file = new FileStream(dialog.FileName, FileMode.Create, FileAccess.Write))
                PdfWriter.WriteImagePage(file, pixelsWide, pixelsHigh, rgb, widthPt, heightPt, $"{data.Settings.TermName}, {PrintSheet.RangeText(dates)}");
            ShowToast("Saved as PDF", dialog.FileName);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ShowToast("The PDF could not be saved", ex.Message);
        }
        finally
        {
            MemoryTrim.Run();
        }
    }
}
