using Android.App;
using Android.OS;
using Android.Print;
using Android.Print.Pdf;

namespace Icono.Mobile.Android;

/// <summary>Printing a PDF with the print dialog of Android (a printer, or "Save as PDF"): the PDF made by icono is handed to the print service as it is, so a page
/// keeps its real paper size and the drawing its scale when it is printed at 100 %.</summary>
internal static class PdfPrinter
{
    public static bool Print(Activity activity, string jobName, byte[] pdf)
    {
        try
        {
            if (activity.GetSystemService(global::Android.Content.Context.PrintService) is not PrintManager manager)
            {
                return false;
            }

            manager.Print(jobName, new PdfAdapter(jobName, pdf), new PrintAttributes.Builder()!.Build());
            return true;
        }
        catch (Java.Lang.Exception)
        {
            return false;
        }
    }

    private sealed class PdfAdapter(string name, byte[] pdf) : PrintDocumentAdapter
    {
        public override void OnLayout(PrintAttributes? oldAttributes, PrintAttributes? newAttributes, CancellationSignal? cancellationSignal, LayoutResultCallback? callback, Bundle? extras)
        {
            if (cancellationSignal?.IsCanceled == true)
            {
                callback?.OnLayoutCancelled();
                return;
            }

            callback?.OnLayoutFinished(new PrintDocumentInfo.Builder(name + ".pdf")!.SetContentType(PrintContentType.Document).Build(), true);
        }

        public override void OnWrite(PageRange[]? pages, ParcelFileDescriptor? destination, CancellationSignal? cancellationSignal, WriteResultCallback? callback)
        {
            try
            {
                if (destination is null)
                {
                    callback?.OnWriteFailed("No destination.");
                    return;
                }

                // the file descriptor of the print service: written through the stream Android gives for it
                using var output = new Java.IO.FileOutputStream(destination.FileDescriptor);
                output.Write(pdf);
                output.Flush();
                callback?.OnWriteFinished([PageRange.AllPages]);
            }
            catch (Exception ex) when (ex is IOException or Java.Lang.Exception)
            {
                callback?.OnWriteFailed(ex.Message);
            }
        }
    }
}
